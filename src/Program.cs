using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Bot;
using Telegram.Bot;

var builder = WebApplication.CreateBuilder(args);

var services = builder.Services;
var config = builder.Configuration;

services.Configure<AppOptions>(config.GetSection(AppOptions.Name));
services.AddHealthChecks().AddCheck<HealthCheck>("Health");
services.AddSingleton<IRepository, Repository>();
services.AddSingleton<Repository.DatabaseContext>();
services.AddSingleton<IMessenger, Messenger>();
services.AddSingleton<Handler>();
services.AddHostedService<Worker>();
services.AddSingleton(x =>
{
    var token = x.GetRequiredService<IOptions<AppOptions>>().Value.FeedbackBotToken;
    return new TelegramBotClient(token);
});

var app = builder.Build();

using var db = app.Services.GetRequiredService<Repository.DatabaseContext>();
db.Database.Migrate();

app.MapHealthChecks("/health");

await app.RunAsync();

[ExcludeFromCodeCoverage]
public partial class Program { }