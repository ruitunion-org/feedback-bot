using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using RuItUnion.FeedbackBot.Commands;
using RuItUnion.FeedbackBot.Data;
using RuItUnion.FeedbackBot.Data.Models;
using RuItUnion.FeedbackBot.Options;
using RuItUnion.FeedbackBot.Services;
using Telegram.Bot;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TgBotFrame.Commands.Authorization.Models;
using TgBotFrame.Middleware;

namespace RuItUnion.FeedbackBot.Tests;

[Collection(nameof(FeedbackBotDatabaseCollection))]
public class AssignmentControllerTests(TestDatabase db)
{
    private static readonly DateTime Base = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private const long ChatId = QueueServiceFactory.FEEDBACK_CHAT_ID;
    private const long AdminId = 100L;
    private const int ThreadId = 1000;

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
            FeedbackChatId = ChatId,
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
            new TopicTitleGenerator(ctx),
            mock.Object,
            logger.Object);
    }

    private static void Bind(
        AssignmentController controller,
        long? userId = AdminId,
        int? threadId = ThreadId,
        int? messageId = 1)
    {
        FrameContext context = TestReflection.Context(
            chatId: ChatId, userId: userId, threadId: threadId, messageId: messageId);
        Update update = new()
        {
            Message = new()
            {
                Id = messageId ?? 1,
                Chat = new() { Id = ChatId },
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
        Assert.Equal(ChatId, msg.ChatId);
        Assert.Equal(ThreadId, msg.MessageThreadId);
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
            lastAssignmentTime: Base, assignmentPenalty: TimeSpan.FromHours(2));
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
        TestDb.User(ctx, AdminId);
        TestDb.User(ctx, 11);
        TestDb.QueueElement(ctx, 11, lastAssignmentTime: Base, assignmentPenalty: TimeSpan.Zero);
        TestDb.QueueElement(ctx, AdminId, lastAssignmentTime: Base, assignmentPenalty: TimeSpan.FromHours(1));
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
        TestDb.User(ctx, AdminId);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        AssignmentController controller = NewController(ctx, mock);
        Bind(controller);

        await controller.Enqueue();

        SendMessageRequest msg = TestBotClient.LastSendMessage(mock)!;
        Assert.Equal(TestResources.GetString("AssignmentController_EnqueueOk"), msg.Text);

        DbQueue q = await ctx.Queue.IgnoreQueryFilters()
            .SingleAsync(x => x.UserId == AdminId, TestContext.Current.CancellationToken);
        Assert.True(q.InQueue);
    }

    [Fact]
    public async Task Enqueue_AlreadyInQueue_SendsAlreadyInQueue()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, AdminId);
        TestDb.QueueElement(ctx, AdminId);
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
        TestDb.User(ctx, AdminId);
        TestDb.User(ctx, 200);
        TestDb.QueueElement(ctx, 200);
        TestDb.Mentor(ctx, AdminId, 200);
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
        TestDb.User(ctx, AdminId);
        TestDb.QueueElement(ctx, AdminId);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        AssignmentController controller = NewController(ctx, mock);
        Bind(controller);

        await controller.Dequeue();

        SendMessageRequest msg = TestBotClient.LastSendMessage(mock)!;
        Assert.Equal(TestResources.GetString("AssignmentController_DequeueOk"), msg.Text);

        int active = await ctx.Queue.IgnoreQueryFilters()
            .CountAsync(x => x.UserId == AdminId && x.InQueue, TestContext.Current.CancellationToken);
        Assert.Equal(0, active);
    }

    [Fact]
    public async Task Dequeue_NotInQueue_SendsNotModified()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, AdminId);
        TestDb.QueueElement(ctx, AdminId, inQueue: false);
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
        TestDb.User(ctx, AdminId);
        TestDb.Topic(ctx, 1, ThreadId, Base, isQueued: false, assigneeId: 1);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        AssignmentController controller = NewController(ctx, mock);
        Bind(controller);

        await controller.Abandon(ThreadId);

        SendMessageRequest msg = TestBotClient.LastSendMessage(mock)!;
        Assert.Equal(
            TestResources.GetString("AssignmentController_AbandonForbidden"),
            msg.Text);
    }

    [Fact]
    public async Task Abandon_Success_QueuesTopicAndAddsPenalty()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, AdminId);
        DbTopic topic = TestDb.Topic(ctx, AdminId, ThreadId, Base,
            isQueued: false, assigneeId: AdminId);
        TestDb.QueueElement(ctx, AdminId, assignmentPenalty: TimeSpan.FromDays(1));
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        TestBotClient.WithEditForumTopic(mock);
        AssignmentController controller = NewController(ctx, mock);
        Bind(controller);

        await controller.Abandon(ThreadId);

        SendMessageRequest msg = TestBotClient.LastSendMessage(mock)!;
        Assert.Equal(TestResources.GetString("AssignmentController_AbandonSuccess"), msg.Text);

        DbTopic reloaded = await ctx.Topics.SingleAsync(x => x.Id == topic.Id, TestContext.Current.CancellationToken);
        Assert.True(reloaded.IsQueued);
        Assert.Null(reloaded.AssigneeId);
        Assert.Equal(AdminId, reloaded.LastLeavedUserId);
        Assert.Equal(DateTime.MinValue, reloaded.LockedFrom);

        DbQueue q = await ctx.Queue.IgnoreQueryFilters()
            .SingleAsync(x => x.UserId == AdminId, TestContext.Current.CancellationToken);
        Assert.Equal(TimeSpan.FromDays(2), q.AssignmentPenalty);
    }

    [Fact]
    public async Task Abandon_UserIsNovice_SendsAbandonNovice()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, AdminId);
        TestDb.User(ctx, 300);
        TestDb.Mentor(ctx, 300, AdminId); // 300 — ментор, AdminId — новичок
        DbTopic topic = TestDb.Topic(ctx, AdminId, ThreadId, Base,
            isQueued: false, assigneeId: AdminId);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        AssignmentController controller = NewController(ctx, mock);
        Bind(controller);

        await controller.Abandon(ThreadId);

        SendMessageRequest msg = TestBotClient.LastSendMessage(mock)!;
        Assert.Equal(TestResources.GetString("AssignmentController_AbandonNovice"), msg.Text);

        DbTopic reloaded = await ctx.Topics.SingleAsync(x => x.Id == topic.Id, TestContext.Current.CancellationToken);
        Assert.Equal(AdminId, reloaded.AssigneeId);
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
        TestDb.Topic(ctx, 1, ThreadId, Base);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        AssignmentController controller = NewController(ctx, mock);
        Bind(controller);

        await controller.Assignee(ThreadId);

        SendMessageRequest msg = TestBotClient.LastSendMessage(mock)!;
        Assert.Equal(TestResources.GetString("AssignmentController_AssigneeNone"), msg.Text);
    }

    [Fact]
    public async Task Assignee_WithAssignee_SendsOk()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1, firstName: "Иван");
        TestDb.User(ctx, AdminId);
        TestDb.Topic(ctx, 1, ThreadId, Base, assigneeId: AdminId);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        AssignmentController controller = NewController(ctx, mock);
        Bind(controller);

        await controller.Assignee(ThreadId);

        SendMessageRequest msg = TestBotClient.LastSendMessage(mock)!;
        Assert.StartsWith(Prefix(TestResources.GetString("AssignmentController_AssigneeOk")), msg.Text);
        Assert.Contains("tg://user?id=" + AdminId, msg.Text);
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
        TestDb.Topic(ctx, 1, ThreadId, Base);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        AssignmentController controller = NewController(ctx, mock);
        TestReflection.SetControllerProperties(controller, TestReflection.Context(chatId: ChatId, userId: AdminId, threadId: ThreadId, messageId: 1), new Update
            {
                Message = new()
                {
                    Id = 1,
                    Chat = new() { Id = -999 }, // wrong chat
                    MessageThreadId = ThreadId,
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
        TestDb.Topic(ctx, 1, ThreadId, Base, isQueued: true);
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
        TestDb.User(ctx, AdminId, userName: "admin");
        DbTopic topic = TestDb.Topic(ctx, 1, ThreadId, Base, isQueued: false);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        TestBotClient.WithEditForumTopic(mock);
        AssignmentController controller = NewController(ctx, mock);
        Bind(controller);

        await controller.Intercept();

        DbTopic reloaded = await ctx.Topics.SingleAsync(x => x.Id == topic.Id, TestContext.Current.CancellationToken);
        Assert.Equal(AdminId, reloaded.AssigneeId);
        Assert.False(reloaded.IsQueued);
        Assert.Equal(1, TestBotClient.Count(mock, typeof(SendMessageRequest)));
    }

    [Fact]
    public async Task Intercept_HasAssigneeAndCandidate_SendsConflict()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1);
        TestDb.User(ctx, 2);
        TestDb.User(ctx, AdminId);
        TestDb.Topic(ctx, 1, ThreadId, Base, isQueued: false,
            assigneeId: 2, interceptCandidateId: 2, interceptMessageId: 100);
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
        TestDb.User(ctx, AdminId);
        TestDb.Topic(ctx, 1, ThreadId, Base, isQueued: false, assigneeId: AdminId);
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
        TestDb.User(ctx, AdminId);
        DbTopic topic = TestDb.Topic(ctx, 1, ThreadId, Base, isQueued: false,
            assigneeId: 1);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock, messageId: 42);
        AssignmentController controller = NewController(ctx, mock);
        Bind(controller);

        await controller.Intercept();

        DbTopic reloaded = await ctx.Topics.SingleAsync(x => x.Id == topic.Id, TestContext.Current.CancellationToken);
        Assert.Equal(AdminId, reloaded.InterceptCandidateId);
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
        TestDb.Topic(ctx, 1, ThreadId, Base, isQueued: false, assigneeId: 1);
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

        await controller.SetMentor(AdminId, AdminId);

        SendMessageRequest msg = TestBotClient.LastSendMessage(mock)!;
        Assert.Equal(TestResources.GetString("AssignmentController_SetMentorSelf"), msg.Text);
    }

    [Fact]
    public async Task SetMentor_AlreadyActive_SendsConflict()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1);
        TestDb.User(ctx, 2);
        TestDb.Mentor(ctx, 1, 2, isActive: true);
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
        DbMentor existing = TestDb.Mentor(ctx, 1, 2, isActive: false);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        AssignmentController controller = NewController(ctx, mock);
        Bind(controller);

        await controller.SetMentor(1, 2);

        DbMentor reloaded = await ctx.Mentors.SingleAsync(x => x.MentorId == existing.MentorId && x.NoviceId == existing.NoviceId,
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
        DbMentor mentor = TestDb.Mentor(ctx, 1, 2, isActive: true);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        AssignmentController controller = NewController(ctx, mock);
        Bind(controller);

        await controller.FreeMentor(1, 2);

        DbMentor reloaded = await ctx.Mentors.IgnoreQueryFilters().SingleAsync(x => x.MentorId == mentor.MentorId && x.NoviceId == mentor.NoviceId,
            TestContext.Current.CancellationToken);
        Assert.False(reloaded.IsActive);

        SendMessageRequest msg = TestBotClient.LastSendMessage(mock)!;
        Assert.StartsWith(
            Prefix(TestResources.GetString("AssignmentController_FreeMentorSuccess")),
            msg.Text);
    }
}