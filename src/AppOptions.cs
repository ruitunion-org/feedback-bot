using System.Diagnostics.CodeAnalysis;

namespace Bot;

[ExcludeFromCodeCoverage]
public class AppOptions
{
    public const string Name = "AppOptions";
    public required string DbConnectionString { get; init; }
    public required string FeedbackBotToken { get; init; }
    public required long FeedbackBotId { get; init; }
    public required long FeedbackChatId { get; init; }
    public required string Start { get; init; }
    public required string Help { get; init; }
}
