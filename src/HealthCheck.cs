using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Telegram.Bot;
using Telegram.Bot.Requests;

namespace Bot;

[ExcludeFromCodeCoverage]
public class HealthCheck(TelegramBotClient _telegramBot, ILogger<HealthCheck> _logger) : IHealthCheck
{
    private readonly GetMeRequest _request = new();

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        bool isHealthy;

        try
        {
            var me = await _telegramBot.GetMeAsync(_request, cancellationToken);
            isHealthy = true;
        }
        catch (Exception e)
        {
            isHealthy = false;
            _logger.LogError(e, "Healthcheck failed.");
        }

        if (isHealthy)
        {
            return HealthCheckResult.Healthy("A healthy result.");
        }

        return HealthCheckResult.Unhealthy("An unhealthy result.");
    }
}