using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using RuItUnion.FeedbackBot.Data;
using RuItUnion.FeedbackBot.Data.Old;
using RuItUnion.FeedbackBot.Middlewares;
using RuItUnion.FeedbackBot.Options;
using RuItUnion.FeedbackBot.Services;
using Telegram.Bot;
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

builder.Services.AddOpenTelemetry().WithMetrics(providerBuilder =>
{
    string[] metrics =
    [
        @"System.Runtime",
        @"System.Net.NameResolution",
        @"System.Net.Http",
        @"Microsoft.Extensions.Diagnostics.ResourceMonitoring",
        @"Microsoft.Extensions.Diagnostics.HealthChecks",
        @"Microsoft.AspNetCore.Hosting",
        @"Microsoft.AspNetCore.Routing",
        @"Microsoft.AspNetCore.Diagnostics",
        @"Microsoft.AspNetCore.RateLimiting",
        @"Microsoft.AspNetCore.HeaderParsing",
        @"Microsoft.AspNetCore.Http.Connections",
        @"Microsoft.AspNetCore.Server.Kestrel",
        @"Microsoft.EntityFrameworkCore",
        @"Npgsql",
        @"TgBotFrame",
        @"TgBotFrame.Commands",
        @"FeedbackBot",
    ];

    providerBuilder.AddRuntimeInstrumentation();
    providerBuilder.AddHttpClientInstrumentation();
    providerBuilder.AddAspNetCoreInstrumentation();
    providerBuilder.AddInstrumentation<FrameMetricsService>();
    providerBuilder.AddInstrumentation<FeedbackMetricsService>();

    providerBuilder.AddPrometheusExporter();
    providerBuilder.AddOtlpExporter();

    providerBuilder.AddMeter(metrics);
}).WithTracing(providerBuilder =>
{
    providerBuilder.AddHttpClientInstrumentation();
    providerBuilder.AddAspNetCoreInstrumentation();
    providerBuilder.AddNpgsql();

    providerBuilder.AddOtlpExporter();
    if (builder.Environment.IsDevelopment())
    {
        providerBuilder.AddConsoleExporter();
    }
});

string tgToken = builder.Configuration.GetConnectionString(@"Telegram")
                 ?? builder.Configuration[@$"{nameof(AppOptions)}:{nameof(AppOptions.FeedbackBotToken)}"]
                 ?? throw new KeyNotFoundException();
builder.Services.AddTelegramHttpClient();
builder.Services.AddSingleton<ITelegramBotClient, TelegramBotClient>(provider =>
{
    IHttpClientFactory factory = provider.GetRequiredService<IHttpClientFactory>();
    return new(tgToken, factory.CreateClient(nameof(ITelegramBotClient)));
});

string dbString = builder.Configuration.GetConnectionString(@"Postgres")
                  ?? builder.Configuration[@$"{nameof(AppOptions)}:{nameof(AppOptions.DbConnectionString)}"]
                  ?? throw new KeyNotFoundException();
builder.Services.AddDbContext<FeedbackBotContext>(optionsBuilder => optionsBuilder.UseNpgsql(dbString));
builder.Services.AddScoped<IAuthorizationData, FeedbackBotContext>();
bool useMigrator = !string.Equals(builder.Configuration[@"Migrator:EnableMigratorFormV01"], @"false",
    StringComparison.OrdinalIgnoreCase);
if (useMigrator)
{
    builder.Services.AddScoped<Repository.DatabaseContext>(_ => new(dbString));
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

builder.Services.AddHealthChecks()
    .AddUrlGroup(new Uri(@"https://api.telegram.org/"), HttpMethod.Head)
    .AddNpgSql(dbString);

WebApplication app = builder.Build();

app.MapPrometheusScrapingEndpoint();
app.UseHealthChecks(@"/health");

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