using RuItUnion.FeedbackBot.Data.Models;

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
                    logger.LogWarning(@"Reply {messageId} in topic {topicId} not found in DB",
                        update.EditedMessage.MessageId, update.EditedMessage.MessageThreadId);
                }
                else
                {
                    await botClient.EditMessageText(reply.Topic.UserChatId, reply.UserMessageId,
                        update.EditedMessage.Text!, cancellationToken: ct).ConfigureAwait(false);
                    logger.LogInformation(@"Edited message {messageId} in chat {chatId}", reply.UserMessageId,
                        reply.Topic.UserChatId);
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
                logger.LogInformation(
                    @"User {username} with id = {userId} tried to edit message {messageId} in chat {chatId}",
                    update.EditedMessage.From?.Username,
                    update.EditedMessage.From?.Id ?? 0L,
                    update.EditedMessage.Id,
                    update.EditedMessage.Chat.Id);
            }
        }

        await Next(update, context, ct).ConfigureAwait(false);
    }
}