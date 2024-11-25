namespace RuItUnion.FeedbackBot.Data.Old.Models;

public record Reply
{
    public long TopicId { get; init; }

    public Topic? Topic { get; init; }

    /// <summary>
    ///     Id сообщения в боте
    /// </summary>
    public required long BotMessageId { get; init; }

    /// <summary>
    ///     Id сообщения в чате обратной связи
    /// </summary>
    public required long Id { get; init; }

    public int Version { get; set; }
}