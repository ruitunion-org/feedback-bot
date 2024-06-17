using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;

namespace Bot;

public interface IMessenger
{
    public Task<long> CreateTopic(string name, CancellationToken ct);
    public Task SendToUserAsync(long userId, string Text, CancellationToken ct);
    public Task SendToChatAsync(long topicId, string Text, CancellationToken ct);
    public Task<long?> ForwardToUserAsync(long userId, int feedbackChatMessageId, CancellationToken ct);
    public Task ForwardToChatAsync(long topicId, long fromChatId, int messageId, CancellationToken ct);
    public Task DeleteMessageAsync(long userId, long messageId, CancellationToken ct);
    public Task OpenTopic(long topicId, CancellationToken ct);
    public Task CloseTopic(long topicId, CancellationToken ct);
    public Task<long[]> GetChatAdmins(CancellationToken ct);
}

public class Messenger(
    IOptions<AppOptions> _options,
    TelegramBotClient _client) : IMessenger
{
    public async Task OpenTopic(long topicId, CancellationToken ct)
    {
        try
        {
            var request = new ReopenForumTopicRequest
            {
                ChatId = _options.Value.FeedbackChatId,
                MessageThreadId = (int)topicId
            };

            await _client.ReopenForumTopicAsync(request, ct);
        }
        catch (ApiRequestException e) when (e.Message == "Bad Request: TOPIC_NOT_MODIFIED")
        {
            // Топик уже открыт. Просто игнорируем.
        }
    }

    public async Task CloseTopic(long topicId, CancellationToken ct)
    {
        try
        {
            var request = new CloseForumTopicRequest()
            {
                ChatId = _options.Value.FeedbackChatId,
                MessageThreadId = (int)topicId
            };

            await _client.CloseForumTopicAsync(request, ct);
        }
        catch (ApiRequestException e) when (e.Message == "Bad Request: TOPIC_NOT_MODIFIED")
        {
            // Топик уже закрыт. Просто игнорируем.
        }
    }

    public async Task<long> CreateTopic(string name, CancellationToken ct)
    {
        var request = new CreateForumTopicRequest()
        {
            ChatId = _options.Value.FeedbackChatId,
            Name = name,
        };

        var topic = await _client.CreateForumTopicAsync(request, ct);

        return topic.MessageThreadId;
    }

    public async Task DeleteMessageAsync(long userId, long messageId, CancellationToken ct)
    {
        var request = new DeleteMessageRequest()
        {
            ChatId = userId,
            MessageId = (int)messageId
        };

        await _client.DeleteMessageAsync(request, ct);
    }

    public async Task<long[]> GetChatAdmins(CancellationToken ct)
    {
        var request = new GetChatAdministratorsRequest()
        {
            ChatId = _options.Value.FeedbackChatId
        };

        var admins = await _client.GetChatAdministratorsAsync(request, ct);

        return admins.Select(x => x.User.Id).ToArray();
    }

    public async Task ForwardToChatAsync(long topicId, long fromChatId, int messageId, CancellationToken ct)
    {
        var request = new ForwardMessageRequest()
        {
            FromChatId = fromChatId,
            MessageId = messageId,
            ChatId = _options.Value.FeedbackChatId,
            MessageThreadId = (int)topicId
        };

        await _client.ForwardMessageAsync(request, ct);
    }

    public async Task<long?> ForwardToUserAsync(long userId, int feedbackChatMessageId, CancellationToken ct)
    {
        var request = new CopyMessageRequest()
        {
            ChatId = userId,
            FromChatId = _options.Value.FeedbackChatId,
            MessageId = feedbackChatMessageId
        };

        try
        {
            var message = await _client.CopyMessageAsync(request, ct);
            return message.Id;
        }
        catch (ApiRequestException ex) when (ex.ErrorCode == 403)
        {
            // Forbidden: bot was blocked by the user
            return null;
        }
    }

    public async Task SendToChatAsync(long topicId, string text, CancellationToken ct)
    {
        var request = new SendMessageRequest()
        {
            ChatId = _options.Value.FeedbackChatId,
            MessageThreadId = (int)topicId,
            Text = text
        };

        await _client.SendMessageAsync(request, ct);
    }

    public async Task SendToUserAsync(long userId, string text, CancellationToken ct)
    {
        var request = new SendMessageRequest()
        {
            ChatId = userId,
            Text = text
        };

        try
        {
            var message = await _client.SendMessageAsync(request, ct);
        }
        catch (ApiRequestException ex) when (ex.ErrorCode == 403)
        {
            // Forbidden: bot was blocked by the user
        }
    }
}
