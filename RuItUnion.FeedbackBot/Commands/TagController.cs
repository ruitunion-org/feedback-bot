using System.Collections.Frozen;
using System.Text;
using RuItUnion.FeedbackBot.Data.Models;
using TgBotFrame.Commands;
using TgBotFrame.Commands.Attributes;
using TgBotFrame.Commands.Authorization.Attributes;

namespace RuItUnion.FeedbackBot.Commands;

[CommandController("ThreadInternal")]
public class TagController(
    ITelegramBotClient botClient,
    FeedbackBotContext dataContext,
    IOptions<AppOptions> options,
    TopicTitleGenerator topicTitleGenerator,
    FeedbackMetricsService metricsService) : CommandControllerBase
{
    private static readonly FrozenSet<ReactionTypeEmoji> _eyeEmoji =
        new[] { new ReactionTypeEmoji { Emoji = @"👀" } }.ToFrozenSet();

    private readonly long _chatId = options.Value.FeedbackChatId;

    [Command(nameof(Tag))]
    public async Task Tag()
    {
        int? messageId = Context.GetMessageId();
        int? threadId = Context.GetThreadId();
        long? chatId = Context.GetChatId();

        if (threadId is null)
        {
            await Send(ResourceManager.GetString(nameof(TagController_Tag_OnlyTopics), Context.GetCultureInfo())!)
                .ConfigureAwait(false);
            return;
        }

        DbTopic? topic = dataContext.Topics
            .AsNoTracking()
            .FirstOrDefault(x => x.ThreadId == threadId);
        if (topic is null)
        {
            await Send(ResourceManager.GetString(nameof(TagController_Tag_NotUserTopic), Context.GetCultureInfo())!)
                .ConfigureAwait(false);
            return;
        }

        StringBuilder sb = new(256);
        foreach (KeyValuePair<string, object?> tag in topic.Tags)
        {
            sb.Append(tag.Key);
            sb.Append(@": ");
            sb.Append(tag.Value?.ToString() ?? @"—");
            sb.AppendLine();
        }

        await Send(sb.Length == 0
            ? ResourceManager.GetString(nameof(TagController_Tag_NoFound), Context.GetCultureInfo())!
            : sb.ToString()).ConfigureAwait(false);
        return;

        Task<Message> Send(string text)
        {
            return botClient.SendMessage(chatId!, text,
                messageThreadId: threadId,
                replyParameters: messageId is not null
                    ? new ReplyParameters
                    {
                        ChatId = chatId,
                        MessageId = messageId.Value,
                        AllowSendingWithoutReply = true,
                    }
                    : null, cancellationToken: CancellationToken);
        }
    }

    [Command(nameof(TagRemove))]
    [Restricted("admin")]
    public async Task TagRemove(string key)
    {
        int? threadId = Context.GetThreadId();
        DbTopic? topic = threadId is null
            ? null
            : await dataContext.Topics
                .AsTracking()
                .Include(x => x.User)
                .FirstOrDefaultAsync(x => x.ThreadId == threadId).ConfigureAwait(false);

        await TagRemove(topic, key).ConfigureAwait(false);
    }

    [Command(nameof(TagRemove))]
    [Restricted("admin")]
    public async Task TagRemove(int topicId, string key)
    {
        DbTopic? topic = await dataContext.Topics
            .AsTracking()
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.Id == topicId).ConfigureAwait(false);

        await TagRemove(topic, key).ConfigureAwait(false);
    }

    [Command(nameof(Tag))]
    [Restricted("admin")]
    public async Task Tag(string key, string value)
    {
        int? threadId = Context.GetThreadId();
        DbTopic? topic = threadId is null
            ? null
            : await dataContext.Topics
                .AsTracking()
                .Include(x => x.User)
                .FirstOrDefaultAsync(x => x.ThreadId == threadId).ConfigureAwait(false);

        await Tag(topic, key, value).ConfigureAwait(false);
    }

    [Command(nameof(Tag))]
    [Restricted("admin")]
    public async Task Tag(int topicId, string key, string value)
    {
        DbTopic? topic = await dataContext.Topics
            .AsTracking()
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.Id == topicId).ConfigureAwait(false);

        await Tag(topic, key, value).ConfigureAwait(false);
    }

    private async Task TagRemove(DbTopic? topic, string key)
    {
        int? messageId = Context.GetMessageId();
        int? threadId = Context.GetThreadId();
        long? chatId = Context.GetChatId();

        if (topic is null)
        {
            if (chatId is null || messageId is null)
            {
                return;
            }

            await botClient.SendMessage(chatId,
                ResourceManager.GetString(nameof(TagController_Tag_NotUserTopic), Context.GetCultureInfo())!,
                messageThreadId: threadId,
                replyParameters: new()
                {
                    ChatId = chatId,
                    MessageId = messageId.Value,
                    AllowSendingWithoutReply = true,
                }).ConfigureAwait(false);
            return;
        }

        topic.Tags = topic.Tags.Remove(key);
        await dataContext.SaveChangesAsync().ConfigureAwait(false);

        await botClient
            .SetMessageReaction(chatId!, messageId!.Value, _eyeEmoji,
                cancellationToken: CancellationToken)
            .ConfigureAwait(false);
    }

    private async Task Tag(DbTopic? topic, string key, string value)
    {
        int? messageId = Context.GetMessageId();
        int? threadId = Context.GetThreadId();
        long? chatId = Context.GetChatId();

        if (topic is null)
        {
            if (chatId is null || messageId is null)
            {
                return;
            }

            await botClient
                .SetMessageReaction(chatId, messageId.Value, _eyeEmoji,
                    cancellationToken: CancellationToken)
                .ConfigureAwait(false);
            await botClient.SendMessage(chatId,
                ResourceManager.GetString(nameof(TagController_Tag_NotUserTopic), Context.GetCultureInfo())!,
                messageThreadId: threadId,
                replyParameters: new()
                {
                    ChatId = chatId,
                    MessageId = messageId.Value,
                    AllowSendingWithoutReply = true,
                }).ConfigureAwait(false);

            return;
        }

        key = key.ToLowerInvariant();

        switch (key)
        {
            case @"type":
                topic.Tags = topic.Tags.SetItem(@"type", value);
                return;
            case @"result" when bool.TryParse(value, out bool boolResult):
                topic.Tags = topic.Tags.SetItem(@"result", boolResult);
                break;
            case @"result" when int.TryParse(value, out int intResult):
                topic.Tags = topic.Tags.SetItem(@"result", intResult != 0);
                break;
            case @"result" when string.Equals(value, @"success", StringComparison.OrdinalIgnoreCase):
                topic.Tags = topic.Tags.SetItem(@"result", true);
                break;
            case @"result" when string.Equals(value, @"fail", StringComparison.OrdinalIgnoreCase):
                topic.Tags = topic.Tags.SetItem(@"result", false);
                break;
            case @"result":
                topic.Tags = topic.Tags.SetItem(@"type", value);
                return;
            case @"header":
                topic.Header = value;
                await botClient.EditForumTopic(_chatId, topic.ThreadId, topicTitleGenerator.GetTopicTitle(topic),
                    cancellationToken: CancellationToken).ConfigureAwait(false);
                break;
            case "":
                if (chatId is not null && messageId is not null)
                {
                    await botClient.SendMessage(chatId,
                        ResourceManager.GetString(nameof(TagController_Tag_EmptyTagName), Context.GetCultureInfo())!,
                        messageThreadId: threadId,
                        replyParameters: new()
                        {
                            ChatId = chatId,
                            MessageId = messageId.Value,
                            AllowSendingWithoutReply = true,
                        }).ConfigureAwait(false);
                }
                return;
            default:
                topic.Tags = topic.Tags.SetItem(key, value);
                break;
        }

        await dataContext.SaveChangesAsync(CancellationToken).ConfigureAwait(false);
        metricsService.IncTagsSet(Context.GetUserId() ?? 0, Context.GetThreadId() ?? 0, key, value);

        if (chatId is not null && messageId is not null)
        {
            await botClient
                .SetMessageReaction(chatId, messageId.Value, _eyeEmoji, cancellationToken: CancellationToken)
                .ConfigureAwait(false);
        }
    }
}