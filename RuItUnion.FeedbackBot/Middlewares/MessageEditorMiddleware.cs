using RuItUnion.FeedbackBot.Data.Models;

namespace RuItUnion.FeedbackBot.Middlewares;

public class MessageEditorMiddleware(
    IOptions<AppOptions> options,
    ITelegramBotClient botClient,
    IFeedbackBotContext db,
    ILogger<MessageEditorMiddleware> logger,
    FeedbackMetricsService feedbackMetricsService) : FrameMiddleware
{
    private readonly long _chatId = options.Value.FeedbackChatId;

    public override async Task InvokeAsync(Update update, FrameContext context, CancellationToken ct = default)
    {
        if (update.EditedMessage is not null)
        {
            await ProcessEditedMessage(update.EditedMessage, context, ct).ConfigureAwait(false);
        }

        await Next(update, context, ct).ConfigureAwait(false);
    }

    private async Task ProcessEditedMessage(Message editedMessage, FrameContext context, CancellationToken ct = default)
    {
        if (editedMessage.Chat.Id == _chatId)
        {
            DbReply? reply = await db.Replies.AsNoTracking().Include(x => x.Topic).FirstOrDefaultAsync(x =>
                x.ChatThreadId == editedMessage.MessageThreadId
                && x.ChatMessageId == editedMessage.MessageId, ct).ConfigureAwait(false);
            if (reply is null)
            {
                logger.LogWarning(@"Reply {messageId} in topic {topicId} not found in DB",
                    editedMessage.MessageId, editedMessage.MessageThreadId);
            }
            else
            {
                if (reply.UserMessageId < 0)
                {
                    return;
                }

                await botClient.EditMessageText(reply.Topic.UserChatId, reply.UserMessageId,
                    editedMessage.Text!, cancellationToken: ct).ConfigureAwait(false);
                OnSuccess(editedMessage, reply);
            }
        }
        else
        {
            await botClient.SendMessage(
                    editedMessage.Chat.Id,
                    ResourceManager.GetString(nameof(MessageEditorMiddleware_NotSupported),
                        context.GetCultureInfo())!,
                    messageThreadId: editedMessage.MessageThreadId, cancellationToken: ct)
                .ConfigureAwait(false);
            logger.LogInformation(
                @"User {username} with id = {userId} tried to edit message {messageId} in chat {chatId}",
                editedMessage.From?.Username,
                editedMessage.From?.Id ?? 0L,
                editedMessage.Id,
                editedMessage.Chat.Id);
        }
    }

    protected virtual void OnSuccess(Message message, DbReply reply)
    {
        logger.LogInformation(@"Edited message {messageId} in chat {chatId}", reply.UserMessageId,
            reply.Topic.UserChatId);
        feedbackMetricsService.IncMessagesEdited(reply.ChatThreadId, message.From?.Id ?? 0L);
    }
}