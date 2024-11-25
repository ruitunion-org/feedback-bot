namespace RuItUnion.FeedbackBot.Data.Old.Models;

public record Topic
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