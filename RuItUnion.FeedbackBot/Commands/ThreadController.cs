using System.Collections.Frozen;
using Microsoft.FeatureManagement;
using Microsoft.FeatureManagement.Mvc;
using RuItUnion.FeedbackBot.Data.Models;
using Telegram.Bot.Exceptions;
using TgBotFrame.Commands;
using TgBotFrame.Commands.Attributes;
using TgBotFrame.Commands.Authorization.Attributes;

namespace RuItUnion.FeedbackBot.Commands;

[CommandController("Thread")]
public class ThreadController(
    IOptions<AppOptions> options,
    ITelegramBotClient botClient,
    IFeedbackBotContext db,
    TopicTitleGenerator topicTitleGenerator,
    IVariantFeatureManager featureManager,
    FeedbackMetricsService feedbackMetricsService)
    : CommandControllerBase
{
    private static readonly FrozenSet<ReactionTypeEmoji> _eyesEmoji =
        new[] { new ReactionTypeEmoji { Emoji = @"👀" } }.ToFrozenSet();

    private static readonly FrozenSet<ReactionTypeEmoji> _highVoltageEmoji =
        new[] { new ReactionTypeEmoji { Emoji = @"⚡" } }.ToFrozenSet();

    private readonly long _chatId = options.Value.FeedbackChatId;

    [Command(nameof(Open))]
    [Restricted("admin")]
    public virtual async Task Open()
    {
        int? threadId = Context.GetThreadId();
        if (threadId is not null) await UpdateTopicStatus(threadId.Value, true).ConfigureAwait(false);
    }

    [Command(nameof(Close))]
    [Restricted("admin")]
    public async Task Close() =>
        await Close(!await featureManager.IsEnabledAsync(@"DoNotAllowEmptyHeader").ConfigureAwait(false))
            .ConfigureAwait(false);

    [Command(nameof(Close))]
    [Restricted("admin")]
    [FeatureGate("DoNotAllowEmptyHeader")]
    public async Task Close(bool force)
    {
        int? threadId = Context.GetThreadId();
        DbTopic? topic = await db.Topics.AsTracking().Include(x => x.User)
            .FirstOrDefaultAsync(x => x.ThreadId == threadId).ConfigureAwait(false);
        if (topic is null) return;

        if (!force && string.IsNullOrWhiteSpace(topic.Header))
            await botClient.SendMessage(_chatId,
                ResourceManager.GetString(nameof(InternalThreadController_Close_NoHeader), Context.GetCultureInfo())!,
                messageThreadId: Update.Message?.MessageThreadId, replyParameters: new()
                {
                    AllowSendingWithoutReply = true,
                    ChatId = Context.GetChatId(),
                    MessageId = Context.GetMessageId() ?? 0,
                }).ConfigureAwait(false);
        else
            await UpdateTopicStatus(threadId!.Value, false, topic).ConfigureAwait(false);
    }


    [Command(nameof(Delete))]
    public virtual async Task Delete()
    {
        int? messageId = Update.Message?.ReplyToMessage?.MessageId;
        if (messageId is null)
        {
            await botClient.SendMessage(_chatId,
                    ResourceManager.GetString(nameof(ThreadController_Delete_NotReply), Context.GetCultureInfo())!,
                    messageThreadId: Update.Message?.MessageThreadId)
                .ConfigureAwait(false);
            return;
        }

        DbReply? reply = await db.Replies.AsTracking().Include(x => x.Topic)
            .FirstOrDefaultAsync(x => x.ChatMessageId == messageId, CancellationToken)
            .ConfigureAwait(false);
        if (reply is not null && reply.UserMessageId >= 0)
        {
            await botClient.DeleteMessage(reply.Topic.UserChatId, reply.UserMessageId, CancellationToken)
                .ConfigureAwait(false);
            await botClient.DeleteMessage(_chatId, messageId.Value, CancellationToken).ConfigureAwait(false);
            db.Replies.Remove(reply);
            await db.SaveChangesAsync(CancellationToken).ConfigureAwait(false);
            feedbackMetricsService.IncMessagesDeleted(reply.ChatThreadId, Context.GetUserId() ?? 0L);
        }
        else
        {
            await botClient.SendMessage(_chatId,
                    ResourceManager.GetString(nameof(ThreadController_Delete_NotFound), Context.GetCultureInfo())!,
                    messageThreadId: Update.Message?.MessageThreadId)
                .ConfigureAwait(false);
        }
    }

    [Command(nameof(Sync))]
    [Restricted("service_admin")]
    public virtual async Task Sync()
    {
        DbTopic[] topics = await db.Topics.Include(x => x.User).AsTracking().ToArrayAsync().ConfigureAwait(false);
        long? chatId = Context.GetChatId();
        int? messageId = Context.GetMessageId();
        if (chatId is not null && messageId is not null)
            await botClient.SetMessageReaction(chatId, messageId.Value, _eyesEmoji).ConfigureAwait(false);

        await Task.WhenAll(topics.Select(x => UpdateTopicStatus(x.ThreadId, x.IsOpen, x))).ConfigureAwait(false);


        if (chatId is not null && messageId is not null)
            await botClient.SetMessageReaction(chatId, messageId.Value, _highVoltageEmoji).ConfigureAwait(false);
    }

    [Command(nameof(Close))]
    [Restricted("admin")]
    public async Task Close(string header)
    {
        int? threadId = Context.GetThreadId();
        if (threadId is not null)
        {
            DbTopic? topic = await db.Topics.AsTracking().Include(x => x.User)
                .FirstOrDefaultAsync(x => x.ThreadId == threadId).ConfigureAwait(false);
            topic!.Header = header;
            await UpdateTopicStatus(threadId.Value, false, topic).ConfigureAwait(false);
        }
    }

    [Command(nameof(Close))]
    [Restricted("admin")]
    public async Task Close(int topicId)
    {
        DbTopic? topic = await db.Topics.AsTracking().Include(x => x.User)
            .FirstOrDefaultAsync(x => x.Id == topicId).ConfigureAwait(false);
        if (topic is null)
        {
            await botClient.SendMessage(_chatId,
                ResourceManager.GetString(nameof(InternalThreadController_Close_TopicNotFound),
                    Context.GetCultureInfo())!,
                messageThreadId: Update.Message?.MessageThreadId, replyParameters: new()
                {
                    AllowSendingWithoutReply = true,
                    ChatId = Context.GetChatId(),
                    MessageId = Context.GetMessageId() ?? 0,
                }).ConfigureAwait(false);
            return;
        }

        await UpdateTopicStatus(topic.ThreadId, false, topic).ConfigureAwait(false);
    }

    [Command(nameof(Link))]
    [Restricted("admin")]
    public async Task Link(int topicId)
    {
        int messageId = await db.Topics
            .AsNoTrackingWithIdentityResolution()
            .Include(x => x.Replies)
            .Where(x => x.Id == topicId)
            .Select(x => x.Replies.Max(y => y.ChatMessageId))
            .FirstOrDefaultAsync().ConfigureAwait(false);
        if (messageId == 0)
        {
            await botClient.SendMessage(_chatId,
                ResourceManager.GetString(nameof(InternalThreadController_Close_TopicNotFound),
                    Context.GetCultureInfo())!,
                messageThreadId: Update.Message?.MessageThreadId, replyParameters: new()
                {
                    AllowSendingWithoutReply = true,
                    ChatId = Context.GetChatId(),
                    MessageId = Context.GetMessageId() ?? 0,
                }).ConfigureAwait(false);
            return;
        }

        await botClient.SendMessage(_chatId,
            $@"https://t.me/c/{-(_chatId + 1000000000000):D}/{messageId:D}",
            messageThreadId: Update.Message?.MessageThreadId, replyParameters: new()
            {
                AllowSendingWithoutReply = true,
                ChatId = Context.GetChatId(),
                MessageId = Context.GetMessageId() ?? 0,
            }).ConfigureAwait(false);
    }

    [Command(nameof(ForceSend))]
    [Restricted("admin")]
    public async Task ForceSend(string message)
    {
        int? threadId = Context.GetThreadId();
        DbTopic? topic = threadId is null
            ? null
            : await db.Topics.AsNoTracking()
                .FirstOrDefaultAsync(x => x.ThreadId == threadId).ConfigureAwait(false);

        if (topic is not null)
        {
            await botClient.SendMessage(topic.UserChatId, message).ConfigureAwait(false);

            long? chatId = Context.GetChatId()!;
            long? messageId = Context.GetMessageId()!;
            if (chatId is not null && messageId is not null)
                await botClient.SetMessageReaction(chatId.Value, (int)messageId.Value, _highVoltageEmoji)
                    .ConfigureAwait(false);
        }
        else
        {
            await botClient.SendMessage(_chatId,
                ResourceManager.GetString(nameof(InternalThreadController_Close_TopicNotFound),
                    Context.GetCultureInfo())!,
                messageThreadId: Update.Message?.MessageThreadId, replyParameters: new()
                {
                    AllowSendingWithoutReply = true,
                    ChatId = Context.GetChatId(),
                    MessageId = Context.GetMessageId() ?? 0,
                }).ConfigureAwait(false);
        }
    }

    protected virtual async Task UpdateTopicStatus(int threadId, bool isOpen, DbTopic? topic = null)
    {
        topic ??= await db.Topics.AsTracking().Include(x => x.User)
            .FirstOrDefaultAsync(x => x.ThreadId == threadId).ConfigureAwait(false);

        if (topic is not null)
        {
            if (topic.IsOpen != isOpen)
            {
                topic.IsOpen = isOpen;
                await db.SaveChangesAsync().ConfigureAwait(false);
                if (topic.IsOpen)
                    feedbackMetricsService.IncOpenedTopics(topic.ThreadId);
                else
                    feedbackMetricsService.DecOpenedTopics(topic.ThreadId);
            }

            try
            {
                await botClient.EditForumTopic(_chatId, topic.ThreadId, topicTitleGenerator.GetTopicTitle(topic))
                    .ConfigureAwait(false);
            }
            catch (ApiRequestException e) when (e.Message == @"Bad Request: TOPIC_NOT_MODIFIED")
            {
            }
        }
    }
}