using Microsoft.FeatureManagement;
using Microsoft.FeatureManagement.Mvc;
using RuItUnion.FeedbackBot.Data.Models;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Extensions.Markup;
using Telegram.Bot.Types.Enums;

namespace RuItUnion.FeedbackBot.Services;

[FeatureGate("Queue")]
public class QueueService(
    IOptions<AppOptions> options,
    IFeedbackBotContext db,
    FeedbackMetricsService metrics,
    IVariantFeatureManager featureManager,
    ITelegramBotClient botClient,
    TopicTitleGenerator topicTitleGenerator,
    TimeProvider clock)
{
    public enum EnqueueResult : sbyte
    {
        Ok,
        AlreadyInQueue = -1,
        Mentor = -2,
    }

    private static readonly SemaphoreSlim _assignLock = new(1, 1);
    private readonly long _chatId = options.Value.FeedbackChatId;

    public IOrderedQueryable<DbQueue> GetQueue() => db.Queue.OrderByDescending(x => x.Mentors.Count > 0)
        .ThenBy(x => x.LastAssignmentTime + x.AssignmentPenalty).ThenByDescending(x => x.UserId);

    public async Task<EnqueueResult> Enqueue(long userId, CancellationToken cts = default)
    {
        DbQueue queueElement = await db.Queue.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.UserId == userId, cts)
                                   .ConfigureAwait(false)
                               ?? (await db.Queue.AddAsync(new()
                               {
                                   UserId = userId,
                                   InQueue = false,
                               }, cts).ConfigureAwait(false)).Entity;

        if (queueElement.InQueue) return EnqueueResult.AlreadyInQueue;

        bool isMentor = await db.Mentors.AnyAsync(x => x.MentorId == userId, cts).ConfigureAwait(false);
        if (isMentor) return EnqueueResult.Mentor;

        queueElement.InQueue = true;
        await db.SaveChangesAsync(cts).ConfigureAwait(false);
        metrics.Enqueue(userId);
        return EnqueueResult.Ok;
    }

    public async Task<bool> Dequeue(long userId, CancellationToken cts = default)
    {
        int count = await db.Queue.IgnoreQueryFilters().Where(x => x.UserId == userId && x.InQueue)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.InQueue, false), cts).ConfigureAwait(false);
        return count > 0;
    }


    public async Task AssignQueue(CancellationToken ct = default)
    {
        if (!await featureManager.IsEnabledAsync(@"Queue", ct).ConfigureAwait(false)) return;
        await _assignLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            do
            {
                DbTopic? topic = await db.Topics.OrderBy(x => x.Created).FirstOrDefaultAsync(x => x.IsQueued, ct)
                    .ConfigureAwait(false);
                if (topic is null) return;

                DbQueue? queueElement =
                    await GetQueue().Include(x => x.User).FirstOrDefaultAsync(ct).ConfigureAwait(false);
                if (queueElement is null) return;
                if (topic.LastLeavedUserId == queueElement.UserId)
                {
                    queueElement = await GetQueue().Skip(1).FirstOrDefaultAsync(ct).ConfigureAwait(false);
                    if (queueElement is null) return;
                }

                DateTime now = clock.GetUtcNow().UtcDateTime;

                TimeSpan remaining = queueElement.AssignmentPenalty - (now - queueElement.LastAssignmentTime);
                if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
                queueElement.AssignmentPenalty =
                    TimeSpan.FromTicks(remaining.Ticks / 2L).Add(options.Value.CooldownDuration);

                queueElement.LastAssignmentTime = now;

                topic.AssigneeId = queueElement.UserId;
                topic.IsQueued = false;
                topic.ResetIntercept(now);

                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                metrics.Dequeue(queueElement.UserId, true);
                await botClient.SendMessage(_chatId,
                        string.Format(ResourceManager.GetString(nameof(AssignmentController_InterceptFast))!, queueElement.User.UserName is not null
                            ? @"@" + queueElement.User.UserName
                            : Tools.MentionHtml(topic.AssigneeId.Value, queueElement.User.FirstName
                                                                        + (queueElement.User.LastName?.Length > 0
                                                                            ? @" " + queueElement.User.LastName
                                                                            : string.Empty))),
                        messageThreadId: topic.ThreadId,
                        disableNotification: false, parseMode: ParseMode.Html, cancellationToken: ct)
                    .ConfigureAwait(false);

                try
                {
                    await botClient.EditForumTopic(_chatId, topic.ThreadId, topicTitleGenerator.GetTopicTitle(topic),
                            cancellationToken: ct)
                        .ConfigureAwait(false);
                }
                catch (ApiRequestException e) when (e.Message == @"Bad Request: TOPIC_NOT_MODIFIED")
                {
                }
            } while (true);
        }
        finally
        {
            _assignLock.Release();
        }
    }
}