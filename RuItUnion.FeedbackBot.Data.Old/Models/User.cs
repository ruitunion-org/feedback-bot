namespace RuItUnion.FeedbackBot.Data.Old.Models;

public record User
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