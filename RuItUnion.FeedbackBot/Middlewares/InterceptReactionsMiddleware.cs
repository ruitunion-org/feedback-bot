using System.Collections.Concurrent;
using Microsoft.FeatureManagement.Mvc;
using RuItUnion.FeedbackBot.Data.Models;
using Telegram.Bot.Extensions.Markup;
using Telegram.Bot.Types.Enums;

namespace RuItUnion.FeedbackBot.Middlewares;

[FeatureGate("Queue")]
public class InterceptReactionsMiddleware(
    IFeedbackBotContext db,
    IOptions<AppOptions> options,
    TimeProvider clock,
    ITelegramBotClient botClient)
    : FrameMiddleware
{
    private static readonly ReactionTypeEmoji likeEmoji = new() { Emoji = @"👍" };

    public static readonly ConcurrentDictionary<int, SemaphoreSlim> ThreadLockers = [];

    public override async Task InvokeAsync(Update update, FrameContext context,
        CancellationToken ct = default)
    {
        if (update.Type is UpdateType.MessageReaction
            && update.MessageReaction?.User is not null
            && update.MessageReaction.Chat.Id == options.Value.FeedbackChatId)
        {
            DbTopic? topic = await db.Topics
                .FirstOrDefaultAsync(x => x.InterceptMessageId == update.MessageReaction.MessageId, ct)
                .ConfigureAwait(false);
            if (topic is not null && update.MessageReaction.User.Id != topic.InterceptCandidateId)
            {
                SemaphoreSlim locker = ThreadLockers.GetOrAdd(topic.ThreadId, _ => new(1, 1));
                await locker.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    if (update.MessageReaction.NewReaction.Any(x =>
                            x is ReactionTypeEmoji emoji && emoji.Emoji == likeEmoji.Emoji)
                        && !topic.InterceptVotes.Contains(update.MessageReaction.User!.Id))
                    {
                        topic.InterceptVotes.Add(update.MessageReaction.User.Id);
                        DateTimeOffset now = clock.GetUtcNow();
                        if (topic.InterceptVotes.Count >= 2
                            || (topic.LockedFrom + options.Value.QueueLockDuration < now
                                && topic.InterceptVotes.Count >= 1))
                        {
                            topic.LastLeavedUserId = topic.AssigneeId;

                            DbQueue? queue = await db.Queue.FirstOrDefaultAsync(x => x.UserId == topic.AssigneeId, ct)
                                .ConfigureAwait(false);
                            queue?.AssignmentPenalty += TimeSpan.FromDays(3);
                            topic.AssigneeId = topic.InterceptCandidateId;

                            topic.IsOpen = true;
                            topic.ResetIntercept(now.UtcDateTime);
                            topic.IsQueued = false;
                            await db.SaveChangesAsync(ct).ConfigureAwait(false);

                            var user = await db.Users.Where(x => x.Id == topic.AssigneeId)
                                .Select(x => new { x.FirstName, x.LastName }).FirstOrDefaultAsync(ct)
                                .ConfigureAwait(false);
                            string name = user is null
                                ? topic.AssigneeId?.ToString(@"D") ?? @"0"
                                : user.LastName is not null
                                    ? @$"{user.FirstName} {user.LastName}"
                                    : user.FirstName;
                            await botClient.SendMessage(options.Value.FeedbackChatId,
                                    ResourceManager.GetString(nameof(InterceptReactionsMiddleware_Result),
                                        context.GetCultureInfo())
                                    + Tools.MentionMarkdown(topic.AssigneeId.GetValueOrDefault(),
                                        Tools.EscapeMarkdown(name)),
                                    messageThreadId: topic.ThreadId,
                                    replyParameters: new()
                                    {
                                        AllowSendingWithoutReply = true,
                                        ChatId = options.Value.FeedbackChatId,
                                        MessageId = update.MessageReaction.MessageId,
                                    }, disableNotification: true, parseMode: ParseMode.MarkdownV2,
                                    cancellationToken: ct)
                                .ConfigureAwait(false);
                        }
                        else
                        {
                            await db.SaveChangesAsync(ct).ConfigureAwait(false);
                        }
                    }
                    else if (topic.InterceptVotes.Contains(update.MessageReaction.User!.Id)
                             && update.MessageReaction.OldReaction.Any(x =>
                                 x is ReactionTypeEmoji emoji && emoji.Emoji == likeEmoji.Emoji)
                             && !update.MessageReaction.NewReaction.Any(x =>
                                 x is ReactionTypeEmoji emoji && emoji.Emoji == likeEmoji.Emoji))
                    {
                        topic.InterceptVotes.Remove(update.MessageReaction.User.Id);
                        await db.SaveChangesAsync(ct).ConfigureAwait(false);
                    }
                }
                finally
                {
                    locker.Release();
                }
            }
        }

        await Next(update, context, ct).ConfigureAwait(false);
    }
}