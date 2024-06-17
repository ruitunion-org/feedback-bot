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
        await _client.SetMyCommandsAsync(new SetMyCommandsRequest()
        {
            Commands =
            [
                new () { Command = "start", Description = "Начать работу с ботом" },
                new () { Command = "help", Description = "Помощь" },
            ],
        }, ct);

        // Команды доступные пользователям чата обратной связи
        await _client.SetMyCommandsAsync(new SetMyCommandsRequest()
        {
            Commands =
            [
                new () { Command = "start", Description = "Начать работу с ботом" },
                new () { Command = "help", Description = "Помощь" },
                new () { Command = "delete", Description = "Удалить ответ" }
            ],
            Scope = new BotCommandScopeChat()
            {
                ChatId = _options.Value.FeedbackChatId
            }
        }, ct);

        // Команды доступные администраторам чата обратной связи
        await _client.SetMyCommandsAsync(new SetMyCommandsRequest()
        {
            Commands =
            [
                new () { Command = "start", Description = "Начать работу с ботом" },
                new () { Command = "help", Description = "Помощь" },
                new () { Command = "delete", Description = "Удалить ответ" },
                new () { Command = "ban", Description = "Забанить пользователя" },
                new () { Command = "unban", Description = "Разбанить пользователя" },
                new () { Command = "open", Description = "Открыть топик" },
                new () { Command = "close", Description = "Закрыть топик" },
            ],
            Scope = new BotCommandScopeChatAdministrators()
            {
                ChatId = _options.Value.FeedbackChatId
            }
        }, ct);

        _logger.LogInformation("Telegram Bot started.");

        await _client.ReceiveAsync(
            (_, update, ct) => _handler.HandleUpdate(update, ct),
            (_, ex, ct) => _handler.HandleError(ex, ct),
            cancellationToken: ct);
    }
}