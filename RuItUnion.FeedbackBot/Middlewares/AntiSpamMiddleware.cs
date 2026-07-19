using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using RuItUnion.FeedbackBot.Data.Models;
using RuItUnion.FeedbackBot.SpamFilters;
using Telegram.Bot.Types.Enums;

namespace RuItUnion.FeedbackBot.Middlewares;

public class AntiSpamMiddleware(
    IOptions<AppOptions> options,
    ITelegramBotClient botClient,
    IFeedbackBotContext db,
    FeedbackMetricsService feedbackMetricsService,
    ILogger<MessageForwarderMiddleware> logger,
    IEnumerable<ISpamFilter>? spamFilters) : FrameMiddleware
{
    private readonly long _chatId = options.Value.FeedbackChatId;

    public override async Task InvokeAsync(Update update, FrameContext context, CancellationToken ct = default)
    {
        long userId = update.Message?.From?.Id ?? 0L;
        if (spamFilters is not null && userId != 0L && update.Message?.Chat.Id != _chatId
            && string.IsNullOrEmpty(context.GetCommandName()))
        {
            bool hasTopic = await db.Topics.AnyAsync(x => x.UserChatId == update.Message!.Chat.Id, ct)
                                .ConfigureAwait(false)
                            || await db.SpamMessages.AnyAsync(x => x.Id == userId, ct).ConfigureAwait(false);
            if (!hasTopic) foreach (ISpamFilter filter in spamFilters)
            {
                if (!filter.IsSpam(update.Message!.Text)) continue;
                EntityEntry<DbSpamMessage> spamEntry = await db.SpamMessages.AddAsync(new()
                {
                    Id = userId,
                    Update = JsonSerializer.SerializeToDocument(update),
                    Reason = filter.Reason,
                }, ct).ConfigureAwait(false);
                await db.Bans.AddAsync(new()
                {
                    Until = DateTime.MaxValue,
                    Description = filter.Reason,
                    UserId = userId,
                }, ct).ConfigureAwait(false);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);

                string message = string.Format(AntiSpamMiddleware_Message, spamEntry.Entity.Id);
                await botClient.SendMessage(_chatId, message, disableNotification: true, parseMode: ParseMode.MarkdownV2, cancellationToken: ct)
                    .ConfigureAwait(false);

                feedbackMetricsService.IncSpamDetected(userId);
                logger.LogInformation(@"Message from {userId} detected as spam", userId);

                return;
            }
        }

        await Next(update, context, ct).ConfigureAwait(false);
    }
}