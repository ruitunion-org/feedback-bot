using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;

namespace Bot;

public interface IMessenger
{
    public Task<long> CreateTopic(string name, CancellationToken ct);
    public Task SendToUserAsync(long userId, string text, CancellationToken ct = default);
    public Task SendToChatAsync(long topicId, string text, CancellationToken ct = default);
    public Task<long?> ForwardToUserAsync(long userId, int feedbackChatMessageId, CancellationToken ct = default);
    public Task ForwardToChatAsync(long topicId, long fromChatId, int messageId, CancellationToken ct = default);
    public Task DeleteMessageAsync(long userId, long messageId, CancellationToken ct = default);
    public Task OpenTopic(long topicId, CancellationToken ct = default);
    public Task CloseTopic(long topicId, CancellationToken ct = default);
    public Task<long[]> GetChatAdmins(CancellationToken ct = default);
}

public class Messenger(
    IOptions<AppOptions> _options,
    TelegramBotClient _client) : IMessenger
{
    public async Task OpenTopic(long topicId, CancellationToken ct = default)
    {
        try
        {
            await _client.ReopenForumTopicAsync(_options.Value.FeedbackChatId, (int)topicId, ct);
        }
        catch (ApiRequestException e) when (e.Message == "Bad Request: TOPIC_NOT_MODIFIED")
        {
            // Топик уже открыт. Просто игнорируем.
        }
    }

    public async Task CloseTopic(long topicId, CancellationToken ct = default)
    {
        try
        {
            await _client.CloseForumTopicAsync(_options.Value.FeedbackChatId, (int)topicId, ct);
        }
        catch (ApiRequestException e) when (e.Message == "Bad Request: TOPIC_NOT_MODIFIED")
        {
            // Топик уже закрыт. Просто игнорируем.
        }
    }

    public async Task<long> CreateTopic(string name, CancellationToken ct = default)
    {
        var topic = await _client.CreateForumTopicAsync(_options.Value.FeedbackChatId, name, cancellationToken: ct);
        return topic.MessageThreadId;
    }

    public async Task DeleteMessageAsync(long userId, long messageId, CancellationToken ct)
    {
        await _client.DeleteMessageAsync(userId, (int)messageId, ct);
    }

    public async Task<long[]> GetChatAdmins(CancellationToken ct = default)
    {
        var admins = await _client.GetChatAdministratorsAsync(_options.Value.FeedbackChatId, ct);
        return admins.Select(x => x.User.Id).ToArray();
    }

    public async Task ForwardToChatAsync(long topicId, long fromChatId, int messageId, CancellationToken ct = default)
    {
        await _client.ForwardMessageAsync(_options.Value.FeedbackChatId, fromChatId, messageId, (int)topicId, cancellationToken: ct);
    }

    public async Task<long?> ForwardToUserAsync(long userId, int feedbackChatMessageId, CancellationToken ct = default)
    {
        try
        {
            var message = await _client.CopyMessageAsync(userId, _options.Value.FeedbackChatId, feedbackChatMessageId, cancellationToken: ct);
            return message.Id;
        }
        catch (ApiRequestException ex) when (ex.ErrorCode == 403)
        {
            // Forbidden: bot was blocked by the user
            return null;
        }
    }

    public async Task SendToChatAsync(long topicId, string text, CancellationToken ct = default)
    {
        await _client.SendTextMessageAsync(_options.Value.FeedbackChatId, text, (int)topicId, cancellationToken: ct);
    }

    public async Task SendToUserAsync(long userId, string text, CancellationToken ct = default)
    {
        try
        {
            var message = await _client.SendTextMessageAsync(userId, text, cancellationToken: ct);
        }
        catch (ApiRequestException ex) when (ex.ErrorCode == 403)
        {
            // Forbidden: bot was blocked by the user
        }
    }
}
