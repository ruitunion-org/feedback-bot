using System.Globalization;

namespace RuItUnion.FeedbackBot.Options;

public record AppOptions
{
    public const string NAME = "AppOptions";

    public required long FeedbackChatId
    {
        get;
        init => field = value < 0
            ? value
            : long.Parse(@"-100" + value.ToString(@"D", CultureInfo.InvariantCulture), NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture);
    }

    public required string Start { get; init; }
    public string? DbConnectionString { get; init; }
    public string? FeedbackBotToken { get; init; }
}