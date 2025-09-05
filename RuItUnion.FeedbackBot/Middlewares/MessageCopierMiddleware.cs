using System.Collections.Frozen;
using RuItUnion.FeedbackBot.Data.Models;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types.Enums;

namespace RuItUnion.FeedbackBot.Middlewares;

public class MessageCopierMiddleware(
    IOptions<AppOptions> options,
    ITelegramBotClient botClient,
    IFeedbackBotContext db,
    FeedbackMetricsService feedbackMetricsService,
    ILogger<MessageCopierMiddleware> logger) : FrameMiddleware
{
    private static readonly FrozenSet<ReactionTypeEmoji> _highVoltageEmoji =
        new[] { new ReactionTypeEmoji { Emoji = @"⚡" } }.ToFrozenSet();

    private readonly long _chatId = options.Value.FeedbackChatId;

    public override async Task InvokeAsync(Update update, FrameContext context, CancellationToken ct = new())
    {
        if (string.IsNullOrEmpty(context.GetCommandName())
            && update.Message?.MessageThreadId is not null
            && update.Message.Chat.Id == _chatId
            && update.Message.ReplyToMessage is not null
            && update.Message.ReplyToMessage.From?.Username == context.GetBotUsername())
        {
            DbTopic? topic = await db.Topics.AsNoTracking()
                .FirstOrDefaultAsync(x => x.ThreadId == update.Message.MessageThreadId, ct).ConfigureAwait(false);
            if (topic?.ThreadId is null)
            {
                return;
            }

            MessageId result;
            try
            {
                result = await botClient.CopyMessage(topic.UserChatId, update.Message.Chat.Id,
                    update.Message.MessageId,
                    cancellationToken: ct).ConfigureAwait(false);
            }
            catch (ApiRequestException ex)
                when (ex.Message == @"Forbidden: bot was blocked by the user")

            {
                await botClient.SendMessage(_chatId,
                    ResourceManager.GetString(nameof(MessageCopier_BotBanned), context.GetCultureInfo())!,
                    messageThreadId: update.Message.MessageThreadId, replyParameters: new()
                    {
                        ChatId = _chatId,
                        MessageId = update.Message.MessageId,
                        AllowSendingWithoutReply = true,
                    }, cancellationToken: ct).ConfigureAwait(false);
                logger.LogInformation(@"Bot has been banned in chat with id = {chatId}", update.Message.Chat.Id);
                return;
            }

            await db.Replies.AddAsync(new()
            {
                ChatThreadId = topic.ThreadId,
                ChatMessageId = update.Message.MessageId,
                UserMessageId = result.Id,
            }, ct).ConfigureAwait(false);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await botClient.SetMessageReaction(update.Message.Chat.Id, update.Message.MessageId, _highVoltageEmoji,
                cancellationToken: ct).ConfigureAwait(false);
            await OnSuccess(update.Message, topic).ConfigureAwait(false);
        }

        await Next(update, context, ct).ConfigureAwait(false);
    }

    protected virtual ValueTask OnSuccess(Message message, DbTopic topic)
    {
        logger.LogInformation(@"Copied message {messageId} from topic {topicId} to chat with id = {userId}",
            message.MessageId,
            message.MessageThreadId,
            topic.UserChatId);
        feedbackMetricsService.IncMessagesCopied(topic.ThreadId, topic.UserChatId);
        return ValueTask.CompletedTask;
    }
}