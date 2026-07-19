using System.Text;
using RuItUnion.FeedbackBot.Data.Models;
using Telegram.Bot.Types.Enums;
using TgBotFrame.Commands;
using TgBotFrame.Commands.Attributes;
using TgBotFrame.Commands.Authorization.Attributes;

namespace RuItUnion.FeedbackBot.Commands;

[CommandController("List")]
public class ListController(
    FeedbackBotContext dataContext,
    ITelegramBotClient botClient,
    IOptions<AppOptions> options,
    TopicTitleGenerator topicTitleGenerator) : CommandControllerBase
{
    private readonly long _chatId = options.Value.FeedbackChatId;

    [Command(nameof(TopicList))]
    [Restricted("admin")]
    public async Task TopicList()
    {
        long? chatId = Context.GetChatId();
        if (chatId is null)
        {
            return;
        }

        StringBuilder text = new();
        foreach (DbTopic topic in dataContext.Topics
                     .AsNoTracking()
                     .Include(x => x.User)
                     .Include(x => x.Replies)
                     .OrderByDescending(x => x.Id)
                     .Where(x => x.IsOpen))
        {
            text.AppendLine();

            try
            {
                int maxMessageId = topic.Replies.Max(x => x.ChatMessageId);
                text.Append(@"<a href=""https://t.me/c/")
                    .Append((-(_chatId + 1000000000000)).ToString(@"D", Culture))
                    .Append('/')
                    .Append(maxMessageId)
                    .Append(@"/"">")
                    .Append(topicTitleGenerator.GetTopicTitle(topic))
                    .Append(@"</a>");
            }
            catch (InvalidOperationException)
            {
                text.Append(topicTitleGenerator.GetTopicTitle(topic));
            }
        }

        string finalText = text.Length > 0
            ? ResourceManager.GetString(nameof(TopicListTitle), Context.GetCultureInfo()) + text
            : ResourceManager.GetString(nameof(TopicListEmpty), Context.GetCultureInfo()) ?? string.Empty;
        await botClient.SendMessage(chatId.Value, finalText, ParseMode.Html, new()
        {
            AllowSendingWithoutReply = true,
            ChatId = chatId.Value,
            MessageId = Context.GetMessageId().GetValueOrDefault(-1),
        }, messageThreadId: Context.GetThreadId(), cancellationToken: CancellationToken).ConfigureAwait(false);
    }

    [Command(nameof(TagList))]
    [Restricted("admin")]
    public async Task TagList()
    {
        long? chatId = Context.GetChatId();
        if (chatId is null)
        {
            return;
        }

        DbTopic[] topics = await dataContext.Topics.AsNoTracking().ToArrayAsync().ConfigureAwait(false);
        (string Name, int Count)[] tagNames = topics
            .SelectMany(x => x.Tags.Keys)
            .GroupBy(x => x, _ => 1, (name, integer) => (name, integer.Count())).OrderByDescending(x => x.Item2)
            .ToArray();

        await botClient.SendMessage(chatId,
            string.Join(Environment.NewLine, tagNames.Select(x => @$"{x.Name}: {x.Count}")), ParseMode.None, new()
            {
                AllowSendingWithoutReply = true,
                ChatId = chatId.Value,
                MessageId = Context.GetMessageId().GetValueOrDefault(-1),
            }, messageThreadId: Context.GetThreadId(), cancellationToken: CancellationToken).ConfigureAwait(false);
    }
}