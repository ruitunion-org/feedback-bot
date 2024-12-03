using System.Reflection;
using Npgsql;
using OpenTelemetry.Metrics;
using RuItUnion.FeedbackBot.Data.Old;
using RuItUnion.FeedbackBot.Middlewares;
using RuItUnion.FeedbackBot.ServiceDefaults;
using TgBotFrame.Commands.Authorization.Extensions;
using TgBotFrame.Commands.Authorization.Interfaces;
using TgBotFrame.Commands.Help.Extensions;
using TgBotFrame.Commands.Injection;
using TgBotFrame.Commands.RateLimit.Middleware;
using TgBotFrame.Commands.RateLimit.Options;
using TgBotFrame.Commands.Start;
using TgBotFrame.Injection;
using TgBotFrame.Services;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AppOptions>(builder.Configuration.GetSection(nameof(AppOptions)));
builder.Services.AddSingleton<FeedbackMetricsService>();

builder.AddServiceDefaults();

builder.Services.AddOpenTelemetry().WithMetrics(providerBuilder =>
{
    string[] metrics =
    [
        @"Npgsql",
        @"TgBotFrame",
        @"TgBotFrame.Commands",
        @"FeedbackBot",
    ];

    providerBuilder.AddInstrumentation<FrameMetricsService>();
    providerBuilder.AddInstrumentation<FeedbackMetricsService>();
    providerBuilder.AddNpgsqlInstrumentation();

    providerBuilder.AddMeter(metrics);
}).WithTracing(providerBuilder => { providerBuilder.AddNpgsql(); });

string tgToken = builder.Configuration.GetConnectionString(@"Telegram")
                 ?? builder.Configuration[@$"{nameof(AppOptions)}:{nameof(AppOptions.FeedbackBotToken)}"]
                 ?? throw new KeyNotFoundException();
builder.Services.AddTelegramHttpClient();
builder.Services.AddSingleton<ITelegramBotClient, TelegramBotClient>(provider =>
{
    IHttpClientFactory factory = provider.GetRequiredService<IHttpClientFactory>();
    return new(tgToken, factory.CreateClient(nameof(ITelegramBotClient)));
});

builder.AddNpgsqlDataSource(@"RuItUnion-FeedbackBot-Database", settings =>
{
    settings.DisableMetrics = true;
    settings.DisableTracing = true;
    settings.DisableHealthChecks = false;
});

builder.Services.AddDbContext<FeedbackBotContext>((provider, optionsBuilder) =>
    optionsBuilder.UseNpgsql(provider.GetRequiredService<NpgsqlDataSource>()));
builder.Services.AddScoped<IAuthorizationData, FeedbackBotContext>();

bool useMigrator = !string.Equals(builder.Configuration[@"Migrator:EnableMigratorFromV01"], @"false",
    StringComparison.OrdinalIgnoreCase);
if (useMigrator)
{
    builder.Services.AddDbContext<OldDatabaseContext>((provider, optionsBuilder) =>
        optionsBuilder.UseNpgsql(provider.GetRequiredService<NpgsqlDataSource>()));
    builder.Services.AddScoped<Migrator>();
}

builder.Services.Configure<RateLimitOptions>(builder.Configuration.GetSection(@"RateLimit"));

builder.Services.AddTgBotFrameCommands(commandsBuilder =>
{
    commandsBuilder.TryAddCommandMiddleware<RateLimitMiddleware>();

    commandsBuilder.TryAddCommandMiddleware<AdminRoleSyncMiddleware>();
    commandsBuilder.AddAuthorization();

    commandsBuilder.TryAddCommandMiddleware<ThreadCommandFilterMiddleware>();
    commandsBuilder.TryAddCommandMiddleware<MessageCopierMiddleware>();
    commandsBuilder.TryAddCommandMiddleware<MessageForwarderMiddleware>();
    commandsBuilder.TryAddCommandMiddleware<MessageEditorMiddleware>();

    commandsBuilder.AddStartCommand(builder.Configuration[@$"{nameof(AppOptions)}:{nameof(AppOptions.Start)}"]
                                    ?? throw new KeyNotFoundException());
    commandsBuilder.AddHelpCommand();
    commandsBuilder.TryAddControllers(Assembly.GetEntryAssembly()!);
});

Uri tgUrl = new(@"https://api.telegram.org/bot" + tgToken + @"/getMe");
builder.Services.AddHealthChecks()
    .AddCheck<TelegramHealthCheck>(@"telegram");

WebApplication app = builder.Build();

app.MapDefaultEndpoints();

IOptions<AppOptions> appOptions = app.Services.GetRequiredService<IOptions<AppOptions>>();
if (appOptions.Value.FeedbackChatId == 0L)
{
    throw new InvalidOperationException(@"AppOptions:FeedbackChatId configuration value is 0");
}

if (useMigrator)
{
    IServiceScopeFactory scopeFactory = app.Services.GetRequiredService<IServiceScopeFactory>();
    await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
    Migrator migrator = scope.ServiceProvider.GetRequiredService<Migrator>();
    await migrator.Migrate(CancellationToken.None).ConfigureAwait(false);
}

if (string.Equals(builder.Configuration[@"Migrator:UpdateDatabase"], @"true",
        StringComparison.OrdinalIgnoreCase))
{
    IServiceScopeFactory scopeFactory = app.Services.GetRequiredService<IServiceScopeFactory>();
    await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
    FeedbackBotContext db = scope.ServiceProvider.GetRequiredService<FeedbackBotContext>();
    await db.Database.MigrateAsync().ConfigureAwait(false);
}

await app.RunAsync().ConfigureAwait(false);