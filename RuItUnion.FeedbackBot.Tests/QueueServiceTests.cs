using Microsoft.EntityFrameworkCore;
using Moq;
using RuItUnion.FeedbackBot.Data;
using RuItUnion.FeedbackBot.Data.Models;
using RuItUnion.FeedbackBot.Properties;
using RuItUnion.FeedbackBot.Services;
using Telegram.Bot;
using Telegram.Bot.Requests;
using Telegram.Bot.Types.Enums;
using TgBotFrame.Commands.Authorization.Models;

namespace RuItUnion.FeedbackBot.Tests;

/// <summary>
///     Тесты QueueService на реальном PostgreSQL (Testcontainers).
///     Покрывают порядок очереди, вставание/выход, назначение тем,
///     распад штрафа и обход TOPIC_NOT_MODIFIED.
/// </summary>
[Collection(nameof(FeedbackBotDatabaseCollection))]
public class QueueServiceTests(TestDatabase db)
{
    private static readonly DateTime _base = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan _cooldown = TimeSpan.FromHours(6);

    private FeedbackBotContext NewContext() => db.CreateContext();

    private static (Mock<ITelegramBotClient> mock, ITelegramBotClient client) NewBot()
    {
        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        TestBotClient.WithEditForumTopic(mock);
        return (mock, mock.Object);
    }

    private static QueueService NewService(
        FeedbackBotContext ctx, ITelegramBotClient client,
        bool featureEnabled = true, TimeProvider? clock = null, TimeSpan? cooldown = null) =>
        QueueServiceFactory.Create(ctx, client, featureEnabled, clock, cooldown ?? _cooldown);

    // ================= GetQueue: порядок =================

    [Fact]
    public async Task GetQueue_NovicesFirst()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1, "nomentor");
        TestDb.User(ctx, 2, "novice");
        TestDb.User(ctx, 3, "mentor");
        TestDb.QueueElement(ctx, 1);
        TestDb.QueueElement(ctx, 2);
        TestDb.Mentor(ctx, 3, 2);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        QueueService service = NewService(ctx, TestBotClient.Create().Object);

        List<long> order =
        [
            .. (await service.GetQueue().ToListAsync(TestContext.Current.CancellationToken))
            .Select(x => x.UserId),
        ];

        Assert.Equal([2, 1], order);
    }

    [Fact]
    public async Task GetQueue_OrdersByLastAssignmentPlusPenaltyThenUserIdDesc()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 100);
        TestDb.User(ctx, 150);
        TestDb.User(ctx, 200);
        // e3 (150): Base + 1h  — минимальная сумма, идёт первым
        TestDb.QueueElement(ctx, 150, lastAssignmentTime: _base, assignmentPenalty: TimeSpan.FromHours(1));
        // e1 (100) и e2 (200): Base + 2h — равенство, решается UserId по убыванию → 200 перед 100
        TestDb.QueueElement(ctx, 100, lastAssignmentTime: _base, assignmentPenalty: TimeSpan.FromHours(2));
        TestDb.QueueElement(ctx, 200, lastAssignmentTime: _base, assignmentPenalty: TimeSpan.FromHours(2));
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        QueueService service = NewService(ctx, TestBotClient.Create().Object);

        List<long> order =
        [
            .. (await service.GetQueue().ToListAsync(TestContext.Current.CancellationToken))
            .Select(x => x.UserId),
        ];

        Assert.Equal([150, 200, 100], order);
    }

    [Fact]
    public async Task GetQueue_HidesNotInQueue()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1);
        TestDb.User(ctx, 2);
        TestDb.QueueElement(ctx, 1);
        TestDb.QueueElement(ctx, 2, false);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        QueueService service = NewService(ctx, TestBotClient.Create().Object);

        List<long> order =
        [
            .. (await service.GetQueue().ToListAsync(TestContext.Current.CancellationToken))
            .Select(x => x.UserId),
        ];

        Assert.Equal([1], order);
    }

    // ================= Enqueue =================

    [Fact]
    public async Task Enqueue_NewUser_CreatesActiveRow()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        QueueService service = NewService(ctx, TestBotClient.Create().Object);

        QueueService.EnqueueResult result = await service.Enqueue(1, TestContext.Current.CancellationToken);

        DbQueue row = await ctx.Queue.IgnoreQueryFilters()
            .SingleAsync(x => x.UserId == 1, TestContext.Current.CancellationToken);
        Assert.Equal(QueueService.EnqueueResult.Ok, result);
        Assert.True(row.InQueue);
    }

    [Fact]
    public async Task Enqueue_InactiveRow_Reactivates()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1);
        TestDb.QueueElement(ctx, 1, false);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        QueueService service = NewService(ctx, TestBotClient.Create().Object);

        QueueService.EnqueueResult result = await service.Enqueue(1, TestContext.Current.CancellationToken);

        DbQueue row = await ctx.Queue.IgnoreQueryFilters()
            .SingleAsync(x => x.UserId == 1, TestContext.Current.CancellationToken);
        Assert.Equal(QueueService.EnqueueResult.Ok, result);
        Assert.True(row.InQueue);
    }

    [Fact]
    public async Task Enqueue_AlreadyActive_Rejected()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1);
        TestDb.QueueElement(ctx, 1);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        QueueService service = NewService(ctx, TestBotClient.Create().Object);

        QueueService.EnqueueResult result = await service.Enqueue(1, TestContext.Current.CancellationToken);

        Assert.Equal(QueueService.EnqueueResult.AlreadyInQueue, result);
    }

    [Fact]
    public async Task Enqueue_ActiveMentor_RejectedAndStaysInactive()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1, "mentor"); // будущий вставший = ментор
        TestDb.User(ctx, 2, "novice");
        TestDb.QueueElement(ctx, 2); // у новичка нужна строка в очереди (FK)
        TestDb.Mentor(ctx, 1, 2);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        QueueService service = NewService(ctx, TestBotClient.Create().Object);

        QueueService.EnqueueResult result = await service.Enqueue(1, TestContext.Current.CancellationToken);
        Assert.Equal(QueueService.EnqueueResult.Mentor, result);

        DbQueue? row = await ctx.Queue.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.UserId == 1, TestContext.Current.CancellationToken);
        if (row is not null) Assert.False(row.InQueue);
    }

    // ================= Dequeue =================

    [Fact]
    public async Task Dequeue_Active_ReturnsTrueAndClears()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1);
        TestDb.QueueElement(ctx, 1);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        QueueService service = NewService(ctx, TestBotClient.Create().Object);

        bool result = await service.Dequeue(1, TestContext.Current.CancellationToken);
        Assert.True(result);

        int activeCount = await ctx.Queue.IgnoreQueryFilters()
            .CountAsync(x => x.UserId == 1 && x.InQueue, TestContext.Current.CancellationToken);
        Assert.Equal(0, activeCount);
    }

    [Fact]
    public async Task Dequeue_NotInQueue_ReturnsFalse()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1);
        TestDb.QueueElement(ctx, 1, false);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        QueueService service = NewService(ctx, TestBotClient.Create().Object);

        bool result = await service.Dequeue(1, TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    // ================= AssignQueue =================

    [Fact]
    public async Task AssignQueue_FeatureDisabled_Noop()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1, "owner");
        TestDb.User(ctx, 2, "assignee");
        DbTopic topic = TestDb.Topic(ctx, 1, 1000, _base, true);
        TestDb.QueueElement(ctx, 2);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        (Mock<ITelegramBotClient> mock, ITelegramBotClient client) = NewBot();
        QueueService service = NewService(ctx, client, false);

        await service.AssignQueue(TestContext.Current.CancellationToken);

        DbTopic reloaded = await ctx.Topics.SingleAsync(x => x.Id == topic.Id, TestContext.Current.CancellationToken);
        Assert.True(reloaded.IsQueued);
        Assert.Null(reloaded.AssigneeId);
        Assert.Equal(0, TestBotClient.Count(mock, typeof(SendMessageRequest)));
    }

    [Fact]
    public async Task AssignQueue_NoQueuedTopic_Noop()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1, "owner");
        TestDb.User(ctx, 2, "assignee");
        TestDb.Topic(ctx, 1, 1000, _base);
        TestDb.QueueElement(ctx, 2);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        (Mock<ITelegramBotClient> mock, ITelegramBotClient client) = NewBot();
        QueueService service = NewService(ctx, client);

        await service.AssignQueue(TestContext.Current.CancellationToken);

        Assert.Equal(0, TestBotClient.Count(mock, typeof(SendMessageRequest)));
    }

    [Fact]
    public async Task AssignQueue_NoQueueElements_Noop()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1, "owner");
        TestDb.User(ctx, 2);
        DbTopic topic = TestDb.Topic(ctx, 1, 1000, _base, true);
        TestDb.QueueElement(ctx, 2, false);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        (Mock<ITelegramBotClient> mock, ITelegramBotClient client) = NewBot();
        QueueService service = NewService(ctx, client);

        await service.AssignQueue(TestContext.Current.CancellationToken);

        DbTopic reloaded = await ctx.Topics.SingleAsync(x => x.Id == topic.Id, TestContext.Current.CancellationToken);
        Assert.True(reloaded.IsQueued);
        Assert.Null(reloaded.AssigneeId);
        Assert.Equal(0, TestBotClient.Count(mock, typeof(SendMessageRequest)));
    }

    [Fact]
    public async Task AssignQueue_AssignsOldestTopicToFirstQueueElement()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1, "owner");
        TestDb.User(ctx, 2, "ivan");
        DbTopic topic = TestDb.Topic(
            ctx, 1, 1000, _base, true,
            interceptCandidateId: 1, interceptMessageId: 77);
        TestDb.QueueElement(ctx, 2);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        (Mock<ITelegramBotClient> mock, ITelegramBotClient client) = NewBot();
        QueueService service = NewService(ctx, client);

        await service.AssignQueue(TestContext.Current.CancellationToken);

        DbTopic reloaded = await ctx.Topics.SingleAsync(x => x.Id == topic.Id, TestContext.Current.CancellationToken);
        Assert.False(reloaded.IsQueued);
        Assert.Equal(2, reloaded.AssigneeId);
        // ResetIntercept(now)
        Assert.Null(reloaded.InterceptCandidateId);
        Assert.Null(reloaded.InterceptMessageId);
        Assert.Empty(reloaded.InterceptVotes);
        Assert.Equal(QueueServiceFactory.DefaultNow, reloaded.LockedFrom);

        DbQueue q = await ctx.Queue.IgnoreQueryFilters()
            .SingleAsync(x => x.UserId == 2, TestContext.Current.CancellationToken);
        Assert.Equal(QueueServiceFactory.DefaultNow, q.LastAssignmentTime);
        // штраф был 0, остаток < 0 → обнулён → новый = cooldown
        Assert.Equal(_cooldown, q.AssignmentPenalty);

        SendMessageRequest msg =
            TestBotClient.LastSendMessage(mock) ?? throw new("SendMessage not called");
        Assert.Equal(QueueServiceFactory.FEEDBACK_CHAT_ID, msg.ChatId);
        Assert.Equal(1000, msg.MessageThreadId);
        Assert.Equal(ParseMode.Html, msg.ParseMode);
        string expected = string.Format(
            TestResources.GetString(nameof(Resources.AssignmentController_InterceptFast)), "@ivan");
        Assert.Equal(expected, msg.Text);

        EditForumTopicRequest edit =
            TestBotClient.LastEditForumTopic(mock) ?? throw new("EditForumTopic not called");
        Assert.Equal(QueueServiceFactory.FEEDBACK_CHAT_ID, edit.ChatId);
        Assert.Equal(1000, edit.MessageThreadId);
    }

    [Fact]
    public async Task AssignQueue_PenaltyDecaysByHalf()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1, "owner");
        TestDb.User(ctx, 2, "ivan");
        TestDb.Topic(ctx, 1, 1000, _base, true);
        // прошло 4h, штраф 8h → остаток 4h → новый = 4h/2 + cooldown = 2h + 6h = 8h
        TestDb.QueueElement(
            ctx, 2,
            lastAssignmentTime: QueueServiceFactory.DefaultNow.AddHours(-4),
            assignmentPenalty: TimeSpan.FromHours(8));
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        (Mock<ITelegramBotClient> _, ITelegramBotClient client) = NewBot();
        QueueService service = NewService(ctx, client);

        await service.AssignQueue(TestContext.Current.CancellationToken);

        DbQueue q = await ctx.Queue.IgnoreQueryFilters()
            .SingleAsync(x => x.UserId == 2, TestContext.Current.CancellationToken);
        Assert.Equal(TimeSpan.FromHours(8), q.AssignmentPenalty);
    }

    [Fact]
    public async Task AssignQueue_PenaltyClampedToCooldownWhenNegative()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1, "owner");
        TestDb.User(ctx, 2, "ivan");
        TestDb.Topic(ctx, 1, 1000, _base, true);
        // прошло 10h, штраф 2h → остаток -8h → 0 → новый = 0 + cooldown = 6h
        TestDb.QueueElement(
            ctx, 2,
            lastAssignmentTime: QueueServiceFactory.DefaultNow.AddHours(-10),
            assignmentPenalty: TimeSpan.FromHours(2));
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        (Mock<ITelegramBotClient> mock, ITelegramBotClient client) = NewBot();
        QueueService service = NewService(ctx, client);

        await service.AssignQueue(TestContext.Current.CancellationToken);

        DbQueue q = await ctx.Queue.IgnoreQueryFilters()
            .SingleAsync(x => x.UserId == 2, TestContext.Current.CancellationToken);
        Assert.Equal(_cooldown, q.AssignmentPenalty);
    }

    [Fact]
    public async Task AssignQueue_SkipsElementThatLeftTopic()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1, "owner");
        TestDb.User(ctx, 10, "left");
        TestDb.User(ctx, 20, "other");
        // последним тему покинул пользователь 10; он же стоит первым в очереди
        DbTopic topic = TestDb.Topic(
            ctx, 1, 1000, _base, true, lastLeavedUserId: 10);
        TestDb.QueueElement(ctx, 10, lastAssignmentTime: _base, assignmentPenalty: TimeSpan.Zero);
        TestDb.QueueElement(ctx, 20, lastAssignmentTime: _base, assignmentPenalty: TimeSpan.FromHours(10));
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        (Mock<ITelegramBotClient> _, ITelegramBotClient client) = NewBot();
        QueueService service = NewService(ctx, client);

        await service.AssignQueue(TestContext.Current.CancellationToken);

        DbTopic reloaded = await ctx.Topics.SingleAsync(x => x.Id == topic.Id, TestContext.Current.CancellationToken);
        Assert.Equal(20, reloaded.AssigneeId);
    }

    [Fact]
    public async Task AssignQueue_OnlyQueueElementLeftTopic_Noop()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1, "owner");
        TestDb.User(ctx, 10, "left");
        // единственный элемент очереди — тот же, кто последним покидал тему
        DbTopic topic = TestDb.Topic(
            ctx, 1, 1000, _base, true, lastLeavedUserId: 10);
        TestDb.QueueElement(ctx, 10, lastAssignmentTime: _base, assignmentPenalty: TimeSpan.Zero);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        (Mock<ITelegramBotClient> mock, ITelegramBotClient client) = NewBot();
        QueueService service = NewService(ctx, client);

        await service.AssignQueue(TestContext.Current.CancellationToken); // не должно бросать

        DbTopic reloaded = await ctx.Topics.SingleAsync(x => x.Id == topic.Id, TestContext.Current.CancellationToken);
        Assert.True(reloaded.IsQueued);
        Assert.Null(reloaded.AssigneeId);

        DbQueue q = await ctx.Queue.IgnoreQueryFilters()
            .SingleAsync(x => x.UserId == 10, TestContext.Current.CancellationToken);
        // штраф/LastAssignmentTime не тронуты: до записи дело не дошло
        Assert.Equal(_base, q.LastAssignmentTime);
        Assert.Equal(TimeSpan.Zero, q.AssignmentPenalty);

        // ничего не отправлено и не отредактировано
        Assert.Equal(0, TestBotClient.Count(mock, typeof(SendMessageRequest)));
        Assert.Equal(0, TestBotClient.Count(mock, typeof(EditForumTopicRequest)));
    }

    [Fact]
    public async Task AssignQueue_AssignsAllQueuedTopicsToDistinctElements()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1, "owner");
        TestDb.User(ctx, 10, "a");
        TestDb.User(ctx, 20, "b");
        TestDb.Topic(ctx, 10, 1000, _base, true);
        TestDb.Topic(ctx, 20, 2000, _base.AddHours(1), true);
        TestDb.QueueElement(ctx, 10, lastAssignmentTime: _base, assignmentPenalty: TimeSpan.Zero);
        TestDb.QueueElement(ctx, 20, lastAssignmentTime: _base, assignmentPenalty: TimeSpan.Zero);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        (Mock<ITelegramBotClient> mock, ITelegramBotClient client) = NewBot();
        QueueService service = NewService(ctx, client);

        await service.AssignQueue(TestContext.Current.CancellationToken);

        List<DbTopic> topics = await ctx.Topics.ToListAsync(TestContext.Current.CancellationToken);
        Assert.All(topics, x => Assert.False(x.IsQueued));
        Assert.Equal(2, TestBotClient.Count(mock, typeof(SendMessageRequest)));
        // две темы, два разных назначенных
        Assert.Equal(2, topics.Select(x => x.AssigneeId).Distinct().Count());
    }

    [Fact]
    public async Task AssignQueue_TopicNotModified_Swallowed()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1, "owner");
        TestDb.User(ctx, 2, "ivan");
        DbTopic topic = TestDb.Topic(ctx, 1, 1000, _base, true);
        TestDb.QueueElement(ctx, 2);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        TestBotClient.WithEditForumTopicNotModified(mock);
        QueueService service = NewService(ctx, mock.Object);

        await service.AssignQueue(TestContext.Current.CancellationToken); // не должно бросать исключение

        DbTopic reloaded = await ctx.Topics.SingleAsync(x => x.Id == topic.Id, TestContext.Current.CancellationToken);
        Assert.False(reloaded.IsQueued);
        Assert.Equal(2, reloaded.AssigneeId);
    }

    [Fact]
    public async Task AssignQueue_UserWithoutUsername_FallsBackToMentionHtml()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1, "owner");
        TestDb.User(ctx, 2); // UserName отсутствует
        DbTopic topic = TestDb.Topic(ctx, 1, 1000, _base, true);
        TestDb.QueueElement(ctx, 2);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        // предусловие: ветка @username недоступна
        DbUser user = await ctx.Users.SingleAsync(x => x.Id == 2, TestContext.Current.CancellationToken);
        Assert.Null(user.UserName);

        (Mock<ITelegramBotClient> mock, ITelegramBotClient client) = NewBot();
        QueueService service = NewService(ctx, client);

        await service.AssignQueue(TestContext.Current.CancellationToken);

        SendMessageRequest msg = TestBotClient.LastSendMessage(mock) ?? throw new("SendMessage not called");
        Assert.Equal(QueueServiceFactory.FEEDBACK_CHAT_ID, msg.ChatId);
        Assert.Equal(1000, msg.MessageThreadId);
        Assert.Equal(ParseMode.Html, msg.ParseMode);
        // сработала ветка Tools.MentionHtml, а не "@username"
        Assert.Contains("tg://user?id=2", msg.Text);
        Assert.DoesNotContain("@null", msg.Text);
    }

    [Fact]
    public async Task AssignQueue_ConcurrentCalls_DoNotDoubleAssign()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1, "owner");
        TestDb.User(ctx, 10, "a");
        TestDb.User(ctx, 20, "b");
        TestDb.Topic(ctx, 10, 1000, _base, true);
        TestDb.Topic(ctx, 20, 2000, _base.AddHours(1), true);
        TestDb.QueueElement(ctx, 10, lastAssignmentTime: _base, assignmentPenalty: TimeSpan.Zero);
        TestDb.QueueElement(ctx, 20, lastAssignmentTime: _base, assignmentPenalty: TimeSpan.Zero);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        await ctx.DisposeAsync();

        (Mock<ITelegramBotClient> mock, ITelegramBotClient client) = NewBot();

        // Не NewContext(): он делает EnsureDeleted+EnsureCreated и стирает засеянные данные.
        // Создаём два независимых контекста поверх той же БД.
        DbContextOptions<FeedbackBotContext> options = new DbContextOptionsBuilder<FeedbackBotContext>()
            .UseNpgsql(db.ConnectionString).Options;
        await using FeedbackBotContext ctx1 = new(options);
        await using FeedbackBotContext ctx2 = new(options);

        QueueService s1 = NewService(ctx1, client);
        QueueService s2 = NewService(ctx2, client);

        await Task.WhenAll(
            s1.AssignQueue(TestContext.Current.CancellationToken),
            s2.AssignQueue(TestContext.Current.CancellationToken));

        List<DbTopic> topics = await ctx1.Topics.ToListAsync(TestContext.Current.CancellationToken);
        Assert.All(topics, x => Assert.False(x.IsQueued));
        Assert.Equal(2, TestBotClient.Count(mock, typeof(SendMessageRequest)));
        Assert.Equal(2, topics.Select(x => x.AssigneeId).Distinct().Count());
    }

    [Fact]
    public async Task AssignQueue_MoreTopicsThanElements_ElementGetsMultipleTopics()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1, "owner");
        TestDb.User(ctx, 10, "a");
        TestDb.User(ctx, 20, "b");
        TestDb.Topic(ctx, 10, 1000, _base, true);
        TestDb.Topic(ctx, 20, 2000, _base.AddHours(1), true);
        TestDb.Topic(ctx, 1, 3000, _base.AddHours(2), true);
        TestDb.QueueElement(ctx, 10, lastAssignmentTime: _base, assignmentPenalty: TimeSpan.Zero);
        TestDb.QueueElement(ctx, 20, lastAssignmentTime: _base, assignmentPenalty: TimeSpan.Zero);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        (Mock<ITelegramBotClient> mock, ITelegramBotClient client) = NewBot();
        QueueService service = NewService(ctx, client);

        await service.AssignQueue(TestContext.Current.CancellationToken);

        List<DbTopic> topics = await ctx.Topics.ToListAsync(TestContext.Current.CancellationToken);
        Assert.All(topics, x => Assert.False(x.IsQueued));
        Assert.Equal(3, TestBotClient.Count(mock, typeof(SendMessageRequest)));

        Dictionary<long?, int> distribution = topics
            .GroupBy(x => x.AssigneeId)
            .ToDictionary(g => g.Key, g => g.Count());

        // обе темы-из-очереди назначены, элемент не выбывает из очереди
        Assert.Equal(2, distribution.Count);
        Assert.Contains(2, distribution.Values);
        Assert.Contains(1, distribution.Values);
    }
}