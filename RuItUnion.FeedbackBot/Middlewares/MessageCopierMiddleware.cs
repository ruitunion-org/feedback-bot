using System.Collections.Frozen;
using Microsoft.FeatureManagement;
using RuItUnion.FeedbackBot.Data.Models;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types.Enums;

namespace RuItUnion.FeedbackBot.Middlewares;

public class MessageCopierMiddleware(
    IOptions<AppOptions> options,
    ITelegramBotClient botClient,
    IFeedbackBotContext db,
    FeedbackMetricsService feedbackMetricsService,
    IVariantFeatureManager featureManager,
    TimeProvider clock,
    ILogger<MessageCopierMiddleware> logger) : FrameMiddleware
{
    private static readonly FrozenSet<ReactionTypeEmoji> _highVoltageEmoji =
        new[] { new ReactionTypeEmoji { Emoji = @"⚡" } }.ToFrozenSet();

    private static readonly FrozenSet<ReactionTypeEmoji> _noHearEmoji =
        new[] { new ReactionTypeEmoji { Emoji = @"🙉" } }.ToFrozenSet();

    private readonly long _chatId = options.Value.FeedbackChatId;

    public override async Task InvokeAsync(Update update, FrameContext context, CancellationToken ct = new())
    {
        if (string.IsNullOrEmpty(context.GetCommandName())
            && update.Message?.MessageThreadId is not null
            && update.Message.Chat.Id == _chatId
            && update.Message.ReplyToMessage?.Type is < MessageType.ForumTopicCreated
                or > MessageType.GeneralForumTopicUnhidden
            && update.Message.ReplyToMessage.From?.Username == context.GetBotUsername())
        {
            DbTopic? topic = await db.Topics.AsTracking()
                .FirstOrDefaultAsync(x => x.ThreadId == update.Message.MessageThreadId, ct).ConfigureAwait(false);
            if (topic?.ThreadId is null
                || update.Message.ReplyToMessage.ForwardOrigin?.Type
                    is not MessageOriginType.HiddenUser
                    and not MessageOriginType.User)
                return;

            bool isInterceptBreak = false;
            if (await featureManager.IsEnabledAsync(@"Queue", ct).ConfigureAwait(false) && topic.AssigneeId is not null)
            {
                if (topic.AssigneeId != update.Message.From?.Id && !await db.Mentors.AnyAsync(x =>
                            x.MentorId == update.Message.From!.Id && x.NoviceId == topic.AssigneeId, ct)
                        .ConfigureAwait(false))
                {
                    await botClient.SetMessageReaction(update.Message.Chat.Id, update.Message.MessageId, _noHearEmoji,
                        cancellationToken: ct).ConfigureAwait(false);
                    await botClient.SendMessage(_chatId,
                            "> Запрещено отвечать в темах с исполнителем, не являясь назначенным на нее\\. Преодолеть запрет возможно с помощью команды /intercept, голосованием сменив назначение",
                            messageThreadId: update.Message?.MessageThreadId,
                            replyParameters: new()
                            {
                                AllowSendingWithoutReply = true,
                                ChatId = _chatId,
                                MessageId = context.GetMessageId().GetValueOrDefault(-1),
                            }, disableNotification: true, parseMode: ParseMode.MarkdownV2, cancellationToken: ct)
                        .ConfigureAwait(false);

                    await Next(update, context, ct).ConfigureAwait(false);
                    return;
                }

                if (update.Message.From?.Id == context.GetUserId() && topic.InterceptMessageId is not null)
                {
                    topic.ResetIntercept(clock.GetUtcNow().DateTime);
                    isInterceptBreak = true;
                }
            }

            if (await featureManager.IsEnabledAsync(@"ForbidMentionsOnAnswer", ct).ConfigureAwait(false)
                && (update.Message.Entities?.Any(x =>
                    x.Type is MessageEntityType.Mention or MessageEntityType.TextMention) ?? false))
            {
                await botClient.SendMessage(_chatId,
                    ResourceManager.GetString(nameof(MessageCopierMiddleware_CopyWithUserNotAllowed),
                        context.GetCultureInfo())!,
                    ParseMode.MarkdownV2,
                    messageThreadId: update.Message.MessageThreadId, replyParameters: new()
                    {
                        ChatId = _chatId,
                        MessageId = update.Message.MessageId,
                        AllowSendingWithoutReply = true,
                    }, cancellationToken: ct).ConfigureAwait(false);
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
            if (isInterceptBreak)
                await botClient.SetMessageReaction(update.Message.Chat.Id, topic.InterceptMessageId!.Value,
                    _noHearEmoji,
                    cancellationToken: ct).ConfigureAwait(false);
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
        feedbackMetricsService.IncMessagesCopied(topic.ThreadId, message.From?.Id ?? 0L);
        return ValueTask.CompletedTask;
    }
}