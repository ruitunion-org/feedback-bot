using System.Text;

namespace Bot;

public interface IInput;

public static class StringExtensions
{
    public static ReadOnlySpan<char> GetCommand(this string s)
    {
        var span = s.AsSpan().TrimStart();
        
        var atIndex = span.IndexOf('@');
        if (atIndex != -1) span = span[..atIndex];
        
        var spaceIndex = span.IndexOf(' ');
        if (spaceIndex != -1) span = span[..spaceIndex];
        
        return span;
    }
}

public record CommandFromUser(long UserId, string Content) : IInput
{
    public ReadOnlySpan<char> Command => Content.GetCommand();
}

public record MessageFromUser(long UserId, int MessageId, string FirstName, string? LastName, string? Username, string Content) : IInput
{
    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append(FirstName);
        if (!string.IsNullOrEmpty(LastName)) sb.AppendFormat(" {0}", LastName);
        if (!string.IsNullOrEmpty(Username)) sb.AppendFormat(" ({0})", Username);
        return sb.ToString();
    }
}

public record CommandFromChat(long ChatId, long UserId, int TopicId, string Content, int? ReplyToMessageId, long? ReplyToUserId) : IInput
{
    public ReadOnlySpan<char> Command => Content.GetCommand();
}

public record MessageFromChat(long ChatId, long UserId, int TopicId, int MessageId, string Content) : IInput;

public interface IEntity
{
    public long Id { get; init; }
    public int Version { get; set; }
}

public record Topic : IEntity
{
    public long Id { get; init; }
    public int Version { get; set; }
    public bool IsOpen { get; set; }
    public long UserId { get; init; }
    public User? User { get; init; }
    public ICollection<Reply> Replies { get; init; } = Array.Empty<Reply>();

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
    public required long Id { get; init; }
    public int Version { get; set; }
    public bool Banned { get; set; }
    public long TopicId { get; init; }
    public Topic? Topic { get; init; }
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
    /// <summary>
    /// Id сообщения в чате обратной связи
    /// </summary>
    public required long Id { get; init; }

    public int Version { get; set; }

    public long TopicId { get; init; }

    public Topic? Topic { get; init; }

    /// <summary>
    /// Id сообщения в боте
    /// </summary>
    public required long BotMessageId { get; init; }
}