using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RuItUnion.FeedbackBot.Data;
using RuItUnion.FeedbackBot.Data.Models;
using RuItUnion.FeedbackBot.Options;
using RuItUnion.FeedbackBot.Services;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using TgBotFrame.Commands.Authorization.Models;
using TgBotFrame.Commands.Extensions;
using TgBotFrame.Middleware;

namespace RuItUnion.FeedbackBot.Middlewares;

public class MessageForwarderMiddleware(
    IOptions<AppOptions> options,
    ITelegramBotClient botClient,
    FeedbackBotContext db,
    FeedbackMetricsService feedbackMetricsService) : FrameMiddleware
{
    private readonly long _chatId = options.Value.FeedbackChatId;

    public override async Task InvokeAsync(Update update, FrameContext context, CancellationToken ct = default)
    {
        if (update.Message is not null && update.Message?.Chat.Id != _chatId
                                       && string.IsNullOrEmpty(context.GetCommandName()))
        {
            await ProcessMessage(update.Message!, context, ct).ConfigureAwait(false);
        }

        await Next(update, context, ct).ConfigureAwait(false);
    }

    private async Task ProcessMessage(Message message, FrameContext context, CancellationToken ct = default)
    {
        DbTopic? dbTopic = await db.Topics.AsNoTracking().FirstOrDefaultAsync(x => x.UserChatId == message.Chat.Id, ct)
            .ConfigureAwait(false);
        if (dbTopic is null)
        {
            DbUser user = await db.Users.AsTracking().FirstAsync(x => x.Id == message.From!.Id, ct)
                .ConfigureAwait(false);

            dbTopic = new()
            {
                ThreadId = Random.Shared.Next(int.MinValue, 0),
                IsOpen = true,
                UserChatId = message.Chat.Id,
                User = user,
            };
            await db.Topics.AddAsync(dbTopic, ct).ConfigureAwait(false);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            ForumTopic topic = await botClient.CreateForumTopic(_chatId, dbTopic.ToString(), cancellationToken: ct)
                .ConfigureAwait(false);
            await db.Topics.ExecuteUpdateAsync(x => x.SetProperty(y => y.ThreadId, topic.MessageThreadId), ct)
                .ConfigureAwait(false);
            db.Topics.Update(dbTopic).State = EntityState.Detached;
            dbTopic.ThreadId = topic.MessageThreadId;
            await CreateInfoMessage(context, dbTopic, user, ct).ConfigureAwait(false);
        }
        else if (!dbTopic.IsOpen)
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

            Task<int> saveTask = db.SaveChangesAsync(ct);
            Task editTask =
                botClient.EditForumTopic(_chatId, threadId!.Value, dbTopic.ToString(), cancellationToken: ct);
            Task reopenTask = botClient.ReopenForumTopic(_chatId, threadId.Value, ct);
            await Task.WhenAll(saveTask, editTask, reopenTask).ConfigureAwait(false);
        }

        try
        {
            await botClient.ForwardMessage(_chatId, message.Chat.Id, message.MessageId,
                dbTopic.ThreadId, false, false, ct).ConfigureAwait(false);
        }
        catch (ApiRequestException e) when (string.Equals(e.Message, @"Bad Request: message thread not found",
                                                StringComparison.OrdinalIgnoreCase))
        {
            await db.Topics.Where(x => x.Id == dbTopic.Id).Take(1).ExecuteDeleteAsync(ct).ConfigureAwait(false);
            await ProcessMessage(message, context, ct).ConfigureAwait(false);
        }

        feedbackMetricsService.IncMessagesForwarded(dbTopic.ThreadId, message.From?.Id ?? 0L);
    }

    private async Task CreateInfoMessage(FrameContext context, DbTopic topic, DbUser user,
        CancellationToken ct = default)
    {
        string noData = ResourceManager.GetString(nameof(UserInfoMessage_NoData), context.GetCultureInfo())!;
        string message = ResourceManager.GetString(nameof(UserInfoMessage), context.GetCultureInfo())!;
        string username = user.UserName is not null ? @"@" + user.UserName : noData;
        message = string.Format(message, user.FirstName, user.LastName ?? noData, username, user.Id,
            context.GetCultureInfo().NativeName);

        Message result = await botClient
            .SendMessage(_chatId, message, messageThreadId: topic.ThreadId, cancellationToken: ct)
            .ConfigureAwait(false);
        await botClient.PinChatMessage(result.Chat.Id, result.MessageId, cancellationToken: ct).ConfigureAwait(false);
    }
}