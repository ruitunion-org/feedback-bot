using System.Net;
using System.Text;
using Microsoft.FeatureManagement.Mvc;
using RuItUnion.FeedbackBot.Data.Models;
using RuItUnion.FeedbackBot.Middlewares;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Extensions.Markup;
using Telegram.Bot.Types.Enums;
using TgBotFrame.Commands;
using TgBotFrame.Commands.Attributes;
using TgBotFrame.Commands.Authorization.Attributes;
using TgBotFrame.Commands.Authorization.Models;

namespace RuItUnion.FeedbackBot.Commands;

[CommandController("Assignment")]
[FeatureGate("Queue")]
public class AssignmentController(
    IOptions<AppOptions> options,
    QueueService queueService,
    IFeedbackBotContext db,
    TimeProvider clock,
    TopicTitleGenerator topicTitleGenerator,
    ITelegramBotClient botClient,
    ILogger<AssignmentController> logger) : CommandControllerBase
{
    private readonly long _chatId = options.Value.FeedbackChatId;

    [Command(nameof(Queue))]
    [Restricted("admin")]
    public Task Queue() => Queue(false);

    [Command(nameof(Queue))]
    [Restricted("admin")]
    public async Task Queue(bool extended)
    {
        StringBuilder text = new(256);
        int num = 0;
        foreach (DbQueue queue in queueService.GetQueue().AsNoTracking().Include(x => x.User).Include(x => x.Mentors)
                     .ThenInclude(x => x.Mentor))
        {
            if (num > 0) text.AppendLine();
            string name = queue.User.LastName is null
                ? queue.User.FirstName
                : @$"{queue.User.FirstName} {queue.User.LastName}";
            text.Append(@$"{++num}. ");
            text.Append(Tools.MentionHtml(queue.UserId,
                WebUtility.HtmlEncode(name)));

            foreach (DbMentor queueMentor in queue.Mentors)
            {
                text.Append(@" + ");
                string mentorName = queueMentor.Mentor.LastName is null
                    ? queueMentor.Mentor.FirstName
                    : @$"{queueMentor.Mentor.FirstName} {queueMentor.Mentor.LastName}";
                text.Append(Tools.MentionHtml(queueMentor.MentorId, WebUtility.HtmlEncode(mentorName)));
            }

            if (extended) text.Append(@$" <code>[{queue.LastAssignmentTime:u} + ({queue.AssignmentPenalty:c})]</code>");
        }

        await SendReplyMessage(text.Length == 0
            ? ResourceManager.GetString(nameof(AssignmentController_QueueIsEmpty), Context.GetCultureInfo())!
            : text.ToString()).ConfigureAwait(false);
    }

    [Command(nameof(QueueStatus))]
    [Restricted("admin")]
    public async Task QueueStatus()
    {
        long? userId = Context.GetUserId();
        if (userId is null) return;
        DbQueue[] queue = await queueService.GetQueue().AsNoTracking().ToArrayAsync().ConfigureAwait(false);
        DbQueue? me = queue.FirstOrDefault(x => x.UserId == userId);
        if (me is null)
            await SendReplyMessage(ResourceManager.GetString(nameof(AssignmentController_NotInQueue),
                Context.GetCultureInfo())!).ConfigureAwait(false);
        else
            await SendReplyMessage(string.Format(
                ResourceManager.GetString(nameof(AssignmentController_QueueNumber), Context.GetCultureInfo())!,
                queue.IndexOf(me) + 1)).ConfigureAwait(false);
    }

    [Command(nameof(Enqueue))]
    [Restricted("admin")]
    public async Task Enqueue()
    {
        long? userId = Context.GetUserId();
        if (userId == null) return;
        QueueService.EnqueueResult result =
            await queueService.Enqueue(userId.Value, CancellationToken).ConfigureAwait(false);
        await SendReplyMessage(result switch
        {
            QueueService.EnqueueResult.Ok => ResourceManager.GetString(nameof(AssignmentController_EnqueueOk),
                Context.GetCultureInfo())!,
            QueueService.EnqueueResult.AlreadyInQueue => ResourceManager.GetString(
                nameof(AssignmentController_EnqueueAlreadyInQueue), Context.GetCultureInfo())!,
            QueueService.EnqueueResult.Mentor =>
                ResourceManager.GetString(nameof(AssignmentController_EnqueueMentor), Context.GetCultureInfo())!,
            _ => throw new ArgumentOutOfRangeException(),
        }, CancellationToken).ConfigureAwait(false);
        await queueService.AssignQueue().ConfigureAwait(false);
    }

    [Command(nameof(Dequeue))]
    [Restricted("admin")]
    public async Task Dequeue()
    {
        long? userId = Context.GetUserId();
        if (userId == null) return;
        await SendReplyMessage(await queueService.Dequeue(userId.Value).ConfigureAwait(false)
                ? ResourceManager.GetString(nameof(AssignmentController_DequeueOk), Context.GetCultureInfo())!
                : ResourceManager.GetString(nameof(AssignmentController_DequeueNotModified), Context.GetCultureInfo())!)
            .ConfigureAwait(false);
    }

    [Command(nameof(Abandon))]
    [Restricted("admin")]
    public Task Abandon() => Abandon(Context.GetThreadId().GetValueOrDefault(-1));


    [Command(nameof(Abandon))]
    [Restricted("admin")]
    public async Task Abandon(int threadId)
    {
        long? userId = Context.GetUserId();
        DbTopic? topic = threadId == -1
            ? null
            : await db.Topics
                .AsTracking()
                .Include(x => x.User)
                .FirstOrDefaultAsync(x => x.ThreadId == threadId, CancellationToken).ConfigureAwait(false);
        if (topic == null)
        {
            await SendReplyMessage(
                ResourceManager.GetString(nameof(InternalThreadController_Close_TopicNotFound),
                    Context.GetCultureInfo())!, CancellationToken).ConfigureAwait(false);
            return;
        }


        if (topic.AssigneeId == userId)
        {
            if (await db.Mentors.AnyAsync(x => x.NoviceId == userId, CancellationToken).ConfigureAwait(false))
            {
                await SendReplyMessage(
                        ResourceManager.GetString(nameof(AssignmentController_AbandonNovice),
                            Context.GetCultureInfo())!,
                        CancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                topic.ResetIntercept(DateTime.MinValue);
                topic.LastLeavedUserId = topic.AssigneeId;
                topic.AssigneeId = null;
                topic.IsQueued = true;

                DbQueue? queueElement =
                    await db.Queue.FirstOrDefaultAsync(x => x.UserId == userId, CancellationToken)
                        .ConfigureAwait(false);
                queueElement?.AssignmentPenalty = queueElement.AssignmentPenalty.Add(TimeSpan.FromDays(1));

                await db.SaveChangesAsync(CancellationToken).ConfigureAwait(false);

                await SendReplyMessage(
                        ResourceManager.GetString(nameof(AssignmentController_AbandonSuccess),
                            Context.GetCultureInfo())!,
                        CancellationToken)
                    .ConfigureAwait(false);

                await queueService.AssignQueue(CancellationToken).ConfigureAwait(false);
            }
        }
        else if (await db.Mentors
                     .AnyAsync(x => x.NoviceId == topic.AssigneeId && x.MentorId == userId, CancellationToken)
                     .ConfigureAwait(false))
        {
            topic.ResetIntercept(DateTime.MinValue);
            topic.LastLeavedUserId = topic.AssigneeId;
            topic.AssigneeId = null;
            topic.IsQueued = true;
            await db.SaveChangesAsync(CancellationToken).ConfigureAwait(false);

            await SendReplyMessage(
                ResourceManager.GetString(nameof(AssignmentController_AbandonMentor), Context.GetCultureInfo())!,
                CancellationToken).ConfigureAwait(false);

            await queueService.AssignQueue(CancellationToken).ConfigureAwait(false);
        }
        else
        {
            await SendReplyMessage(
                ResourceManager.GetString(nameof(AssignmentController_AbandonForbidden), Context.GetCultureInfo())!,
                CancellationToken).ConfigureAwait(false);
        }
    }

    [Command(nameof(Assignee))]
    [Restricted("admin")]
    public Task Assignee() => Assignee(Update.Message?.MessageThreadId ?? -1);

    [Command(nameof(Assignee))]
    [Restricted("admin")]
    public async Task Assignee(int threadId)
    {
        DbTopic? topic = threadId == -1
            ? null
            : await db.Topics
                .AsNoTracking()
                .Include(x => x.User)
                .FirstOrDefaultAsync(x => x.ThreadId == threadId, CancellationToken).ConfigureAwait(false);
        if (topic is not null)
            await SendReplyMessage(topic.AssigneeId is null
                    ? ResourceManager.GetString(nameof(AssignmentController_AssigneeNone), Context.GetCultureInfo())!
                    : string.Format(
                        ResourceManager.GetString(nameof(AssignmentController_AssigneeOk), Context.GetCultureInfo())!,
                        Tools.MentionHtml(topic.AssigneeId.Value, topic.User.ToString())), CancellationToken)
                .ConfigureAwait(false);
        else
            await SendReplyMessage(
                ResourceManager.GetString(nameof(InternalThreadController_Close_TopicNotFound),
                    Context.GetCultureInfo())!, CancellationToken).ConfigureAwait(false);
    }

    [Command(nameof(Intercept))]
    [Restricted("admin")]
    public async Task Intercept()
    {
        int? topicId = Update.Message?.MessageThreadId;
        DbTopic? topic;
        if (topicId is null || Update.Message?.Chat.Id != _chatId || (topic = await db.Topics.AsTracking()
                .FirstOrDefaultAsync(x => x.ThreadId == topicId, CancellationToken).ConfigureAwait(false)) is null)
        {
            await SendReplyMessage(
                    ResourceManager.GetString(nameof(AssignmentController_InterceptInvalid), Context.GetCultureInfo())!,
                    CancellationToken)
                .ConfigureAwait(false);
            return;
        }

        switch (topic)
        {
            case { IsQueued: true, AssigneeId: null }:
                await SendReplyMessage(
                        ResourceManager.GetString(nameof(AssignmentController_InterceptQueue),
                            Context.GetCultureInfo())!,
                        CancellationToken)
                    .ConfigureAwait(false);
                break;
            case { IsQueued: false, AssigneeId: null }:
                topic.ResetIntercept(clock.GetUtcNow().UtcDateTime);
                topic.AssigneeId = Context.GetUserId();

                await db.SaveChangesAsync(CancellationToken).ConfigureAwait(false);

                try
                {
                    await botClient.EditForumTopic(_chatId, topic.ThreadId, topicTitleGenerator.GetTopicTitle(topic))
                        .ConfigureAwait(false);
                }
                catch (ApiRequestException e) when (e.Message == @"Bad Request: TOPIC_NOT_MODIFIED")
                {
                }

                DbUser? user = await db.Users.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == Context.GetUserId(), CancellationToken).ConfigureAwait(false);
                if (user is not null)
                    await botClient.SendMessage(_chatId,
                            string.Format(
                                ResourceManager.GetString(nameof(AssignmentController_InterceptFast),
                                    Context.GetCultureInfo())!, user.UserName is not null
                                    ? @"@" + user.UserName
                                    : Tools.MentionHtml(topic.AssigneeId!.Value, user.FirstName
                                        + (user.LastName?.Length > 0
                                            ? @" " + user.LastName
                                            : string.Empty))),
                            messageThreadId: topic.ThreadId,
                            disableNotification: false, parseMode: ParseMode.Html, cancellationToken: CancellationToken)
                        .ConfigureAwait(false);
                break;
            case { AssigneeId: not null, InterceptCandidateId: not null }:
                await SendReplyMessage(
                        ResourceManager.GetString(nameof(AssignmentController_InterceptConflict),
                            Context.GetCultureInfo())!,
                        CancellationToken)
                    .ConfigureAwait(false);
                break;
            case { AssigneeId: not null, InterceptCandidateId: null }:
                long userId = Context.GetUserId() ?? throw new ArgumentNullException(nameof(Context));
                DbUser? interceptUser = await db.Users.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == userId, CancellationToken).ConfigureAwait(false);
                if (interceptUser is null)
                {
                    logger.LogError(@"User with id = {id} not found", userId);
                    await SendReplyMessage(ResourceManager.GetString(nameof(AssignmentController_UserNotFound),
                                Context.GetCultureInfo())!,
                            CancellationToken)
                        .ConfigureAwait(false);
                    return;
                }

                if (interceptUser.Id == topic.AssigneeId)
                {
                    await SendReplyMessage(
                            ResourceManager.GetString(nameof(AssignmentController_InterceptSelf),
                                Context.GetCultureInfo())!,
                            CancellationToken)
                        .ConfigureAwait(false);
                    return;
                }

                SemaphoreSlim locker =
                    InterceptReactionsMiddleware.ThreadLockers.GetOrAdd(topic.ThreadId, _ => new(1, 1));
                await locker.WaitAsync(CancellationToken).ConfigureAwait(false);
                Message? answer = null;
                try
                {
                    answer = await SendReplyMessage(
                            string.Format(
                                ResourceManager.GetString(nameof(AssignmentController_InterceptStart),
                                    Context.GetCultureInfo())!,
                                interceptUser),
                            CancellationToken)
                        .ConfigureAwait(false);
                    topic.InterceptCandidateId = userId;
                    topic.InterceptMessageId = answer.Id;
                    topic.InterceptVotes.Clear();
                    await db.SaveChangesAsync(CancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    if (answer is not null)
                        await botClient.DeleteMessage(answer.Chat.Id, answer.Id, CancellationToken)
                            .ConfigureAwait(false);
                    logger.LogError(e, @"Exception on topic intercept");
                }
                finally
                {
                    locker.Release();
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(topic));
        }
    }

    [Command(nameof(SetMentor))]
    [Restricted("admin")]
    public Task SetMentor() =>
        SetMentor(Context.GetUserId() ?? -1, Update.Message?.ReplyToMessage?.From?.Id ?? -1);

    [Command(nameof(SetMentor))]
    [Restricted("admin")]
    public Task SetMentor(long noviceId) =>
        SetMentor(Context.GetUserId() ?? -1, noviceId);

    [Command(nameof(SetMentor))]
    [Restricted("admin")]
    public async Task SetMentor(long mentorId, long noviceId)
    {
        if (mentorId == -1 || noviceId == -1)
        {
            await SendReplyMessage(
                    ResourceManager.GetString(nameof(AssignmentController_SetMentorInvalidReply),
                        Context.GetCultureInfo())!,
                    CancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (mentorId == noviceId)
        {
            await SendReplyMessage(
                    ResourceManager.GetString(nameof(AssignmentController_SetMentorSelf), Context.GetCultureInfo())!,
                    CancellationToken)
                .ConfigureAwait(false);
            return;
        }

        DbMentor? mentorship = await db.Mentors
            .IgnoreQueryFilters()
            .AsTracking()
            .Include(x => x.Mentor)
            .Include(x => x.Novice)
            .FirstOrDefaultAsync(x => x.MentorId == mentorId && x.NoviceId == noviceId,
                CancellationToken)
            .ConfigureAwait(false);

        if (mentorship?.IsActive is true)
        {
            await SendReplyMessage(
                    ResourceManager.GetString(nameof(AssignmentController_SetMentorConflict),
                        Context.GetCultureInfo())!, CancellationToken)
                .ConfigureAwait(false);
            return;
        }

        DbUser? mentor = mentorship?.Mentor
                         ?? await db.Users.FirstOrDefaultAsync(x => x.Id == mentorId, CancellationToken)
                             .ConfigureAwait(false);
        DbUser? novice = mentorship?.Novice
                         ?? await db.Users.FirstOrDefaultAsync(x => x.Id == noviceId, CancellationToken)
                             .ConfigureAwait(false);

        if (mentor is null || novice is null)
        {
            await SendReplyMessage(
                    ResourceManager.GetString(nameof(AssignmentController_MentorOrNoviceNotFound),
                        Context.GetCultureInfo())!, CancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (mentorship is null)
            db.Mentors.Add(new()
            {
                MentorId = mentorId,
                NoviceId = noviceId,
                IsActive = true,
            });
        else
            mentorship.IsActive = true;

        await db.Queue.Where(x => x.UserId == mentorId).ExecuteUpdateAsync(x => x.SetProperty(y => y.InQueue, false))
            .ConfigureAwait(false);

        await db.SaveChangesAsync(CancellationToken).ConfigureAwait(false);

        await queueService.AssignQueue(CancellationToken).ConfigureAwait(false);

        await SendReplyMessage(
                string.Format(
                    ResourceManager.GetString(nameof(AssignmentController_SetMentorSuccess), Context.GetCultureInfo())!,
                    mentor, novice),
                CancellationToken)
            .ConfigureAwait(false);
    }

    [Command(nameof(FreeMentor))]
    [Restricted("admin")]
    public Task FreeMentor() =>
        FreeMentor(Update.Message?.From?.Id ?? -1, Update.Message?.ReplyToMessage?.From?.Id ?? -1);

    [Command(nameof(FreeMentor))]
    [Restricted("admin")]
    public Task FreeMentor(long noviceId) => FreeMentor(Update.Message?.From?.Id ?? -1, noviceId);

    [Command(nameof(FreeMentor))]
    [Restricted("admin")]
    public async Task FreeMentor(long mentorId, long noviceId)
    {
        DbMentor? mentor = mentorId < 0 || noviceId < 0
            ? null
            : await db.Mentors.Include(x => x.Mentor).Include(x => x.Novice)
                .Where(x => x.MentorId == mentorId && x.NoviceId == noviceId).FirstOrDefaultAsync()
                .ConfigureAwait(false);

        if (mentor is null)
        {
            await SendReplyMessage(ResourceManager.GetString(nameof(AssignmentController_MentorNotFound),
                Context.GetCultureInfo())!).ConfigureAwait(false);
            return;
        }

        mentor.IsActive = false;
        await db.SaveChangesAsync().ConfigureAwait(false);

        await queueService.AssignQueue().ConfigureAwait(false);

        await SendReplyMessage(string.Format(
            ResourceManager.GetString(nameof(AssignmentController_FreeMentorSuccess), Context.GetCultureInfo())!,
            mentor.Mentor, mentor.Novice)).ConfigureAwait(false);
    }

    [Command(nameof(AddQueue))]
    [Restricted("admin")]
    public Task AddQueue() => Abandon(Context.GetThreadId().GetValueOrDefault(-1));

    [Command(nameof(AddQueue))]
    [Restricted("admin")]
    public async Task AddQueue(long threadId)
    {
        long? userId = Context.GetUserId();
        DbTopic? topic = threadId == -1
            ? null
            : await db.Topics
                .AsTracking()
                .Include(x => x.User)
                .FirstOrDefaultAsync(x => x.ThreadId == threadId, CancellationToken).ConfigureAwait(false);
        if (topic == null)
        {
            await SendReplyMessage(
                ResourceManager.GetString(nameof(InternalThreadController_Close_TopicNotFound),
                    Context.GetCultureInfo())!, CancellationToken).ConfigureAwait(false);
            return;
        }
        if (!topic.IsQueued)
        {
            await SendReplyMessage(ResourceManager.GetString(nameof(AssignmentController_AddQueue_NotModified),
                Context.GetCultureInfo())!, CancellationToken).ConfigureAwait(false);
        }

        topic.IsQueued = true;
        await db.SaveChangesAsync().ConfigureAwait(false);

        await queueService.AssignQueue().ConfigureAwait(false);
        await SendReplyMessage(ResourceManager.GetString(nameof(AssignmentController_AddQueue_Ok),
            Context.GetCultureInfo())!, CancellationToken).ConfigureAwait(false);
    }

    [Command(nameof(RemoveQueue))]
    [Restricted("admin")]
    public Task RemoveQueue() => Abandon(Context.GetThreadId().GetValueOrDefault(-1));

    [Command(nameof(RemoveQueue))]
    [Restricted("admin")]
    public async Task RemoveQueue(long threadId)
    {
        long? userId = Context.GetUserId();
        DbTopic? topic = threadId == -1
            ? null
            : await db.Topics
                .AsTracking()
                .Include(x => x.User)
                .FirstOrDefaultAsync(x => x.ThreadId == threadId, CancellationToken).ConfigureAwait(false);
        if (topic == null)
        {
            await SendReplyMessage(
                ResourceManager.GetString(nameof(InternalThreadController_Close_TopicNotFound),
                    Context.GetCultureInfo())!, CancellationToken).ConfigureAwait(false);
            return;
        }

        if (!topic.IsQueued)
        {
            await SendReplyMessage(ResourceManager.GetString(nameof(AssignmentController_RemoveQueue_NotModified),
                Context.GetCultureInfo())!, CancellationToken).ConfigureAwait(false);
        }

        if (topic.AssigneeId is not null && (userId != topic.AssigneeId || !await db.RoleMembers.AnyAsync(x => x.RoleId == -50 && x.UserId == userId).ConfigureAwait(false)))
        {
            await SendReplyMessage(ResourceManager.GetString(nameof(AssignmentController_RemoveQueue_Forbidden),
                Context.GetCultureInfo())!, CancellationToken).ConfigureAwait(false);
            return;
        }

        topic.IsQueued = false;
        topic.AssigneeId = null;
        topic.ResetIntercept(clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync().ConfigureAwait(false);

        await queueService.AssignQueue().ConfigureAwait(false);
        await SendReplyMessage(ResourceManager.GetString(nameof(AssignmentController_RemoveQueue_Ok),
            Context.GetCultureInfo())!, CancellationToken).ConfigureAwait(false);
    }

    private async Task<Message> SendReplyMessage(string text, CancellationToken cts = default) =>
        await botClient.SendMessage(Context.GetChatId()!,
                text,
                messageThreadId: Update.Message?.MessageThreadId,
                replyParameters: new()
                {
                    AllowSendingWithoutReply = true,
                    ChatId = Context.GetChatId()!,
                    MessageId = Context.GetMessageId().GetValueOrDefault(-1),
                }, disableNotification: true, parseMode: ParseMode.Html, cancellationToken: cts)
            .ConfigureAwait(false);
}