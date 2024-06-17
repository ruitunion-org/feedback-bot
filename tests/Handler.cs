using Microsoft.Extensions.Options;

namespace Bot.Tests;

[TestClass]
public partial class HandlerTests
{
    public static IOptions<AppOptions> OptionsMock => Options.Create(new AppOptions()
    {
        DbConnectionString = "",
        FeedbackBotId = 123456789,
        FeedbackBotToken = "token",
        FeedbackChatId = 987654321,
        Help = "Help",
        Start = "Start"
    });
}