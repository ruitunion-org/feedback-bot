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
    FeedbackBotContext db,
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
    public async Task Open()
    {
        int? threadId = Context.GetThreadId();
        if (threadId is not null)
        {
            await UpdateTopicStatus(threadId.Value, true).ConfigureAwait(false);
        }
    }

    [Command(nameof(Close))]
    [Restricted("admin")]
    public async Task Close()
    {
        int? threadId = Context.GetThreadId();
        if (threadId is not null)
        {
            await UpdateTopicStatus(threadId.Value, false).ConfigureAwait(false);
        }
    }

    [Command(nameof(Delete))]
    public async Task Delete()
    {
        int? messageId = Update.Message?.ReplyToMessage?.MessageId;
        if (messageId is null)
        {
            await botClient.SendMessage(_chatId,
                    ResourceManager.GetString(nameof(ThreadController_Delete_NotReply), Context.GetCultureInfo())!)
                .ConfigureAwait(false);
            return;
        }

        DbReply? reply = await db.Replies.AsTracking().Include(x => x.Topic)
            .FirstOrDefaultAsync(x => x.ChatMessageId == messageId, CancellationToken)
            .ConfigureAwait(false);
        if (reply is not null)
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
                    ResourceManager.GetString(nameof(ThreadController_Delete_NotFound), Context.GetCultureInfo())!)
                .ConfigureAwait(false);
        }
    }

    [Command(nameof(Sync))]
    [Restricted("admin")]
    public async Task Sync()
    {
        DbTopic[] topics = await db.Topics.Include(x => x.User).AsTracking().ToArrayAsync().ConfigureAwait(false);
        long? chatId = Context.GetChatId();
        int? messageId = Context.GetMessageId();
        if (chatId is not null && messageId is not null)
        {
            await botClient.SetMessageReaction(chatId, messageId.Value, _eyesEmoji).ConfigureAwait(false);
        }

        await Task.WhenAll(topics.Select(x => UpdateTopicStatus(x.ThreadId, x.IsOpen, x))).ConfigureAwait(false);


        if (chatId is not null && messageId is not null)
        {
            await botClient.SetMessageReaction(chatId, messageId.Value, _highVoltageEmoji).ConfigureAwait(false);
        }
    }

    private async Task UpdateTopicStatus(int threadId, bool isOpen, DbTopic? topic = null)
    {
        topic ??= await db.Topics.AsTracking().Include(x => x.User)
            .FirstOrDefaultAsync(x => x.ThreadId == threadId).ConfigureAwait(false);
        try
        {
            if (isOpen)
            {
                await botClient.ReopenForumTopic(_chatId, threadId, CancellationToken).ConfigureAwait(false);
            }
            else
            {
                await botClient.CloseForumTopic(_chatId, threadId, CancellationToken).ConfigureAwait(false);
            }
        }
        catch (ApiRequestException e) when (e.Message == @"Bad Request: TOPIC_NOT_MODIFIED")
        {
        }

        if (topic is not null)
        {
            if (topic.IsOpen != isOpen)
            {
                topic.IsOpen = isOpen;
                await db.SaveChangesAsync().ConfigureAwait(false);
            }

            try
            {
                await botClient.EditForumTopic(_chatId, topic.ThreadId, topic.ToString()).ConfigureAwait(false);
            }
            catch (ApiRequestException e) when (e.Message == @"Bad Request: TOPIC_NOT_MODIFIED")
            {
            }
        }
    }
}