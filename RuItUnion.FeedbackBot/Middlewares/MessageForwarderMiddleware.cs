using System.Globalization;
using RuItUnion.FeedbackBot.Data.Models;
using Telegram.Bot.Exceptions;
using TgBotFrame.Commands.Authorization.Models;

namespace RuItUnion.FeedbackBot.Middlewares;

public class MessageForwarderMiddleware(
    IOptions<AppOptions> options,
    ITelegramBotClient botClient,
    IFeedbackBotContext db,
    FeedbackMetricsService feedbackMetricsService,
    TopicTitleGenerator topicTitleGenerator,
    ILogger<MessageForwarderMiddleware> logger) : FrameMiddleware
{
    private readonly long _chatId = options.Value.FeedbackChatId;

    public override async Task InvokeAsync(Update update, FrameContext context, CancellationToken ct = default)
    {
        if (update.Message is not null && update.Message?.Chat.Id != _chatId
                                       && string.IsNullOrEmpty(context.GetCommandName()))
        {
            try
            {
                await ProcessMessage(update.Message!, context, ct).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                await botClient.SendMessage(update.Message!.Chat.Id,
                    ResourceManager.GetString(nameof(MessageForwarderMiddleware_Exception), context.GetCultureInfo())!,
                    replyParameters: new()
                    {
                        AllowSendingWithoutReply = true,
                        ChatId = update.Message!.Chat.Id,
                        MessageId = update.Message!.Id,
                    },
                    cancellationToken: ct).ConfigureAwait(false);
                logger.LogError(e, @"Exception during message forwarding");
            }
        }

        await Next(update, context, ct).ConfigureAwait(false);
    }

    private async Task ProcessMessage(Message message, FrameContext context, CancellationToken ct = default)
    {
        DbTopic? dbTopic = await db.Topics.AsNoTracking().FirstOrDefaultAsync(x => x.UserChatId == message.Chat.Id, ct)
            .ConfigureAwait(false);
        if (dbTopic is null)
        {
            dbTopic = await CreateTopic(message, context, ct).ConfigureAwait(false);
        }
        else if (!dbTopic.IsOpen)
        {
            await OpenTopic(message, context, dbTopic, ct).ConfigureAwait(false);
        }

        try
        {
            await botClient.ForwardMessage(_chatId, message.Chat.Id, message.MessageId,
                dbTopic.ThreadId, false, false, ct).ConfigureAwait(false);
            logger.LogInformation(@"Forwarded message {messageId} from chat {chatId} to topic {topicId}", message.Id,
                message.Chat.Id, dbTopic.ThreadId);
        }
        catch (ApiRequestException e) when (string.Equals(e.Message, @"Bad Request: message thread not found",
                                                StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(@"Topic {topicId} not found in chat, creating new topic...", dbTopic.ThreadId);
            await db.Topics.Where(x => x.Id == dbTopic.Id).Take(1).ExecuteDeleteAsync(ct).ConfigureAwait(false);
            await ProcessMessage(message, context, ct).ConfigureAwait(false);
        }

        feedbackMetricsService.IncMessagesForwarded(dbTopic.ThreadId, message.From?.Id ?? 0L);
    }

    protected virtual async Task OpenTopic(Message message, FrameContext context, DbTopic dbTopic,
        CancellationToken ct = default)
    {
        db.Topics.Update(dbTopic).State = EntityState.Unchanged;
        dbTopic.IsOpen = true;
        int? threadId = dbTopic.ThreadId;
        if (threadId is null or < 0)
        {
            db.Topics.Remove(dbTopic);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await ProcessMessage(message, context, ct).ConfigureAwait(false);
        }

        Task saveTask = db.SaveChangesAsync(ct);
        Task editTask =
            botClient.EditForumTopic(_chatId, threadId!.Value, topicTitleGenerator.GetTopicTitle(dbTopic),
                cancellationToken: ct);
        Task reopenTask = botClient.ReopenForumTopic(_chatId, threadId.Value, ct);
        await Task.WhenAll(saveTask, editTask, reopenTask).ConfigureAwait(false);
        logger.LogInformation(@"Reopened topic {topicId}", threadId.Value);
    }

    protected virtual async Task<DbTopic> CreateTopic(Message message, FrameContext context,
        CancellationToken ct = default)
    {
        DbUser user = await db.Users.AsTracking().FirstAsync(x => x.Id == message.From!.Id, ct)
            .ConfigureAwait(false);

        DbTopic dbTopic = new()
        {
            ThreadId = default,
            IsOpen = true,
            UserChatId = message.Chat.Id,
            User = user,
        };
        ForumTopic topic = await botClient
            .CreateForumTopic(_chatId, topicTitleGenerator.GetTopicTitle(dbTopic), cancellationToken: ct)
            .ConfigureAwait(false);
        dbTopic.ThreadId = topic.MessageThreadId;
        await db.Topics.AddAsync(dbTopic, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        logger.LogInformation(@"Created topic {topicId} for user {username} with id = {userId}", topic.MessageThreadId,
            user.UserName, user.Id);
        await CreateInfoMessage(context, dbTopic, user, ct).ConfigureAwait(false);
        return dbTopic;
    }

    protected virtual async Task CreateInfoMessage(FrameContext context, DbTopic topic, DbUser user,
        CancellationToken ct = default)
    {
        CultureInfo culture = context.GetCultureInfo();
        string noData = ResourceManager.GetString(nameof(UserInfoMessage_NoData), culture)!;
        string message = ResourceManager.GetString(nameof(UserInfoMessage), culture)!;
        string username = user.UserName is not null ? @"@" + user.UserName : noData;
        message = string.Format(message, user.FirstName, user.LastName ?? noData, username, user.Id,
            culture.NativeName);

        Message result = await botClient
            .SendMessage(_chatId, message, messageThreadId: topic.ThreadId, cancellationToken: ct)
            .ConfigureAwait(false);
        logger.LogInformation(@"Sent head message for topic {topicId}", result.MessageThreadId);
        await botClient.PinChatMessage(result.Chat.Id, result.MessageId, cancellationToken: ct).ConfigureAwait(false);
    }
}