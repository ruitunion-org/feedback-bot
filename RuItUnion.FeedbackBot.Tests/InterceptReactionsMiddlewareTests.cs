using Microsoft.EntityFrameworkCore;
using Moq;
using RuItUnion.FeedbackBot.Data;
using RuItUnion.FeedbackBot.Data.Models;
using RuItUnion.FeedbackBot.Middlewares;
using RuItUnion.FeedbackBot.Properties;
using Telegram.Bot;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TgBotFrame.Middleware;

namespace RuItUnion.FeedbackBot.Tests;

[Collection(nameof(FeedbackBotDatabaseCollection))]
public class InterceptReactionsMiddlewareTests(TestDatabase db)
{
    private static readonly DateTime Base = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private const long ChatId = QueueServiceFactory.FEEDBACK_CHAT_ID;

    private FeedbackBotContext NewContext() => db.CreateContext();

    // ---------- helpers ----------

    private static ReactionTypeEmoji Like() => new() { Emoji = "👍" };
    private static ReactionTypeEmoji Heart() => new() { Emoji = "❤" };

    private static Update ReactionUpdate(
        long chatId, int messageId, long? userId,
        ReactionType[]? oldReaction = null, ReactionType[]? newReaction = null)
    {
        return new Update
        {
            MessageReaction = new MessageReactionUpdated
            {
                Chat = new() { Id = chatId },
                MessageId = messageId,
                User = userId is null
                    ? null
                    : new() { Id = userId.Value, IsBot = false, FirstName = "Voter" },
                Date = QueueServiceFactory.DefaultNow,
                OldReaction = oldReaction ?? [],
                NewReaction = newReaction ?? [],
            },
        };
    }

    private static (Mock<ITelegramBotClient> mock, ITelegramBotClient client, InterceptReactionsMiddleware mv, Func<int> nextCalls) NewClient(
        FeedbackBotContext ctx,
        Func<ITelegramBotClient>? _ = null)
    {
        Mock<ITelegramBotClient> mock = TestBotClient.Create();
        TestBotClient.WithSendMessage(mock);
        int calls = 0;
        InterceptReactionsMiddleware mw = InterceptReactionsMiddlewareFactory.Create(
            ctx, mock.Object,
            next: (_, _, _) => { Interlocked.Increment(ref calls); return Task.CompletedTask; });
        return (mock, mock.Object, mw, () => calls);
    }

    // ================= gate / passthrough =================

    [Fact]
    public async Task NonReactionUpdate_PassesThrough()
    {
        FeedbackBotContext ctx = NewContext();
        int nextCalls = 0;
        InterceptReactionsMiddleware mw = InterceptReactionsMiddlewareFactory.Create(
            ctx, TestBotClient.Create().Object,
            next: (_, _, _) => { nextCalls++; return Task.CompletedTask; });

        Update update = new()
        {
            Message = new() { Id = 1, Chat = new() { Id = ChatId } },
        };
        await mw.InvokeAsync(update, TestReflection.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(1, nextCalls);
    }

    [Fact]
    public async Task WrongChatId_IgnoresAndPassesThrough()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1);
        TestDb.User(ctx, 2);
        DbTopic topic = TestDb.Topic(ctx, 1, 1000, Base, true,
            interceptCandidateId: 2, interceptMessageId: 500);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        (Mock<ITelegramBotClient> mock, ITelegramBotClient client, InterceptReactionsMiddleware mw, Func<int> nextCalls) = NewClient(ctx);

        await mw.InvokeAsync(
            ReactionUpdate(chatId: -999, messageId: 500, userId: 3, newReaction: [Like()]),
            TestReflection.Context(), TestContext.Current.CancellationToken);

        DbTopic reloaded = await ctx.Topics.SingleAsync(x => x.Id == topic.Id,
            TestContext.Current.CancellationToken);
        Assert.Empty(reloaded.InterceptVotes);
        Assert.Equal(1, nextCalls());
        Assert.Equal(0, TestBotClient.Count(mock, typeof(SendMessageRequest)));
    }

    [Fact]
    public async Task NullUser_IgnoresAndPassesThrough()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1);
        TestDb.User(ctx, 2);
        DbTopic topic = TestDb.Topic(ctx, 1, 1000, Base, true,
            interceptCandidateId: 2, interceptMessageId: 500);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        (Mock<ITelegramBotClient> mock, ITelegramBotClient client, InterceptReactionsMiddleware mw, Func<int> nextCalls) = NewClient(ctx);

        await mw.InvokeAsync(
            ReactionUpdate(chatId: ChatId, messageId: 500, userId: null, newReaction: [Like()]),
            TestReflection.Context(), TestContext.Current.CancellationToken);

        DbTopic reloaded = await ctx.Topics.SingleAsync(x => x.Id == topic.Id,
            TestContext.Current.CancellationToken);
        Assert.Empty(reloaded.InterceptVotes);
        Assert.Equal(1, nextCalls());
        Assert.Equal(0, TestBotClient.Count(mock, typeof(SendMessageRequest)));
    }

    [Fact]
    public async Task UnknownInterceptMessage_Ignores()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1);
        TestDb.User(ctx, 2);
        DbTopic topic = TestDb.Topic(ctx, 1, 1000, Base, true,
            interceptCandidateId: 2, interceptMessageId: 500);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        (Mock<ITelegramBotClient> mock, ITelegramBotClient client, InterceptReactionsMiddleware mw, Func<int> nextCalls) = NewClient(ctx);

        await mw.InvokeAsync(
            ReactionUpdate(chatId: ChatId, messageId: 999, userId: 3, newReaction: [Like()]),
            TestReflection.Context(), TestContext.Current.CancellationToken);

        DbTopic reloaded = await ctx.Topics.SingleAsync(x => x.Id == topic.Id,
            TestContext.Current.CancellationToken);
        Assert.Empty(reloaded.InterceptVotes);
        Assert.Equal(1, nextCalls());
        Assert.Equal(0, TestBotClient.Count(mock, typeof(SendMessageRequest)));
    }

    [Fact]
    public async Task CandidateVotingForSelf_Ignores()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1, "owner");
        TestDb.User(ctx, 2, "candidate");
        DbTopic topic = TestDb.Topic(ctx, 1, 1000, Base, true,
            assigneeId: 1, interceptCandidateId: 2, interceptMessageId: 500);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        (Mock<ITelegramBotClient> mock, ITelegramBotClient client, InterceptReactionsMiddleware mw, Func<int> nextCalls) = NewClient(ctx);

        // Кандидат 2 голосует за себя
        await mw.InvokeAsync(
            ReactionUpdate(chatId: ChatId, messageId: 500, userId: 2, newReaction: [Like()]),
            TestReflection.Context(), TestContext.Current.CancellationToken);

        DbTopic reloaded = await ctx.Topics.SingleAsync(x => x.Id == topic.Id,
            TestContext.Current.CancellationToken);
        Assert.Empty(reloaded.InterceptVotes);
        Assert.Equal(1, reloaded.AssigneeId);
        Assert.Equal(1, nextCalls());
        Assert.Equal(0, TestBotClient.Count(mock, typeof(SendMessageRequest)));
    }

    // ================= add-vote: below threshold =================

    [Fact]
    public async Task FirstVote_LockNotExpired_OnlyRecordsVote()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1, "owner");
        TestDb.User(ctx, 2, "candidate");
        TestDb.User(ctx, 3, "voter");
        // LockDuration = 1h; LockedFrom = now - 30min → не истёк
        DbTopic topic = TestDb.Topic(ctx, 1, 1000, Base, true,
            assigneeId: 1, interceptCandidateId: 2, interceptMessageId: 500,
            lockedFrom: QueueServiceFactory.DefaultNow.AddMinutes(-30));
        TestDb.QueueElement(ctx, 1);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        (Mock<ITelegramBotClient> mock, ITelegramBotClient client, InterceptReactionsMiddleware mw, Func<int> nextCalls) = NewClient(ctx);

        await mw.InvokeAsync(
            ReactionUpdate(chatId: ChatId, messageId: 500, userId: 3, newReaction: [Like()]),
            TestReflection.Context(), TestContext.Current.CancellationToken);

        DbTopic reloaded = await ctx.Topics.SingleAsync(x => x.Id == topic.Id,
            TestContext.Current.CancellationToken);
        Assert.Single(reloaded.InterceptVotes);
        Assert.Contains(3, reloaded.InterceptVotes);
        Assert.Equal(1, reloaded.AssigneeId);
        Assert.True(reloaded.IsQueued);

        DbQueue q = await ctx.Queue.SingleAsync(x => x.UserId == 1, TestContext.Current.CancellationToken);
        Assert.Equal(TimeSpan.Zero, q.AssignmentPenalty);

        Assert.Equal(0, TestBotClient.Count(mock, typeof(SendMessageRequest)));
        Assert.Equal(1, nextCalls());
    }

    // ================= add-vote: threshold by count =================

    [Fact]
    public async Task SecondVote_TriggersSwap()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1, "owner", firstName: "Пётр", lastName: "Петров");
        TestDb.User(ctx, 2, "candidate", firstName: "Иван", lastName: "Иванов");
        TestDb.User(ctx, 3, "voter1");
        TestDb.User(ctx, 4, "voter2");
        DbTopic topic = TestDb.Topic(ctx, 1, 1000, Base, true,
            assigneeId: 1, interceptCandidateId: 2, interceptMessageId: 500,
            lockedFrom: QueueServiceFactory.DefaultNow.AddMinutes(-30));
        topic.InterceptVotes.Add(3); // один голос уже есть
        TestDb.QueueElement(ctx, 1);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        (Mock<ITelegramBotClient> mock, ITelegramBotClient client, InterceptReactionsMiddleware mw, Func<int> nextCalls) = NewClient(ctx);

        await mw.InvokeAsync(
            ReactionUpdate(chatId: ChatId, messageId: 500, userId: 4, newReaction: [Like()]),
            TestReflection.Context(), TestContext.Current.CancellationToken);


        DbTopic reloaded = await ctx.Topics.SingleAsync(x => x.Id == topic.Id,
            TestContext.Current.CancellationToken);
        Assert.Equal(2, reloaded.AssigneeId);                 // кандидат стал исполнителем
        Assert.Equal(1, reloaded.LastLeavedUserId);           // прежний исполнитель записан как ушедший
        Assert.True(reloaded.IsOpen);
        Assert.False(reloaded.IsQueued);
        Assert.Null(reloaded.InterceptCandidateId);           // ResetIntercept
        Assert.Null(reloaded.InterceptMessageId);
        Assert.Empty(reloaded.InterceptVotes);

        DbQueue q = await ctx.Queue.SingleAsync(x => x.UserId == 1, TestContext.Current.CancellationToken);
        Assert.Equal(TimeSpan.FromDays(3), q.AssignmentPenalty);

        SendMessageRequest msg = TestBotClient.LastSendMessage(mock) ?? throw new("SendMessage not called");
        Assert.Equal(ChatId, msg.ChatId);
        Assert.Equal(1000, msg.MessageThreadId);
        Assert.Equal(ParseMode.MarkdownV2, msg.ParseMode);
        Assert.True(msg.DisableNotification);
        Assert.NotNull(msg.ReplyParameters);
        Assert.Equal(500, msg.ReplyParameters.MessageId);

        string prefix = TestResources.GetString(nameof(Resources.InterceptReactionsMiddleware_Result));
        Assert.StartsWith(prefix, msg.Text);
        Assert.Contains("Иван", msg.Text);
        Assert.Contains("tg://user?id=2", msg.Text);
        Assert.Equal(1, nextCalls());
    }

    // ================= add-vote: threshold by time =================

    [Fact]
    public async Task FirstVote_LockExpired_TriggersSwap()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1, "owner");
        TestDb.User(ctx, 2, "candidate");
        TestDb.User(ctx, 3, "voter");
        // LockDuration = 1h; LockedFrom = now - 2h → истёк, одного голоса достаточно
        DbTopic topic = TestDb.Topic(ctx, 1, 1000, Base, true,
            assigneeId: 1, interceptCandidateId: 2, interceptMessageId: 500,
            lockedFrom: QueueServiceFactory.DefaultNow.AddHours(-2));
        TestDb.QueueElement(ctx, 1);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        (Mock<ITelegramBotClient> mock, ITelegramBotClient client, InterceptReactionsMiddleware mw, Func<int> nextCalls) = NewClient(ctx);

        await mw.InvokeAsync(
            ReactionUpdate(chatId: ChatId, messageId: 500, userId: 3, newReaction: [Like()]),
            TestReflection.Context(), TestContext.Current.CancellationToken);

        DbTopic reloaded = await ctx.Topics.SingleAsync(x => x.Id == topic.Id,
            TestContext.Current.CancellationToken);
        Assert.Equal(2, reloaded.AssigneeId);
        Assert.Equal(1, reloaded.LastLeavedUserId);
        Assert.True(reloaded.IsOpen);
        Assert.False(reloaded.IsQueued);

        DbQueue q = await ctx.Queue.SingleAsync(x => x.UserId == 1, TestContext.Current.CancellationToken);
        Assert.Equal(TimeSpan.FromDays(3), q.AssignmentPenalty);

        Assert.Equal(1, TestBotClient.Count(mock, typeof(SendMessageRequest)));
        Assert.Equal(1, nextCalls());
    }

    // ================= add-vote: duplicate =================

    [Fact]
    public async Task DuplicateVote_Noop()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1, "owner");
        TestDb.User(ctx, 2, "candidate");
        TestDb.User(ctx, 3, "voter");
        DbTopic topic = TestDb.Topic(ctx, 1, 1000, Base, true,
            assigneeId: 1, interceptCandidateId: 2, interceptMessageId: 500,
            lockedFrom: QueueServiceFactory.DefaultNow.AddHours(-2)); // время бы позволило swap
        topic.InterceptVotes.Add(3); // уже голосовал
        TestDb.QueueElement(ctx, 1);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        (Mock<ITelegramBotClient> mock, ITelegramBotClient client, InterceptReactionsMiddleware mw, Func<int> nextCalls) = NewClient(ctx);

        // "повторный лайк" (в реальности Telegram обычно не шлёт, но защищаемся)
        await mw.InvokeAsync(
            ReactionUpdate(chatId: ChatId, messageId: 500, userId: 3, newReaction: [Like()]),
            TestReflection.Context(), TestContext.Current.CancellationToken);

        DbTopic reloaded = await ctx.Topics.SingleAsync(x => x.Id == topic.Id,
            TestContext.Current.CancellationToken);
        Assert.Single(reloaded.InterceptVotes);          // не задублировался
        Assert.Equal(1, reloaded.AssigneeId);            // swap не произошёл
        Assert.Equal(1, nextCalls());
        Assert.Equal(0, TestBotClient.Count(mock, typeof(SendMessageRequest)));
    }

    // ================= remove-vote =================

    [Fact]
    public async Task RemovedReaction_RemovesVote()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1, "owner");
        TestDb.User(ctx, 2, "candidate");
        TestDb.User(ctx, 3, "voter3");
        TestDb.User(ctx, 4, "voter4");
        DbTopic topic = TestDb.Topic(ctx, 1, 1000, Base, true,
            assigneeId: 1, interceptCandidateId: 2, interceptMessageId: 500);
        topic.InterceptVotes.Add(3);
        topic.InterceptVotes.Add(4);
        TestDb.QueueElement(ctx, 1);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        (Mock<ITelegramBotClient> mock, ITelegramBotClient client, InterceptReactionsMiddleware mw, Func<int> nextCalls) = NewClient(ctx);

        await mw.InvokeAsync(
            ReactionUpdate(chatId: ChatId, messageId: 500, userId: 3,
                oldReaction: [Like()], newReaction: []),
            TestReflection.Context(), TestContext.Current.CancellationToken);

        DbTopic reloaded = await ctx.Topics.SingleAsync(x => x.Id == topic.Id,
            TestContext.Current.CancellationToken);
        Assert.Single(reloaded.InterceptVotes);
        Assert.Contains(4, reloaded.InterceptVotes);
        Assert.DoesNotContain(3, reloaded.InterceptVotes);
        Assert.Equal(1, reloaded.AssigneeId);
        Assert.Equal(0, TestBotClient.Count(mock, typeof(SendMessageRequest)));
        Assert.Equal(1, nextCalls());
    }

    [Fact]
    public async Task RemovedReaction_UserNotInVotes_Noop()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1, "owner");
        TestDb.User(ctx, 2, "candidate");
        TestDb.User(ctx, 3, "voter");
        DbTopic topic = TestDb.Topic(ctx, 1, 1000, Base, true,
            assigneeId: 1, interceptCandidateId: 2, interceptMessageId: 500);
        TestDb.QueueElement(ctx, 1);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        (Mock<ITelegramBotClient> mock, ITelegramBotClient client, InterceptReactionsMiddleware mw, Func<int> nextCalls) = NewClient(ctx);

        await mw.InvokeAsync(
            ReactionUpdate(chatId: ChatId, messageId: 500, userId: 3,
                oldReaction: [Like()], newReaction: []),
            TestReflection.Context(), TestContext.Current.CancellationToken);

        DbTopic reloaded = await ctx.Topics.SingleAsync(x => x.Id == topic.Id,
            TestContext.Current.CancellationToken);
        Assert.Empty(reloaded.InterceptVotes);
        Assert.Equal(1, nextCalls());
        Assert.Equal(0, TestBotClient.Count(mock, typeof(SendMessageRequest)));
    }

    // ================= non-👍 reaction =================

    [Fact]
    public async Task NonLikeReaction_Ignored()
    {
        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1, "owner");
        TestDb.User(ctx, 2, "candidate");
        TestDb.User(ctx, 3, "voter");
        DbTopic topic = TestDb.Topic(ctx, 1, 1000, Base, true,
            assigneeId: 1, interceptCandidateId: 2, interceptMessageId: 500,
            lockedFrom: QueueServiceFactory.DefaultNow.AddHours(-2));
        TestDb.QueueElement(ctx, 1);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        (Mock<ITelegramBotClient> mock, ITelegramBotClient client, InterceptReactionsMiddleware mw, Func<int> nextCalls) = NewClient(ctx);

        await mw.InvokeAsync(
            ReactionUpdate(chatId: ChatId, messageId: 500, userId: 3, newReaction: [Heart()]),
            TestReflection.Context(), TestContext.Current.CancellationToken);

        DbTopic reloaded = await ctx.Topics.SingleAsync(x => x.Id == topic.Id,
            TestContext.Current.CancellationToken);
        Assert.Empty(reloaded.InterceptVotes);
        Assert.Equal(1, reloaded.AssigneeId);   // swap не произошёл
        Assert.Equal(1, nextCalls());
        Assert.Equal(0, TestBotClient.Count(mock, typeof(SendMessageRequest)));
    }

    // ================= Next всегда вызывается =================

    [Fact]
    public async Task Next_IsCalledOnEveryPath_WhenNoException()
    {
        // Прогоняем разные нетривиальные ветки, каждый раз проверяем Next
        int calls = 0;
        FrameUpdateDelegate next = (_, _, _) =>
        {
            calls++;
            return Task.CompletedTask;
        };

        FeedbackBotContext ctx = NewContext();
        TestDb.User(ctx, 1, "owner");
        TestDb.User(ctx, 2, "candidate");
        TestDb.User(ctx, 3, "voter");
        TestDb.Topic(ctx, 1, 1000, Base, true,
            assigneeId: 1, interceptCandidateId: 2, interceptMessageId: 500,
            lockedFrom: QueueServiceFactory.DefaultNow);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        InterceptReactionsMiddleware mw = InterceptReactionsMiddlewareFactory.Create(
            ctx, TestBotClient.Create().Object, next: next);

        await mw.InvokeAsync(
            new Update { Message = new() { Id = 1, Chat = new() { Id = ChatId } } },
            TestReflection.Context(), TestContext.Current.CancellationToken);
        await mw.InvokeAsync(
            ReactionUpdate(ChatId, 500, 3, newReaction: [Like()]),
            TestReflection.Context(), TestContext.Current.CancellationToken);
        await mw.InvokeAsync(
            ReactionUpdate(ChatId, 500, 3, oldReaction: [Like()], newReaction: []),
            TestReflection.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(3, calls);
    }
}