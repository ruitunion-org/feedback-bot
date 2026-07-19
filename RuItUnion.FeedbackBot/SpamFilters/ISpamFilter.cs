namespace RuItUnion.FeedbackBot.SpamFilters;

public interface ISpamFilter
{
    public string Reason => GetType().Name;
    public bool IsSpam(string? text);
}