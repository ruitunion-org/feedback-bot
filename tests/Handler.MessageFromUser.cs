using Microsoft.Extensions.Logging;
using Moq;
using Telegram.Bot.Types;

namespace Bot.Tests;

public partial class HandlerTests
{
    [TestMethod]
    public async Task Message_from_new_user_causes_topic_creation_and_then_forwarded_to_the_chat()
    {
        // Arrange
        var userId = 11111;
        var topicId = 22222;
        var messageId = 33333;
        User? existingUser = null;
        var newTopic = new Topic() { Id = topicId, IsOpen = true, UserId = userId };
        var newUser = new User() { Id = userId, TopicId = topicId, Topic = newTopic };
        var update = new Update()
        {
            Message = new Message()
            {
                MessageId = messageId,
                Text = "Hello, World!",
                Chat = new() { Id = userId, },
                From = new() { Id = userId, FirstName = "John Doe" }
            }
        };

        var repository = new Mock<IRepository>(MockBehavior.Strict);
        repository.Setup(x => x.TryRead(userId, out existingUser)).Returns(false);
        repository.Setup(x => x.Create(It.Is<User>(u => u == newUser)));
        repository.Setup(x => x.TryRead(topicId, out newTopic)).Returns(true);

        var messenger = new Mock<IMessenger>(MockBehavior.Strict);
        messenger.Setup(x => x.CreateTopic(It.IsAny<string>(), CancellationToken.None)).ReturnsAsync(topicId);
        messenger.Setup(x => x.ForwardToChatAsync(topicId, userId, messageId, CancellationToken.None)).Returns(Task.CompletedTask);

        var sut = new Handler(Mock.Of<ILogger<Handler>>(), OptionsMock, repository.Object, messenger.Object);

        // Act
        await sut.HandleUpdate(update, CancellationToken.None);

        // Assert
        repository.Verify(x => x.TryRead(userId, out existingUser), Times.Once);
        repository.Verify(x => x.TryRead(topicId, out newTopic), Times.Once);
        repository.Verify(x => x.Create(It.Is<User>(u => u == newUser)), Times.Once);
        messenger.Verify(x => x.ForwardToChatAsync(topicId, userId, messageId, CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task Message_from_banned_user_not_forwaded_to_the_chat()
    {
        // Arrange
        var userId = 11111;
        var topicId = 22222;
        var messageId = 33333;
        var text = "Вы больше не можете отправлять сообщения в бота.";
        var existingUser = new User()
        {
            Id = userId,
            TopicId = topicId,
            Banned = true // user banned
        };
        var update = new Update()
        {
            Message = new Message()
            {
                MessageId = messageId,
                Text = "Hello, World!",
                Chat = new() { Id = userId, },
                From = new() { Id = userId, FirstName = "John Doe" }
            }
        };

        var repository = new Mock<IRepository>(MockBehavior.Strict);
        repository.Setup(x => x.TryRead(userId, out existingUser)).Returns(true);

        var messenger = new Mock<IMessenger>(MockBehavior.Strict);
        messenger.Setup(x => x.CreateTopic(It.IsAny<string>(), CancellationToken.None)).ReturnsAsync(topicId);
        messenger.Setup(x => x.SendToUserAsync(userId, text, CancellationToken.None)).Returns(Task.CompletedTask);

        var sut = new Handler(Mock.Of<ILogger<Handler>>(), OptionsMock, repository.Object, messenger.Object);

        // Act
        await sut.HandleUpdate(update, CancellationToken.None);

        // Assert
        repository.Verify(x => x.TryRead(userId, out existingUser), Times.Once);
        messenger.Verify(x => x.SendToUserAsync(userId, text, CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task Message_from_existing_user_reopens_closed_topic()
    {
        // Arrange
        var userId = 11111;
        var topicId = 22222;
        var messageId = 33333;
        var existingUser = new User() { Id = userId, TopicId = topicId };
        var existingTopic = new Topic()
        {
            Id = topicId,
            IsOpen = false, // topic closed
            UserId = userId
        };
        var changedTopic = existingTopic with { IsOpen = true, Version = 1 };
        var update = new Update()
        {
            Message = new Message()
            {
                MessageId = messageId,
                Text = "Hello, World!",
                Chat = new() { Id = userId, },
                From = new() { Id = userId, FirstName = "John Doe" }
            }
        };

        var repository = new Mock<IRepository>(MockBehavior.Strict);
        repository.Setup(x => x.TryRead(userId, out existingUser)).Returns(true);
        repository.Setup(x => x.TryRead(topicId, out existingTopic)).Returns(true);
        repository.Setup(x => x.TryUpdate(changedTopic)).Returns(true);

        var messenger = new Mock<IMessenger>(MockBehavior.Strict);
        messenger.Setup(x => x.CreateTopic(It.IsAny<string>(), CancellationToken.None)).ReturnsAsync(topicId);
        messenger.Setup(x => x.ForwardToChatAsync(topicId, userId, messageId, CancellationToken.None)).Returns(Task.CompletedTask);
        messenger.Setup(x => x.OpenTopic(topicId, CancellationToken.None)).Returns(Task.CompletedTask);

        var sut = new Handler(Mock.Of<ILogger<Handler>>(), OptionsMock, repository.Object, messenger.Object);

        // Act
        await sut.HandleUpdate(update, CancellationToken.None);

        // Assert
        repository.Verify(x => x.TryRead(userId, out existingUser), Times.Once);
        repository.Verify(x => x.TryRead(topicId, out existingTopic), Times.Once);
        repository.Verify(x => x.TryUpdate(existingTopic), Times.Once);
        messenger.Verify(x => x.OpenTopic(topicId, CancellationToken.None), Times.Once);
        messenger.Verify(x => x.ForwardToChatAsync(topicId, userId, messageId, CancellationToken.None), Times.Once);
    }
}