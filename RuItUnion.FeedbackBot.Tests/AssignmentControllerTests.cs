using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using RuItUnion.FeedbackBot.Commands;
using RuItUnion.FeedbackBot.Data;
using RuItUnion.FeedbackBot.Data.Models;
using RuItUnion.FeedbackBot.Options;
using Telegram.Bot;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TgBotFrame.Middleware;

namespace RuItUnion.FeedbackBot.Tests;

[Collection(nameof(FeedbackBotDatabaseCollection))]
public class AssignmentControllerTests(TestDatabase db)
{
    private const long _chatId = QueueServiceFactory.FEEDBACK_CHAT_ID;
    private const long _adminId = 100L;
    private const int _threadId = 1000;
    private static readonly DateTime _base = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private FeedbackBotContext NewContext() => db.CreateContext();

    // ---------- helpers ----------

    private static AssignmentController NewController(
        FeedbackBotContext ctx,
        Mock<ITelegramBotClient>? mock = null,
        TimeProvider? clock = null)
    {
        mock ??= TestBotClient.Create();
        OptionsWrapper<AppOptions> options = new(new()
        {
            FeedbackChatId = _chatId,
            Start = string.Empty,
            LockDuration = TimeSpan.FromHours(1),
            CooldownDuration = TimeSpan.FromHours(6),
        });
        Mock<ILogger<AssignmentController>> logger = new(MockBehavior.Loose);
        return new(
            options,
            QueueServiceFactory.Create(ctx, mock.Object, clock: clock),
            ctx,
            clock ?? new TestTimeProvider(QueueServiceFactory.DefaultNow),
            new(ctx),
            mock.Object,
            logger.Object);
    }

    private static void Bind(
        AssignmentController controller,
        long? userId = _adminId,
        int? threadId = _threadId,
        int? messageId = 1)
    {
        FrameContext context = TestReflection.Context(
            _chatId, userId, threadId, messageId);
        Update update = new()
        {
            Message = new()
            {
                Id = messageId ?? 1,
                Chat = new() { Id = _chatId },
                MessageThreadId = threadId,
                From = new() { Id = userId ?? 0, IsBot = false, FirstName = "Test" },
            },
        };
        TestReflection.SetControllerProperties(controller, context, update);
    }

    private static string Prefix(string format) => format.Split('{')[0];

    // ================= Queue =================

    [Fact]
    public async Task Queue_Empty_SendsEmptyMessage()
    {
        FeedbackBotContext ctx = NewContext();
        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        AssignmentController controller = NewController(ctx, mock);
        Bind(controller);

        await controller.Queue();

        SendMessageRequest msg = TestBotClient.LastSendMessage(mock)
                                 ?? throw new InvalidOperationException("SendMessage not called");
        Assert.Equal(_chatId, msg.ChatId);
        Assert.Equal(_threadId, msg.MessageThreadId);
        Assert.Equal(ParseMode.Html, msg.ParseMode);
        Assert.Equal(TestResources.GetString("AssignmentController_QueueIsEmpty"), msg.Text);
    }

    [Fact]
    public async Task Queue_WithElement_ListsUser()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 10, "alice", "Алиса");
        TestDb.QueueElement(ctx, 10);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        AssignmentController controller = NewController(ctx, mock);
        Bind(controller);

        await controller.Queue();

        SendMessageRequest msg = TestBotClient.LastSendMessage(mock)!;
        Assert.Contains("tg://user?id=10", msg.Text);
        Assert.StartsWith("1.", msg.Text.TrimStart());
    }

    [Fact]
    public async Task Queue_Extended_IncludesPenaltyInfo()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 10);
        TestDb.QueueElement(ctx, 10,
            lastAssignmentTime: _base, assignmentPenalty: TimeSpan.FromHours(2));
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        AssignmentController controller = NewController(ctx, mock);
        Bind(controller);

        await controller.Queue(true);

        SendMessageRequest msg = TestBotClient.LastSendMessage(mock)!;
        Assert.Contains("02:00:00", msg.Text);
        Assert.Contains("<code>", msg.Text);
        Assert.Contains("</code>", msg.Text);
    }

    // ================= QueueStatus =================

    [Fact]
    public async Task QueueStatus_NotInQueue_SendsNotInQueue()
    {
        FeedbackBotContext ctx = NewContext();
        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        AssignmentController controller = NewController(ctx, mock);
        Bind(controller);

        await controller.QueueStatus();

        SendMessageRequest msg = TestBotClient.LastSendMessage(mock)!;
        Assert.Equal(TestResources.GetString("AssignmentController_NotInQueue"), msg.Text);
    }

    [Fact]
    public async Task QueueStatus_InQueue_SendsNumber()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, _adminId);
        TestDb.User(ctx, 11);
        TestDb.QueueElement(ctx, 11, lastAssignmentTime: _base, assignmentPenalty: TimeSpan.Zero);
        TestDb.QueueElement(ctx, _adminId, lastAssignmentTime: _base, assignmentPenalty: TimeSpan.FromHours(1));
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        AssignmentController controller = NewController(ctx, mock);
        Bind(controller);

        await controller.QueueStatus();

        SendMessageRequest msg = TestBotClient.LastSendMessage(mock)!;
        Assert.Contains("2", msg.Text);
    }

    // ================= Enqueue =================

    [Fact]
    public async Task Enqueue_NewUser_SendsOk()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, _adminId);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        AssignmentController controller = NewController(ctx, mock);
        Bind(controller);

        await controller.Enqueue();

        SendMessageRequest msg = TestBotClient.LastSendMessage(mock)!;
        Assert.Equal(TestResources.GetString("AssignmentController_EnqueueOk"), msg.Text);

        DbQueue q = await ctx.Queue.IgnoreQueryFilters()
            .SingleAsync(x => x.UserId == _adminId, TestContext.Current.CancellationToken);
        Assert.True(q.InQueue);
    }

    [Fact]
    public async Task Enqueue_AlreadyInQueue_SendsAlreadyInQueue()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, _adminId);
        TestDb.QueueElement(ctx, _adminId);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        AssignmentController controller = NewController(ctx, mock);
        Bind(controller);

        await controller.Enqueue();

        SendMessageRequest msg = TestBotClient.LastSendMessage(mock)!;
        Assert.Equal(TestResources.GetString("AssignmentController_EnqueueAlreadyInQueue"), msg.Text);
    }

    [Fact]
    public async Task Enqueue_Mentor_SendsMentor()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, _adminId);
        TestDb.User(ctx, 200);
        TestDb.QueueElement(ctx, 200);
        TestDb.Mentor(ctx, _adminId, 200);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        AssignmentController controller = NewController(ctx, mock);
        Bind(controller);

        await controller.Enqueue();

        SendMessageRequest msg = TestBotClient.LastSendMessage(mock)!;
        Assert.Equal(TestResources.GetString("AssignmentController_EnqueueMentor"), msg.Text);
    }

    // ================= Dequeue =================

    [Fact]
    public async Task Dequeue_InQueue_SendsOk()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, _adminId);
        TestDb.QueueElement(ctx, _adminId);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        AssignmentController controller = NewController(ctx, mock);
        Bind(controller);

        await controller.Dequeue();

        SendMessageRequest msg = TestBotClient.LastSendMessage(mock)!;
        Assert.Equal(TestResources.GetString("AssignmentController_DequeueOk"), msg.Text);

        int active = await ctx.Queue.IgnoreQueryFilters()
            .CountAsync(x => x.UserId == _adminId && x.InQueue, TestContext.Current.CancellationToken);
        Assert.Equal(0, active);
    }

    [Fact]
    public async Task Dequeue_NotInQueue_SendsNotModified()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, _adminId);
        TestDb.QueueElement(ctx, _adminId, false);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        AssignmentController controller = NewController(ctx, mock);
        Bind(controller);

        await controller.Dequeue();

        SendMessageRequest msg = TestBotClient.LastSendMessage(mock)!;
        Assert.Equal(TestResources.GetString("AssignmentController_DequeueNotModified"), msg.Text);
    }

    // ================= Abandon =================

    [Fact]
    public async Task Abandon_TopicNotFound_SendsNotFound()
    {
        FeedbackBotContext ctx = NewContext();
        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        AssignmentController controller = NewController(ctx, mock);
        Bind(controller);

        await controller.Abandon(9999);

        SendMessageRequest msg = TestBotClient.LastSendMessage(mock)!;
        Assert.Equal(
            TestResources.GetString("InternalThreadController_Close_TopicNotFound"),
            msg.Text);
    }

    [Fact]
    public async Task Abandon_UserIsNotAssignee_Noop()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1);
        TestDb.User(ctx, _adminId);
        TestDb.Topic(ctx, 1, _threadId, _base, false, 1);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        AssignmentController controller = NewController(ctx, mock);
        Bind(controller);

        await controller.Abandon(_threadId);

        SendMessageRequest msg = TestBotClient.LastSendMessage(mock)!;
        Assert.Equal(
            TestResources.GetString("AssignmentController_AbandonForbidden"),
            msg.Text);
    }

    [Fact]
    public async Task Abandon_Success_QueuesTopicAndAddsPenalty()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, _adminId);
        DbTopic topic = TestDb.Topic(ctx, _adminId, _threadId, _base,
            false, _adminId);
        TestDb.QueueElement(ctx, _adminId, assignmentPenalty: TimeSpan.FromDays(1));
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        TestBotClient.WithEditForumTopic(mock);
        AssignmentController controller = NewController(ctx, mock);
        Bind(controller);

        await controller.Abandon(_threadId);

        SendMessageRequest msg = TestBotClient.LastSendMessage(mock)!;
        Assert.Equal(TestResources.GetString("AssignmentController_AbandonSuccess"), msg.Text);

        DbTopic reloaded = await ctx.Topics.SingleAsync(x => x.Id == topic.Id, TestContext.Current.CancellationToken);
        Assert.True(reloaded.IsQueued);
        Assert.Null(reloaded.AssigneeId);
        Assert.Equal(_adminId, reloaded.LastLeavedUserId);
        Assert.Equal(DateTime.MinValue, reloaded.LockedFrom);

        DbQueue q = await ctx.Queue.IgnoreQueryFilters()
            .SingleAsync(x => x.UserId == _adminId, TestContext.Current.CancellationToken);
        Assert.Equal(TimeSpan.FromDays(2), q.AssignmentPenalty);
    }

    [Fact]
    public async Task Abandon_UserIsNovice_SendsAbandonNovice()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, _adminId);
        TestDb.User(ctx, 300);
        TestDb.Mentor(ctx, 300, _adminId); // 300 — ментор, AdminId — новичок
        DbTopic topic = TestDb.Topic(ctx, _adminId, _threadId, _base,
            false, _adminId);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        AssignmentController controller = NewController(ctx, mock);
        Bind(controller);

        await controller.Abandon(_threadId);

        SendMessageRequest msg = TestBotClient.LastSendMessage(mock)!;
        Assert.Equal(TestResources.GetString("AssignmentController_AbandonNovice"), msg.Text);

        DbTopic reloaded = await ctx.Topics.SingleAsync(x => x.Id == topic.Id, TestContext.Current.CancellationToken);
        Assert.Equal(_adminId, reloaded.AssigneeId);
    }

    // ================= Assignee =================

    [Fact]
    public async Task Assignee_TopicNotFound_SendsNotFound()
    {
        FeedbackBotContext ctx = NewContext();
        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        AssignmentController controller = NewController(ctx, mock);
        Bind(controller);

        await controller.Assignee(9999);

        SendMessageRequest msg = TestBotClient.LastSendMessage(mock)!;
        Assert.Equal(
            TestResources.GetString("InternalThreadController_Close_TopicNotFound"),
            msg.Text);
    }

    [Fact]
    public async Task Assignee_NoAssignee_SendsNone()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1);
        TestDb.Topic(ctx, 1, _threadId, _base);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        AssignmentController controller = NewController(ctx, mock);
        Bind(controller);

        await controller.Assignee(_threadId);

        SendMessageRequest msg = TestBotClient.LastSendMessage(mock)!;
        Assert.Equal(TestResources.GetString("AssignmentController_AssigneeNone"), msg.Text);
    }

    [Fact]
    public async Task Assignee_WithAssignee_SendsOk()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1, firstName: "Иван");
        TestDb.User(ctx, _adminId);
        TestDb.Topic(ctx, 1, _threadId, _base, assigneeId: _adminId);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        AssignmentController controller = NewController(ctx, mock);
        Bind(controller);

        await controller.Assignee(_threadId);

        SendMessageRequest msg = TestBotClient.LastSendMessage(mock)!;
        Assert.StartsWith(Prefix(TestResources.GetString("AssignmentController_AssigneeOk")), msg.Text);
        Assert.Contains("tg://user?id=" + _adminId, msg.Text);
    }

    // ================= Intercept =================

    [Fact]
    public async Task Intercept_TopicNotFound_SendsInvalid()
    {
        FeedbackBotContext ctx = NewContext();
        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        AssignmentController controller = NewController(ctx, mock);
        Bind(controller);

        await controller.Intercept();

        SendMessageRequest msg = TestBotClient.LastSendMessage(mock)!;
        Assert.Equal(TestResources.GetString("AssignmentController_InterceptInvalid"), msg.Text);
    }

    [Fact]
    public async Task Intercept_WrongChat_SendsInvalid()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1);
        TestDb.Topic(ctx, 1, _threadId, _base);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        AssignmentController controller = NewController(ctx, mock);
        TestReflection.SetControllerProperties(controller, TestReflection.Context(_chatId, _adminId, _threadId, 1), new()
        {
            Message = new()
            {
                Id = 1,
                Chat = new() { Id = -999 }, // wrong chat
                MessageThreadId = _threadId,
            },
        }, TestContext.Current.CancellationToken);

        await controller.Intercept();

        SendMessageRequest msg = TestBotClient.LastSendMessage(mock)!;
        Assert.Equal(TestResources.GetString("AssignmentController_InterceptInvalid"), msg.Text);
    }

    [Fact]
    public async Task Intercept_QueuedNoAssignee_SendsQueue()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1);
        TestDb.Topic(ctx, 1, _threadId, _base, true);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        AssignmentController controller = NewController(ctx, mock);
        Bind(controller);

        await controller.Intercept();

        SendMessageRequest msg = TestBotClient.LastSendMessage(mock)!;
        Assert.Equal(TestResources.GetString("AssignmentController_InterceptQueue"), msg.Text);
    }

    [Fact]
    public async Task Intercept_NotQueuedNoAssignee_Intercepts()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1);
        TestDb.User(ctx, _adminId, "admin");
        DbTopic topic = TestDb.Topic(ctx, 1, _threadId, _base);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        TestBotClient.WithEditForumTopic(mock);
        AssignmentController controller = NewController(ctx, mock);
        Bind(controller);

        await controller.Intercept();

        DbTopic reloaded = await ctx.Topics.SingleAsync(x => x.Id == topic.Id, TestContext.Current.CancellationToken);
        Assert.Equal(_adminId, reloaded.AssigneeId);
        Assert.False(reloaded.IsQueued);
        Assert.Equal(1, TestBotClient.Count(mock, typeof(SendMessageRequest)));
    }

    [Fact]
    public async Task Intercept_HasAssigneeAndCandidate_SendsConflict()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1);
        TestDb.User(ctx, 2);
        TestDb.User(ctx, _adminId);
        TestDb.Topic(ctx, 1, _threadId, _base, false,
            2, 2, 100);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        AssignmentController controller = NewController(ctx, mock);
        Bind(controller);

        await controller.Intercept();

        SendMessageRequest msg = TestBotClient.LastSendMessage(mock)!;
        Assert.Equal(TestResources.GetString("AssignmentController_InterceptConflict"), msg.Text);
    }

    [Fact]
    public async Task Intercept_Self_SendsSelf()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1);
        TestDb.User(ctx, _adminId);
        TestDb.Topic(ctx, 1, _threadId, _base, false, _adminId);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        AssignmentController controller = NewController(ctx, mock);
        Bind(controller);

        await controller.Intercept();

        SendMessageRequest msg = TestBotClient.LastSendMessage(mock)!;
        Assert.Equal(TestResources.GetString("AssignmentController_InterceptSelf"), msg.Text);
    }

    [Fact]
    public async Task Intercept_Start_SetsCandidateAndMessageId()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1);
        TestDb.User(ctx, _adminId);
        DbTopic topic = TestDb.Topic(ctx, 1, _threadId, _base, false,
            1);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock, 42);
        AssignmentController controller = NewController(ctx, mock);
        Bind(controller);

        await controller.Intercept();

        DbTopic reloaded = await ctx.Topics.SingleAsync(x => x.Id == topic.Id, TestContext.Current.CancellationToken);
        Assert.Equal(_adminId, reloaded.InterceptCandidateId);
        Assert.Equal(42, reloaded.InterceptMessageId);
        Assert.Empty(reloaded.InterceptVotes);
        Assert.Equal(1, reloaded.AssigneeId); // исполнитель не меняется

        SendMessageRequest msg = TestBotClient.LastSendMessage(mock)!;
        Assert.StartsWith(
            Prefix(TestResources.GetString("AssignmentController_InterceptStart")),
            msg.Text);
    }

    [Fact]
    public async Task Intercept_UserNotFound_SendsUserNotFound()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1);
        // AdminId отсутствует в БД
        TestDb.Topic(ctx, 1, _threadId, _base, false, 1);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        AssignmentController controller = NewController(ctx, mock);
        Bind(controller);

        await controller.Intercept();

        SendMessageRequest msg = TestBotClient.LastSendMessage(mock)!;
        Assert.Equal(TestResources.GetString("AssignmentController_UserNotFound"), msg.Text);
    }

    // ================= SetMentor =================

    [Fact]
    public async Task SetMentor_InvalidId_SendsInvalidReply()
    {
        FeedbackBotContext ctx = NewContext();
        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        AssignmentController controller = NewController(ctx, mock);
        Bind(controller);

        await controller.SetMentor(-1, -1);

        SendMessageRequest msg = TestBotClient.LastSendMessage(mock)!;
        Assert.Equal(TestResources.GetString("AssignmentController_SetMentorInvalidReply"), msg.Text);
    }

    [Fact]
    public async Task SetMentor_Self_SendsSelf()
    {
        FeedbackBotContext ctx = NewContext();
        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        AssignmentController controller = NewController(ctx, mock);
        Bind(controller);

        await controller.SetMentor(_adminId, _adminId);

        SendMessageRequest msg = TestBotClient.LastSendMessage(mock)!;
        Assert.Equal(TestResources.GetString("AssignmentController_SetMentorSelf"), msg.Text);
    }

    [Fact]
    public async Task SetMentor_AlreadyActive_SendsConflict()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1);
        TestDb.User(ctx, 2);
        TestDb.Mentor(ctx, 1, 2);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        AssignmentController controller = NewController(ctx, mock);
        Bind(controller);

        await controller.SetMentor(1, 2);

        SendMessageRequest msg = TestBotClient.LastSendMessage(mock)!;
        Assert.Equal(TestResources.GetString("AssignmentController_SetMentorConflict"), msg.Text);
    }

    [Fact]
    public async Task SetMentor_UserNotFound_SendsNotFound()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        AssignmentController controller = NewController(ctx, mock);
        Bind(controller);

        await controller.SetMentor(1, 999);

        SendMessageRequest msg = TestBotClient.LastSendMessage(mock)!;
        Assert.Equal(
            TestResources.GetString("AssignmentController_MentorOrNoviceNotFound"),
            msg.Text);
    }

    [Fact]
    public async Task SetMentor_New_CreatesRelationship()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1);
        TestDb.User(ctx, 2);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        AssignmentController controller = NewController(ctx, mock);
        Bind(controller);

        await controller.SetMentor(1, 2);

        DbMentor mentor = await ctx.Mentors.SingleAsync(x => x.MentorId == 1 && x.NoviceId == 2,
            TestContext.Current.CancellationToken);
        Assert.True(mentor.IsActive);

        SendMessageRequest msg = TestBotClient.LastSendMessage(mock)!;
        Assert.StartsWith(
            Prefix(TestResources.GetString("AssignmentController_SetMentorSuccess")),
            msg.Text);
    }

    [Fact]
    public async Task SetMentor_Inactive_Reactivates()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1);
        TestDb.User(ctx, 2);
        DbMentor existing = TestDb.Mentor(ctx, 1, 2, false);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        AssignmentController controller = NewController(ctx, mock);
        Bind(controller);

        await controller.SetMentor(1, 2);

        DbMentor reloaded = await ctx.Mentors.SingleAsync(
            x => x.MentorId == existing.MentorId && x.NoviceId == existing.NoviceId,
            TestContext.Current.CancellationToken);
        Assert.True(reloaded.IsActive);
    }

    // ================= FreeMentor =================

    [Fact]
    public async Task FreeMentor_NotFound_SendsNotFound()
    {
        FeedbackBotContext ctx = NewContext();
        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        AssignmentController controller = NewController(ctx, mock);
        Bind(controller);

        await controller.FreeMentor(1, 2);

        SendMessageRequest msg = TestBotClient.LastSendMessage(mock)!;
        Assert.Equal(TestResources.GetString("AssignmentController_MentorNotFound"), msg.Text);
    }

    [Fact]
    public async Task FreeMentor_Success_Deactivates()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1);
        TestDb.User(ctx, 2);
        DbMentor mentor = TestDb.Mentor(ctx, 1, 2);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        AssignmentController controller = NewController(ctx, mock);
        Bind(controller);

        await controller.FreeMentor(1, 2);

        DbMentor reloaded = await ctx.Mentors.IgnoreQueryFilters().SingleAsync(
            x => x.MentorId == mentor.MentorId && x.NoviceId == mentor.NoviceId,
            TestContext.Current.CancellationToken);
        Assert.False(reloaded.IsActive);

        SendMessageRequest msg = TestBotClient.LastSendMessage(mock)!;
        Assert.StartsWith(
            Prefix(TestResources.GetString("AssignmentController_FreeMentorSuccess")),
            msg.Text);
    }
}