using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Telegram.Bot;

namespace Bot;

[ExcludeFromCodeCoverage]
public class HealthCheck(TelegramBotClient _telegramBot, ILogger<HealthCheck> _logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _telegramBot.GetMeAsync(cancellationToken);
            return HealthCheckResult.Healthy("A healthy result.");
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Healthcheck failed.");
            return HealthCheckResult.Unhealthy("An unhealthy result.");
        }
    }
}