using System.Collections.Frozen;
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
    FeedbackMetricsService feedbackMetricsService)
    : CommandControllerBase
{
    protected static readonly FrozenSet<ReactionTypeEmoji> EyesEmoji =
        new[] { new ReactionTypeEmoji { Emoji = @"👀" } }.ToFrozenSet();

    protected static readonly FrozenSet<ReactionTypeEmoji> HighVoltageEmoji =
        new[] { new ReactionTypeEmoji { Emoji = @"⚡" } }.ToFrozenSet();

    protected readonly long ChatId = options.Value.FeedbackChatId;

    [Command(nameof(Open))]
    [Restricted("admin")]
    public virtual async Task Open()
    {
        int? threadId = Context.GetThreadId();
        if (threadId is not null)
        {
            await UpdateTopicStatus(threadId.Value, true).ConfigureAwait(false);
        }
    }

    [Command(nameof(Close))]
    [Restricted("admin")]
    public virtual async Task Close()
    {
        int? threadId = Context.GetThreadId();
        if (threadId is not null)
        {
            await UpdateTopicStatus(threadId.Value, false).ConfigureAwait(false);
        }
    }

    [Command(nameof(Delete))]
    public virtual async Task Delete()
    {
        int? messageId = Update.Message?.ReplyToMessage?.MessageId;
        if (messageId is null)
        {
            await botClient.SendMessage(ChatId,
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
            await botClient.DeleteMessage(ChatId, messageId.Value, CancellationToken).ConfigureAwait(false);
            db.Replies.Remove(reply);
            await db.SaveChangesAsync(CancellationToken).ConfigureAwait(false);
            feedbackMetricsService.IncMessagesDeleted(reply.ChatThreadId, Context.GetUserId() ?? 0L);
        }
        else
        {
            await botClient.SendMessage(ChatId,
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
        {
            await botClient.SetMessageReaction(chatId, messageId.Value, EyesEmoji).ConfigureAwait(false);
        }

        await Task.WhenAll(topics.Select(x => UpdateTopicStatus(x.ThreadId, x.IsOpen, x))).ConfigureAwait(false);


        if (chatId is not null && messageId is not null)
        {
            await botClient.SetMessageReaction(chatId, messageId.Value, HighVoltageEmoji).ConfigureAwait(false);
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
            }

            try
            {
                await botClient.EditForumTopic(ChatId, topic.ThreadId, topicTitleGenerator.GetTopicTitle(topic))
                    .ConfigureAwait(false);
            }
            catch (ApiRequestException e) when (e.Message == @"Bad Request: TOPIC_NOT_MODIFIED")
            {
            }
        }
    }
}