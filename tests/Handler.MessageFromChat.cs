using Microsoft.Extensions.Logging;
using Moq;
using Telegram.Bot.Types;

namespace Bot.Tests;

public partial class HandlerTests
{
    [TestMethod]
    public async Task Reply_from_admin_sent_to_the_user()
    {
        // Arrange
        var userId = 12345;
        var adminId = 11111;
        var topicId = 22222;
        var chatMessageId = 33333;
        var botMessageId = 444444;
        var existingUser = new User() { Id = adminId, TopicId = topicId };
        var existingTopic = new Topic() { Id = topicId, IsOpen = true, UserId = userId };
        var newReply = new Reply { Id = chatMessageId, BotMessageId = botMessageId, TopicId = topicId };
        var update = new Update()
        {
            Message = new Message()
            {
                MessageId = chatMessageId,
                MessageThreadId = topicId,
                Text = "This is a reply",
                Chat = new() { Id = OptionsMock.Value.FeedbackChatId },
                From = new() { Id = adminId, FirstName = "Administrator" },
                ReplyToMessage = new Message()
                {
                    From = new() { Id = OptionsMock.Value.FeedbackBotId },
                    ForwardDate = DateTime.Now,
                }
            }
        };

        var repository = new Mock<IRepository>(MockBehavior.Strict);
        repository.Setup(x => x.TryRead(topicId, out existingTopic)).Returns(true);
        repository.Setup(x => x.Create(It.Is<Reply>(r => r == newReply)));

        var messenger = new Mock<IMessenger>(MockBehavior.Strict);
        messenger.Setup(x => x.ForwardToUserAsync(userId, chatMessageId, CancellationToken.None)).ReturnsAsync(botMessageId);

        var sut = new Handler(Mock.Of<ILogger<Handler>>(), OptionsMock, repository.Object, messenger.Object);

        // Act
        await sut.HandleUpdate(update, CancellationToken.None);

        // Assert
        repository.Verify(x => x.TryRead(topicId, out existingTopic), Times.Once);
        repository.Verify(x => x.Create(It.Is<Reply>(r => r == newReply)), Times.Once);
        messenger.Verify(x => x.ForwardToUserAsync(userId, chatMessageId, CancellationToken.None), Times.Once);
    }
}