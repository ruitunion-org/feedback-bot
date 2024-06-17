using Microsoft.Extensions.Options;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bot;

public class Handler(
    ILogger<Handler> _logger,
    IOptions<AppOptions> _options,
    IRepository _repository,
    IMessenger _telegram)
{
    private readonly string _youWereBanned = "Вы больше не можете отправлять сообщения в бота.";
    private readonly string _unableToBanAdmin = "Нельзя забанить администратора чата";
    private readonly string _userBanned = "Пользователь забанен";
    private readonly string _userUnbanned = "Пользователь разбанен";
    private readonly string _messageDeleted = "Сообщение удалено";
    private readonly string _userBlockedTheBot = "Пользователь заблокировал бота";
    private readonly string _error = "🪲 Что-то пошло не так...";

    public Task HandleUpdate(Update update, CancellationToken ct)
    {
        // Определяем тип сообщения
        IInput? input = update switch
        {
            // Команда от пользователя в бота
            {
                Message:
                {
                    Text: not null,
                    From: not null,
                    Type: MessageType.Text
                } m
            } when m.From.Id == m.Chat.Id && m.Text.StartsWith('/') =>
                new CommandFromUser(m.From.Id, m.Text),
            // Сообщение от пользователя в бота
            {
                Message:
                {
                    Text: not null,
                    From: not null, Type:
                    MessageType.Text
                } m
            } when m.From.Id == m.Chat.Id =>
                new MessageFromUser(m.From.Id, m.MessageId, m.From.FirstName, m.From.LastName, m.From.Username, m.Text),
            // Контент от пользователя в бота
            {
                Message:
                {
                    From: not null,
                    Type: MessageType.Photo or MessageType.Document
                } m
            } when m.From.Id == m.Chat.Id =>
                new MessageFromUser(m.From.Id, m.MessageId, m.From.FirstName, m.From.LastName, m.From.Username, string.Empty),
            // Реплай на сообщение пользователя в чате обратной связи
            // То, что это сообщение от пользователя определяем по 2 признакам:
            // 1. ForwardDate не null
            // 2. ReplyToMessage.From.Id == Id бота (бот делал форвард сообщения пользователя)
            {
                Message:
                {
                    Text: not null,
                    From: not null,
                    Type: MessageType.Text,
                    MessageThreadId: not null,
                    ReplyToMessage: { From: not null, ForwardOrigin: not null }
                } m
            } when !m.Text.StartsWith('/') &&
                m.ReplyToMessage.From.Id == _options.Value.FeedbackBotId =>
                    new MessageFromChat(m.Chat.Id, m.From.Id, m.MessageThreadId.Value, m.MessageId, m.Text),
            // Реплай с контентом
            {
                Message:
                {
                    From: not null,
                    Type: MessageType.Photo or MessageType.Document,
                    MessageThreadId: not null,
                    ReplyToMessage: { From: not null, ForwardOrigin: not null }
                } m
            } when m.ReplyToMessage.From.Id == _options.Value.FeedbackBotId =>
                new MessageFromChat(m.Chat.Id, m.From.Id, m.MessageThreadId.Value, m.MessageId, string.Empty),
            // Команда в чате обратной связи
            {
                Message:
                {
                    Text: not null,
                    From: not null,
                    Type: MessageType.Text,
                    MessageThreadId: not null
                } m
            } when m.Text.StartsWith('/') =>
                new CommandFromChat(m.Chat.Id, m.From.Id, m.MessageThreadId.Value, m.Text, m.ReplyToMessage?.MessageId, m.ReplyToMessage?.From?.Id),
            _ => null
        };

        // Игнорируем неподдерживаемые сообщения
        if (input is null)
        {
            return Task.CompletedTask;
        }

        return input switch
        {
            // Пользователь бота
            MessageFromUser msg => ForwardToChat(msg, ct),
            CommandFromUser { Command: "/start" } c => Start(c, ct),
            CommandFromUser { Command: "/help" } c => Help(c, ct),
            // Администраторы чата обратной связи
            MessageFromChat msg => ForwardToUser(msg, ct),
            CommandFromChat { Command: "/start" } c => Start(c, ct),
            CommandFromChat { Command: "/help" } c => Help(c, ct),
            CommandFromChat { Command: "/ban" } c => Ban(c, ct),
            CommandFromChat { Command: "/unban" } c => Unban(c, ct),
            CommandFromChat { Command: "/delete" } c => Delete(c, ct),
            CommandFromChat { Command: "/open" } c => Open(c, ct),
            CommandFromChat { Command: "/close" } c => Close(c, ct),
            // Игнорируем всё остальное
            _ => Task.CompletedTask
        };
    }

    private async Task Start(CommandFromUser command, CancellationToken ct)
    {
        await _telegram.SendToUserAsync(command.UserId, _options.Value.Start, ct);
    }

    private async Task Help(CommandFromUser command, CancellationToken ct)
    {
        await _telegram.SendToUserAsync(command.UserId, _options.Value.Help, ct);
    }

    private async Task ForwardToChat(MessageFromUser message, CancellationToken ct)
    {
        // Пользователь пишет впервые
        if (!_repository.TryRead(message.UserId, out User? user))
        {
            long topicId = await _telegram.CreateTopic(message.ToString(), ct);

            user = new User()
            {
                Id = message.UserId,
                TopicId = topicId,
                Topic = new Topic()
                {
                    Id = topicId,
                    IsOpen = true,
                    UserId = message.UserId
                }
            };

            _repository.Create(user);
        }

        // Проверяем бан
        if (user.Banned)
        {
            await _telegram.SendToUserAsync(message.UserId, _youWereBanned, ct);
            return;
        }

        if (!_repository.TryRead(user.TopicId, out Topic? topic))
        {
            _logger.LogError("Unable to find topic with id {id}.", user.TopicId);
            return;
        }

        // Проверяем открыт ли топик
        if (!topic.IsOpen)
        {
            topic.Open();
            _repository.TryUpdate(topic);
            await _telegram.OpenTopic(topic.Id, ct);
        }

        // Форвардим сообщение в чат обратной связи
        await _telegram.ForwardToChatAsync(topic.Id, topic.UserId, message.MessageId, ct);
    }

    private async Task ForwardToUser(MessageFromChat message, CancellationToken ct)
    {
        if (!_repository.TryRead(message.TopicId, out Topic? topic))
        {
            _logger.LogError("Unable to find topic with id {id}.", message.TopicId);
            return;
        }

        var botMessageId = await _telegram.ForwardToUserAsync(topic.UserId, message.MessageId, ct);

        if (!botMessageId.HasValue)
        {
            await _telegram.SendToChatAsync(message.TopicId, _userBlockedTheBot, ct);
            return;
        }

        // Сохраняем реплай для возможности удаления
        var reply = new Reply()
        {
            Id = message.MessageId,
            TopicId = topic.Id,
            BotMessageId = botMessageId.Value,
        };

        _repository.Create(reply);
    }

    private Task Start(CommandFromChat command, CancellationToken ct) =>
        _telegram.SendToChatAsync(command.TopicId, _options.Value.Start, ct);

    private Task Help(CommandFromChat command, CancellationToken ct) =>
        _telegram.SendToChatAsync(command.TopicId, _options.Value.Help, ct);

    private async Task Ban(CommandFromChat command, CancellationToken ct)
    {
        // Команда доступна только админам
        var admins = await _telegram.GetChatAdmins(ct);
        if (!admins.Contains(command.UserId)) return;

        if (!_repository.TryRead(command.TopicId, out Topic? topic))
        {
            _logger.LogError("Unable to find topic with id {id}.", command.TopicId);
            return;
        }

        // Нельзя забанить администраторов чата обратной связи
        if (admins.Contains(topic.UserId))
        {
            await _telegram.SendToChatAsync(command.TopicId, _unableToBanAdmin, ct);
            return;
        }

        if (!_repository.TryRead(topic.UserId, out User? user))
        {
            _logger.LogError("Unable to find user with id {id}.", command.UserId);
            return;
        }

        // Баним пользователя
        if (!user.Banned)
        {
            user.Ban();

            if (!_repository.TryUpdate(user))
            {
                await _telegram.SendToChatAsync(command.TopicId, _error, ct);
                return;
            }
        }

        await _telegram.SendToChatAsync(command.TopicId, _userBanned, ct);
    }

    private async Task Unban(CommandFromChat command, CancellationToken ct)
    {
        // Команда доступна только админам
        var admins = await _telegram.GetChatAdmins(ct);
        if (!admins.Contains(command.UserId)) return;

        if (!_repository.TryRead(command.TopicId, out Topic? topic))
        {
            _logger.LogError("Unable to find topic with id {id}.", command.TopicId);
            return;
        }

        if (!_repository.TryRead(topic.UserId, out User? user))
        {
            _logger.LogError("Unable to find user with id {id}.", command.UserId);
            return;
        }

        // Снимаем бан
        if (user.Banned)
        {
            user.Unban();

            if (!_repository.TryUpdate(user))
            {
                await _telegram.SendToChatAsync(command.TopicId, _error, ct);
                return;
            }
        }

        await _telegram.SendToChatAsync(command.TopicId, _userUnbanned, ct);
    }

    private async Task Delete(CommandFromChat command, CancellationToken ct)
    {
        // Проверяем, что команда - это реплай на сообщение
        if (!command.ReplyToUserId.HasValue || !command.ReplyToMessageId.HasValue) return;

        // Удалить можно только свой реплай 
        if (command.ReplyToUserId.Value != command.UserId) return;

        // Удаляем реплай, если ещё не удалён
        if (_repository.TryRead(command.ReplyToMessageId.Value, out Reply? reply))
        {
            if (!_repository.TryRead(command.TopicId, out Topic? topic))
            {
                _logger.LogError("Unable to find topic with id {id}.", command.TopicId);
                return;
            }
            await _telegram.DeleteMessageAsync(topic.UserId, reply.BotMessageId, ct);
            _repository.Delete<Reply>(command.ReplyToMessageId.Value);
            await _telegram.SendToChatAsync(command.TopicId, _messageDeleted, ct);
        }
    }

    private async Task Open(CommandFromChat command, CancellationToken ct)
    {
        // Команда доступна только админам
        var admins = await _telegram.GetChatAdmins(ct);
        if (!admins.Contains(command.UserId)) return;

        if (!_repository.TryRead(command.TopicId, out Topic? topic))
        {
            _logger.LogError("Unable to find topic with id {id}.", command.TopicId);
            return;
        }

        // Топик уже открыт
        if (topic.IsOpen) return;

        await _telegram.OpenTopic(command.TopicId, ct);

        topic.Open();
        if (!_repository.TryUpdate(topic))
        {
            await _telegram.SendToChatAsync(command.TopicId, _error, ct);
            return;
        }
    }

    private async Task Close(CommandFromChat command, CancellationToken ct)
    {
        // Команда доступна только админам
        var admins = await _telegram.GetChatAdmins(ct);
        if (!admins.Contains(command.UserId)) return;
        
        if (!_repository.TryRead(command.TopicId, out Topic? topic))
        {
            _logger.LogError("Unable to find topic with id {id}.", command.TopicId);
            return;
        }

        // Топик уже закрыт
        if (!topic.IsOpen) return;

        await _telegram.CloseTopic(command.TopicId, ct);

        topic.Close();
        if (!_repository.TryUpdate(topic))
        {
            await _telegram.SendToChatAsync(command.TopicId, _error, ct);
            return;
        }
    }

    public Task HandleError(Exception e, CancellationToken ct)
    {
        _logger.LogError(e, "Unhandled exception");
        return Task.CompletedTask;
    }
}