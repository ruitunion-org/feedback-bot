namespace RuItUnion.FeedbackBot.Data.Old;

public interface IEntity
{
    public long Id { get; init; }
    public int Version { get; set; }
}

public record Topic : IEntity
{
    public bool IsOpen { get; set; }
    public long UserId { get; init; }
    public User? User { get; init; }
    public ICollection<Reply> Replies { get; init; } = [];
    public long Id { get; init; }
    public int Version { get; set; }

    public void Open()
    {
        IsOpen = true;
        Version++;
    }

    public void Close()
    {
        IsOpen = false;
        Version++;
    }
}

public record User : IEntity
{
    public bool Banned { get; set; }
    public long TopicId { get; init; }
    public Topic? Topic { get; init; }
    public required long Id { get; init; }
    public int Version { get; set; }

    public void Ban()
    {
        Banned = true;
        Version++;
    }

    public void Unban()
    {
        Banned = false;
        Version++;
    }
}

public record Reply : IEntity
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