using Microsoft.Extensions.Logging;
using Moq;
using Telegram.Bot.Types;

namespace Bot.Tests;

public partial class HandlerTests
{
    [DataTestMethod]
    [DataRow("/start", "Start")]
    [DataRow("/help", "Help")]
    public async Task Command_from_chat_causes_hardcoded_response(string command, string hardcodedResponse)
    {
        // Arrange
        var feedbackChatUserId = 11111;
        var topicId = 22222;
        var chatMessageId = 33333;
        var update = new Update()
        {
            Message = new Message()
            {
                MessageId = chatMessageId,
                MessageThreadId = topicId,
                Text = command,
                Chat = new() { Id = OptionsMock.Value.FeedbackChatId },
                From = new() { Id = feedbackChatUserId, FirstName = "Feedback Chat User" }
            }
        };

        var repository = new Mock<IRepository>(MockBehavior.Strict);

        var messenger = new Mock<IMessenger>(MockBehavior.Strict);
        messenger.Setup(x => x.SendToChatAsync(topicId, It.Is<string>(x => x.Equals(hardcodedResponse)), CancellationToken.None)).Returns(Task.CompletedTask);

        var sut = new Handler(Mock.Of<ILogger<Handler>>(), OptionsMock, repository.Object, messenger.Object);

        // Act
        await sut.HandleUpdate(update, CancellationToken.None);

        // Assert
        messenger.Verify(x => x.SendToChatAsync(topicId, It.Is<string>(x => x.Equals(hardcodedResponse)), CancellationToken.None), Times.Once);
    }

    [DataTestMethod]
    [DataRow("/ban")]
    [DataRow("/unban")]
    [DataRow("/open")]
    [DataRow("/close")]
    public async Task Command_is_avaiable_only_to_chat_admins(string command)
    {
        // Arrange
        var feedbackChatUserId = 11111;
        var topicId = 22222;
        var chatMessageId = 33333;
        var update = new Update()
        {
            Message = new Message()
            {
                MessageId = chatMessageId,
                MessageThreadId = topicId,
                Text = command,
                Chat = new() { Id = OptionsMock.Value.FeedbackChatId },
                From = new() { Id = feedbackChatUserId, FirstName = "Feedback Chat User" }
            }
        };

        var repository = new Mock<IRepository>(MockBehavior.Strict);
        var messenger = new Mock<IMessenger>(MockBehavior.Strict);
        messenger.Setup(x => x.GetChatAdmins(CancellationToken.None)).Returns(Task.FromResult((long[])[1L, 2L, 3L]));

        var sut = new Handler(Mock.Of<ILogger<Handler>>(), OptionsMock, repository.Object, messenger.Object);

        // Act
        await sut.HandleUpdate(update, CancellationToken.None);

        // Assert
        messenger.Verify(x => x.GetChatAdmins(CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task Unable_to_ban_admins()
    {
        // Arrange
        var adminId = 11111;
        var topicId = 22222;
        var chatMessageId = 33333;
        var existingTopic = new Topic() { Id = topicId, IsOpen = true, UserId = adminId };
        var update = new Update()
        {
            Message = new Message()
            {
                MessageId = chatMessageId,
                MessageThreadId = topicId,
                Text = "/ban",
                Chat = new() { Id = OptionsMock.Value.FeedbackChatId },
                From = new() { Id = adminId, FirstName = "Administrator" }
            }
        };

        var repository = new Mock<IRepository>(MockBehavior.Strict);
        repository.Setup(x => x.TryRead(topicId, out existingTopic)).Returns(true);

        var messenger = new Mock<IMessenger>(MockBehavior.Strict);
        messenger.Setup(x => x.GetChatAdmins(CancellationToken.None)).ReturnsAsync([11111, 2, 3]);
        messenger.Setup(x => x.SendToChatAsync(topicId, "Нельзя забанить администратора чата", CancellationToken.None)).Returns(Task.CompletedTask);

        var sut = new Handler(Mock.Of<ILogger<Handler>>(), OptionsMock, repository.Object, messenger.Object);

        // Act
        await sut.HandleUpdate(update, CancellationToken.None);

        // Assert
        repository.Verify(x => x.TryRead(topicId, out existingTopic), Times.Once);
        messenger.Verify(x => x.GetChatAdmins(CancellationToken.None), Times.Once);
        messenger.Verify(x => x.SendToChatAsync(topicId, "Нельзя забанить администратора чата", CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task Ban_command_causes_user_ban()
    {
        // Arrange
        var adminId = 11111;
        var userId = 44444;
        var topicId = 22222;
        var chatMessageId = 33333;
        var user = new User() { Id = userId, TopicId = topicId };
        var bannedUser = user with { Version = 1, Banned = true };
        var topic = new Topic() { Id = topicId, IsOpen = true, UserId = userId };
        var update = new Update()
        {
            Message = new Message()
            {
                MessageId = chatMessageId,
                MessageThreadId = topicId,
                Text = "/ban",
                Chat = new() { Id = OptionsMock.Value.FeedbackChatId },
                From = new() { Id = adminId, FirstName = "Administrator" }
            }
        };

        var repository = new Mock<IRepository>(MockBehavior.Strict);
        repository.Setup(x => x.TryRead(topicId, out topic)).Returns(true);
        repository.Setup(x => x.TryRead(userId, out user)).Returns(true);
        repository.Setup(x => x.TryUpdate(bannedUser)).Returns(true);

        var messenger = new Mock<IMessenger>(MockBehavior.Strict);
        messenger.Setup(x => x.GetChatAdmins(CancellationToken.None)).ReturnsAsync([11111, 2, 3]);
        messenger.Setup(x => x.SendToChatAsync(topicId, "Пользователь забанен", CancellationToken.None)).Returns(Task.CompletedTask);

        var sut = new Handler(Mock.Of<ILogger<Handler>>(), OptionsMock, repository.Object, messenger.Object);

        // Act
        await sut.HandleUpdate(update, CancellationToken.None);

        // Assert
        repository.Verify(x => x.TryRead(topicId, out topic), Times.Once);
        repository.Verify(x => x.TryRead(userId, out user), Times.Once);
        repository.Verify(x => x.TryUpdate(bannedUser), Times.Once);
        messenger.Verify(x => x.GetChatAdmins(CancellationToken.None), Times.Once);
        messenger.Verify(x => x.SendToChatAsync(topicId, "Пользователь забанен", CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task Already_banned_user()
    {
        // Arrange
        var adminId = 11111;
        var userId = 44444;
        var topicId = 22222;
        var chatMessageId = 33333;
        var user = new User() { Id = userId, TopicId = topicId, Banned = true };
        var topic = new Topic() { Id = topicId, IsOpen = true, UserId = userId };
        var update = new Update()
        {
            Message = new Message()
            {
                MessageId = chatMessageId,
                MessageThreadId = topicId,
                Text = "/ban",
                Chat = new() { Id = OptionsMock.Value.FeedbackChatId },
                From = new() { Id = adminId, FirstName = "Administrator" }
            }
        };

        var repository = new Mock<IRepository>(MockBehavior.Strict);
        repository.Setup(x => x.TryRead(topicId, out topic)).Returns(true);
        repository.Setup(x => x.TryRead(userId, out user)).Returns(true);

        var messenger = new Mock<IMessenger>(MockBehavior.Strict);
        messenger.Setup(x => x.GetChatAdmins(CancellationToken.None)).ReturnsAsync([11111, 2, 3]);
        messenger.Setup(x => x.SendToChatAsync(topicId, "Пользователь забанен", CancellationToken.None)).Returns(Task.CompletedTask);

        var sut = new Handler(Mock.Of<ILogger<Handler>>(), OptionsMock, repository.Object, messenger.Object);

        // Act
        await sut.HandleUpdate(update, CancellationToken.None);

        // Assert
        repository.Verify(x => x.TryRead(topicId, out topic), Times.Once);
        repository.Verify(x => x.TryRead(userId, out user), Times.Once);
        messenger.Verify(x => x.GetChatAdmins(CancellationToken.None), Times.Once);
        messenger.Verify(x => x.SendToChatAsync(topicId, "Пользователь забанен", CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task Unban_command_causes_user_unban()
    {
        // Arrange
        var adminId = 11111;
        var userId = 44444;
        var topicId = 22222;
        var chatMessageId = 33333;
        var user = new User() { Id = userId, TopicId = topicId, Banned = true };
        var unbannedUser = user with { Version = 1, Banned = false };
        var topic = new Topic() { Id = topicId, IsOpen = true, UserId = userId };
        var update = new Update()
        {
            Message = new Message()
            {
                MessageId = chatMessageId,
                MessageThreadId = topicId,
                Text = "/unban",
                Chat = new() { Id = OptionsMock.Value.FeedbackChatId },
                From = new() { Id = adminId, FirstName = "Administrator" }
            }
        };

        var repository = new Mock<IRepository>(MockBehavior.Strict);
        repository.Setup(x => x.TryRead(topicId, out topic)).Returns(true);
        repository.Setup(x => x.TryRead(userId, out user)).Returns(true);
        repository.Setup(x => x.TryUpdate(unbannedUser)).Returns(true);

        var messenger = new Mock<IMessenger>(MockBehavior.Strict);
        messenger.Setup(x => x.GetChatAdmins(CancellationToken.None)).ReturnsAsync([11111, 2, 3]);
        messenger.Setup(x => x.SendToChatAsync(topicId, "Пользователь разбанен", CancellationToken.None)).Returns(Task.CompletedTask);

        var sut = new Handler(Mock.Of<ILogger<Handler>>(), OptionsMock, repository.Object, messenger.Object);

        // Act
        await sut.HandleUpdate(update, CancellationToken.None);

        // Assert
        repository.Verify(x => x.TryRead(topicId, out topic), Times.Once);
        repository.Verify(x => x.TryRead(userId, out user), Times.Once);
        repository.Verify(x => x.TryUpdate(unbannedUser), Times.Once);
        messenger.Verify(x => x.GetChatAdmins(CancellationToken.None), Times.Once);
        messenger.Verify(x => x.SendToChatAsync(topicId, "Пользователь разбанен", CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task Already_unbanned_user()
    {
        // Arrange
        var adminId = 11111;
        var userId = 44444;
        var topicId = 22222;
        var chatMessageId = 33333;
        var user = new User() { Id = userId, TopicId = topicId, Banned = false };
        var topic = new Topic() { Id = topicId, IsOpen = true, UserId = userId };
        var update = new Update()
        {
            Message = new Message()
            {
                MessageId = chatMessageId,
                MessageThreadId = topicId,
                Text = "/unban",
                Chat = new() { Id = OptionsMock.Value.FeedbackChatId },
                From = new() { Id = adminId, FirstName = "Administrator" }
            }
        };

        var repository = new Mock<IRepository>(MockBehavior.Strict);
        repository.Setup(x => x.TryRead(topicId, out topic)).Returns(true);
        repository.Setup(x => x.TryRead(userId, out user)).Returns(true);

        var messenger = new Mock<IMessenger>(MockBehavior.Strict);
        messenger.Setup(x => x.GetChatAdmins(CancellationToken.None)).ReturnsAsync([11111, 2, 3]);
        messenger.Setup(x => x.SendToChatAsync(topicId, "Пользователь разбанен", CancellationToken.None)).Returns(Task.CompletedTask);

        var sut = new Handler(Mock.Of<ILogger<Handler>>(), OptionsMock, repository.Object, messenger.Object);

        // Act
        await sut.HandleUpdate(update, CancellationToken.None);

        // Assert
        repository.Verify(x => x.TryRead(topicId, out topic), Times.Once);
        repository.Verify(x => x.TryRead(userId, out user), Times.Once);
        messenger.Verify(x => x.GetChatAdmins(CancellationToken.None), Times.Once);
        messenger.Verify(x => x.SendToChatAsync(topicId, "Пользователь разбанен", CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task Delete_command_causes_reply_delete()
    {
        // Arrange
        var feedbackChatUserId = 11111;
        var userId = 44444;
        var topicId = 22222;
        var chatMessageId = 33333;
        var replyMessageId = 5555;
        var botMessageId = 1232131242;
        var topic = new Topic() { Id = topicId, IsOpen = true, UserId = userId };
        var reply = new Reply() { Id = replyMessageId, BotMessageId = botMessageId };
        var update = new Update()
        {
            Message = new Message()
            {
                MessageId = chatMessageId,
                MessageThreadId = topicId,
                Text = "/delete",
                Chat = new() { Id = OptionsMock.Value.FeedbackChatId },
                From = new() { Id = feedbackChatUserId, FirstName = "Feedback User Id" },
                ReplyToMessage = new Message()
                {
                    MessageId = replyMessageId,
                    From = new() { Id = feedbackChatUserId },
                    ForwardOrigin = new MessageOriginUser()
                    {
                        Date = DateTime.Now
                    }
                }
            }
        };

        var repository = new Mock<IRepository>(MockBehavior.Strict);
        repository.Setup(x => x.TryRead(replyMessageId, out reply)).Returns(true);
        repository.Setup(x => x.TryRead(topicId, out topic)).Returns(true);
        repository.Setup(x => x.Delete<Reply>(replyMessageId));

        var messenger = new Mock<IMessenger>(MockBehavior.Strict);
        messenger.Setup(x => x.DeleteMessageAsync(userId, botMessageId, CancellationToken.None)).Returns(Task.CompletedTask);
        messenger.Setup(x => x.SendToChatAsync(topicId, "Сообщение удалено", CancellationToken.None)).Returns(Task.CompletedTask);

        var sut = new Handler(Mock.Of<ILogger<Handler>>(), OptionsMock, repository.Object, messenger.Object);

        // Act
        await sut.HandleUpdate(update, CancellationToken.None);

        // Assert
        repository.Verify(x => x.TryRead(replyMessageId, out reply), Times.Once);
        repository.Verify(x => x.TryRead(topicId, out topic), Times.Once);
        repository.Verify(x => x.Delete<Reply>(replyMessageId), Times.Once);
        messenger.Verify(x => x.DeleteMessageAsync(userId, botMessageId, CancellationToken.None));
        messenger.Verify(x => x.SendToChatAsync(topicId, "Сообщение удалено", CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task Chat_user_can_delete_only_his_own_replies()
    {
        // Arrange
        var feedbackChatUserId = 11111;
        var topicId = 22222;
        var chatMessageId = 33333;
        var update = new Update()
        {
            Message = new Message()
            {
                MessageId = chatMessageId,
                MessageThreadId = topicId,
                Text = "/delete",
                Chat = new() { Id = OptionsMock.Value.FeedbackChatId },
                From = new() { Id = feedbackChatUserId, FirstName = "Feedback User Id" },
                ReplyToMessage = new Message()
                {
                    MessageId = 1235135,
                    From = new() { Id = 112412 },
                    ForwardOrigin = new MessageOriginUser()
                    {
                        Date = DateTime.Now
                    }
                }
            }
        };

        var repository = new Mock<IRepository>(MockBehavior.Strict);
        var messenger = new Mock<IMessenger>(MockBehavior.Strict);

        var sut = new Handler(Mock.Of<ILogger<Handler>>(), OptionsMock, repository.Object, messenger.Object);

        // Act
        await sut.HandleUpdate(update, CancellationToken.None);

        // Assert (no invocations should be performed)
    }

    [TestMethod]
    public async Task Open_command_causes_topic_opening()
    {
        // Arrange
        var feedbackChatUserId = 11111;
        var userId = 44444;
        var topicId = 22222;
        var chatMessageId = 33333;
        var topic = new Topic() { Id = topicId, UserId = userId };
        var openedTopic = topic with { Version = 1, IsOpen = true };
        var update = new Update()
        {
            Message = new Message()
            {
                MessageId = chatMessageId,
                MessageThreadId = topicId,
                Text = "/open",
                Chat = new() { Id = OptionsMock.Value.FeedbackChatId },
                From = new() { Id = feedbackChatUserId, FirstName = "Feedback User Id" }
            }
        };

        var repository = new Mock<IRepository>(MockBehavior.Strict);
        repository.Setup(x => x.TryRead(topicId, out topic)).Returns(true);
        repository.Setup(x => x.TryUpdate(openedTopic)).Returns(true);

        var messenger = new Mock<IMessenger>(MockBehavior.Strict);
        messenger.Setup(x => x.GetChatAdmins(CancellationToken.None)).ReturnsAsync([11111, 2, 3]);
        messenger.Setup(x => x.OpenTopic(topicId, CancellationToken.None)).Returns(Task.CompletedTask);

        var sut = new Handler(Mock.Of<ILogger<Handler>>(), OptionsMock, repository.Object, messenger.Object);

        // Act
        await sut.HandleUpdate(update, CancellationToken.None);

        // Assert
        repository.Verify(x => x.TryRead(topicId, out topic), Times.Once);
        repository.Verify(x => x.TryUpdate(openedTopic), Times.Once);
        messenger.Verify(x => x.GetChatAdmins(CancellationToken.None), Times.Once);
        messenger.Verify(x => x.OpenTopic(topicId, CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task Close_command_causes_topic_closing()
    {
        // Arrange
        var feedbackChatUserId = 11111;
        var userId = 44444;
        var topicId = 22222;
        var chatMessageId = 33333;
        var topic = new Topic() { Id = topicId, IsOpen = true, UserId = userId };
        var openedTopic = topic with { Version = 1, IsOpen = false };
        var update = new Update()
        {
            Message = new Message()
            {
                MessageId = chatMessageId,
                MessageThreadId = topicId,
                Text = "/close",
                Chat = new() { Id = OptionsMock.Value.FeedbackChatId },
                From = new() { Id = feedbackChatUserId, FirstName = "Feedback User Id" }
            }
        };

        var repository = new Mock<IRepository>(MockBehavior.Strict);
        repository.Setup(x => x.TryRead(topicId, out topic)).Returns(true);
        repository.Setup(x => x.TryUpdate(openedTopic)).Returns(true);

        var messenger = new Mock<IMessenger>(MockBehavior.Strict);
        messenger.Setup(x => x.GetChatAdmins(CancellationToken.None)).ReturnsAsync([11111, 2, 3]);
        messenger.Setup(x => x.CloseTopic(topicId, CancellationToken.None)).Returns(Task.CompletedTask);

        var sut = new Handler(Mock.Of<ILogger<Handler>>(), OptionsMock, repository.Object, messenger.Object);

        // Act
        await sut.HandleUpdate(update, CancellationToken.None);

        // Assert
        repository.Verify(x => x.TryRead(topicId, out topic), Times.Once);
        repository.Verify(x => x.TryUpdate(openedTopic), Times.Once);
        messenger.Verify(x => x.GetChatAdmins(CancellationToken.None), Times.Once);
        messenger.Verify(x => x.CloseTopic(topicId, CancellationToken.None), Times.Once);
    }
}