using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;

namespace Bot;

[ExcludeFromCodeCoverage]
public class Worker(
    IOptions<AppOptions> _options,
    ILogger<Worker> _logger,
    TelegramBotClient _client,
    Handler _handler) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Команды доступные обычным пользователям бота
        await _client.SetMyCommandsAsync(
        [
            new () { Command = "start", Description = "Начать работу с ботом" },
            new () { Command = "help", Description = "Помощь" },
        ], cancellationToken: ct);

        // Команды доступные пользователям чата обратной связи
        await _client.SetMyCommandsAsync([
            new () { Command = "start", Description = "Начать работу с ботом" },
            new () { Command = "help", Description = "Помощь" },
            new () { Command = "delete", Description = "Удалить ответ" }
        ], BotCommandScope.Chat(_options.Value.FeedbackChatId), cancellationToken: ct);

        // Команды доступные администраторам чата обратной связи
        await _client.SetMyCommandsAsync([
            new () { Command = "start", Description = "Начать работу с ботом" },
            new () { Command = "help", Description = "Помощь" },
            new () { Command = "delete", Description = "Удалить ответ" },
            new () { Command = "ban", Description = "Забанить пользователя" },
            new () { Command = "unban", Description = "Разбанить пользователя" },
            new () { Command = "open", Description = "Открыть топик" },
            new () { Command = "close", Description = "Закрыть топик" },
        ], BotCommandScope.ChatAdministrators(_options.Value.FeedbackChatId), cancellationToken: ct);

        _logger.LogInformation("Telegram Bot started.");

        await _client.ReceiveAsync(
            (_, update, token) => _handler.HandleUpdate(update, token),
            (_, ex, token) => _handler.HandleError(ex, token),
            cancellationToken: ct);
    }
}