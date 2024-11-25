using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RuItUnion.FeedbackBot.Data;
using RuItUnion.FeedbackBot.Data.Models;
using RuItUnion.FeedbackBot.Options;
using RuItUnion.FeedbackBot.Services;
using Telegram.Bot;
using Telegram.Bot.Types;
using TgBotFrame.Commands.Extensions;
using TgBotFrame.Middleware;

namespace RuItUnion.FeedbackBot.Middlewares;

public class MessageEditorMiddleware(
    IOptions<AppOptions> options,
    ITelegramBotClient botClient,
    FeedbackBotContext db,
    ILogger<MessageEditorMiddleware> logger,
    FeedbackMetricsService feedbackMetricsService) : FrameMiddleware
{
    private readonly long _chatId = options.Value.FeedbackChatId;

    public override async Task InvokeAsync(Update update, FrameContext context, CancellationToken ct = default)
    {
        if (update.EditedMessage is not null)
        {
            if (update.EditedMessage.Chat.Id == _chatId)
            {
                DbReply? reply = await db.Replies.AsNoTracking().Include(x => x.Topic).FirstOrDefaultAsync(x =>
                    x.ChatThreadId == update.EditedMessage.MessageThreadId
                    && x.ChatMessageId == update.EditedMessage.MessageId, ct).ConfigureAwait(false);
                if (reply is null)
                {
                    logger.LogWarning(@"Reply not found");
                }
                else
                {
                    await botClient.EditMessageText(reply.Topic.UserChatId, reply.UserMessageId,
                        update.EditedMessage.Text!, cancellationToken: ct).ConfigureAwait(false);
                    feedbackMetricsService.IncMessagesEdited(reply.ChatThreadId, update.EditedMessage.From?.Id ?? 0L);
                }
            }
            else
            {
                await botClient.SendMessage(
                        update.EditedMessage.Chat.Id,
                        ResourceManager.GetString(nameof(MessageEditorMiddleware_NotSupported),
                            context.GetCultureInfo())!,
                        messageThreadId: update.EditedMessage!.MessageThreadId, cancellationToken: ct)
                    .ConfigureAwait(false);
            }
        }

        await Next(update, context, ct).ConfigureAwait(false);
    }
}