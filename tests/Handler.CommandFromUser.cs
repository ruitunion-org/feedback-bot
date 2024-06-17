using Microsoft.Extensions.Logging;
using Moq;
using Telegram.Bot.Types;

namespace Bot.Tests;

public partial class HandlerTests
{
    [DataTestMethod]
    [DataRow("/start", "Start")]
    [DataRow("/help", "Help")]
    public async Task Command_from_user_causes_hardcoded_response(string command, string hardcodedResponse)
    {
        // Arrange
        var userId = 11111;
        var messageId = 33333;
        var update = new Update()
        {
            Message = new Message()
            {
                MessageId = messageId,
                Text = command,
                Chat = new() { Id = userId, },
                From = new() { Id = userId, FirstName = "John Doe" }
            }
        };

        var repository = new Mock<IRepository>(MockBehavior.Strict);

        var messenger = new Mock<IMessenger>(MockBehavior.Strict);
        messenger.Setup(x => x.SendToUserAsync(userId, hardcodedResponse, CancellationToken.None)).Returns(Task.CompletedTask);

        var sut = new Handler(Mock.Of<ILogger<Handler>>(), OptionsMock, repository.Object, messenger.Object);

        // Act
        await sut.HandleUpdate(update, CancellationToken.None);

        // Assert
        messenger.Verify(x => x.SendToUserAsync(userId, hardcodedResponse, CancellationToken.None), Times.Once);
    }
}