using System.Diagnostics.Metrics;
using System.Globalization;
using System.Reflection;
using System.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;
using Microsoft.FeatureManagement.FeatureFilters;
using Moq;
using RuItUnion.FeedbackBot.Data;
using RuItUnion.FeedbackBot.Data.Models;
using RuItUnion.FeedbackBot.Middlewares;
using RuItUnion.FeedbackBot.Options;
using RuItUnion.FeedbackBot.Services;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;
using Testcontainers.PostgreSql;
using TgBotFrame.Commands;
using TgBotFrame.Commands.Authorization.Models;
using TgBotFrame.Middleware;

namespace RuItUnion.FeedbackBot.Tests;

/// <summary>
///     Time provider с фиксированным, детерминированно сдвигаемым временем.
/// </summary>
public sealed class TestTimeProvider(DateTime utcNow = default) : TimeProvider
{
    public DateTime UtcNow { get; set; } = utcNow;

    public override DateTimeOffset GetUtcNow() => new(UtcNow, TimeSpan.Zero);

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}

/// <summary>
///     IVariantFeatureManager test double с настраиваемым списком включённых фич.
/// </summary>
public sealed class TestFeatureManager(HashSet<string>? enabledFeatures = null) : IVariantFeatureManager
{
    public HashSet<string> EnabledFeatures { get; } = enabledFeatures ?? [];

    public ValueTask<bool> IsEnabledAsync(string feature, CancellationToken cancellationToken = default) =>
        new(EnabledFeatures.Contains(feature));

    public ValueTask<bool> IsEnabledAsync<TContext>(
        string feature, TContext context, CancellationToken cancellationToken = default) =>
        IsEnabledAsync(feature, cancellationToken);

    public async IAsyncEnumerable<string> GetFeatureNamesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (string feature in EnabledFeatures)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return feature;
        }
    }

    public ValueTask<Variant> GetVariantAsync(string feature, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(nameof(GetVariantAsync));

    public ValueTask<Variant> GetVariantAsync(
        string feature, ITargetingContext context, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(nameof(GetVariantAsync));
}

[CollectionDefinition(nameof(FeedbackBotDatabaseCollection))]
public sealed class FeedbackBotDatabaseCollection : ICollectionFixture<TestDatabase>
{
}

/// <summary>
///     Тестовая база на реальном PostgreSQL (Testcontainers).
///     Контейнер запускается в конструкторе, поэтому доступен сразу.
///     Каждое CreateContext даёт чистую схему (EnsureDeleted + EnsureCreated).
/// </summary>
public sealed class TestDatabase : IAsyncDisposable
{
    public const string DatabaseName = "feedback_bot_tests";

    private readonly PostgreSqlContainer _container;

    public TestDatabase()
    {
        _container = new PostgreSqlBuilder("docker.io/library/postgres:18")
            .WithDatabase(DatabaseName)
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        _container.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    public string ConnectionString => _container.GetConnectionString();

    public FeedbackBotContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FeedbackBotContext>()
            .UseNpgsql(ConnectionString).Options;
        FeedbackBotContext context = new(options);
        context.Database.EnsureDeleted();
        context.Database.EnsureCreated();
        return context;
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>
///     Фабрики тестовых сущностей с заполнением всех null!-навигаций.
/// </summary>
public static class TestDb
{
    public static DbUser User(
        FeedbackBotContext db, long id, string? userName = null,
        string firstName = "Ivan", string? lastName = "Ivanov")
    {
        DbUser user = new()
        {
            Id = id,
            UserName = userName,
            FirstName = firstName,
            LastName = lastName,
        };
        db.Users.Add(user);
        return user;
    }

    public static DbTopic Topic(
        FeedbackBotContext db, long userChatId, int threadId, DateTime created,
        bool isQueued = false, long? assigneeId = null, long? interceptCandidateId = null,
        int? interceptMessageId = null, long? lastLeavedUserId = null,
        DateTime? lockedFrom = null)
    {
        DbTopic topic = new()
        {
            ThreadId = threadId,
            UserChatId = userChatId,
            Created = created,
            IsQueued = isQueued,
            AssigneeId = assigneeId,
            InterceptCandidateId = interceptCandidateId,
            InterceptMessageId = interceptMessageId,
            LastLeavedUserId = lastLeavedUserId,
            LockedFrom = lockedFrom ?? DateTime.MinValue,
        };
        db.Topics.Add(topic);
        return topic;
    }

    public static DbQueue QueueElement(
        FeedbackBotContext db, long userId, bool inQueue = true,
        DateTime lastAssignmentTime = default, TimeSpan assignmentPenalty = default)
    {
        DbQueue queue = new()
        {
            UserId = userId,
            InQueue = inQueue,
            LastAssignmentTime = lastAssignmentTime,
            AssignmentPenalty = assignmentPenalty,
        };
        db.Queue.Add(queue);
        return queue;
    }

    public static DbMentor Mentor(FeedbackBotContext db, long mentorId, long noviceId, bool isActive = true)
    {
        DbMentor mentor = new()
        {
            MentorId = mentorId,
            NoviceId = noviceId,
            IsActive = isActive,
        };
        db.Mentors.Add(mentor);
        return mentor;
    }
}

/// <summary>
///     Хелперы мок-клиента Telegram. В Telegram.Bot 22.x все запросы
///     идут через ITelegramBotClient.SendRequest, поэтому мокаются именно они.
/// </summary>
public static class TestBotClient
{
    public static Mock<ITelegramBotClient> Create() => new(MockBehavior.Loose);

    public static Mock<ITelegramBotClient> WithSendMessage(
        Mock<ITelegramBotClient> mock, int messageId = 1, long? chatId = null)
    {
        mock
            .Setup(c => c.SendRequest(It.IsAny<IRequest<Message>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Message
            {
                Id = messageId,
                Chat = new()
                {
                    Id = chatId ?? -1,
                },
            });
        return mock;
    }

    public static void WithEditForumTopic(Mock<ITelegramBotClient> mock)
    {
        mock
            .Setup(c => c.SendRequest<bool>(It.IsAny<IRequest<bool>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    public static void WithEditForumTopicNotModified(Mock<ITelegramBotClient> mock)
    {
        mock
            .Setup(c => c.SendRequest<bool>(It.IsAny<IRequest<bool>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiRequestException("Bad Request: TOPIC_NOT_MODIFIED"));
    }

    public static SendMessageRequest? LastSendMessage(Mock<ITelegramBotClient> mock) =>
        mock.Invocations.LastOrDefault(x => x.Arguments[0] is SendMessageRequest)
            ?.Arguments[0] as SendMessageRequest;

    public static EditForumTopicRequest? LastEditForumTopic(Mock<ITelegramBotClient> mock) =>
        mock.Invocations.LastOrDefault(x => x.Arguments[0] is EditForumTopicRequest)
            ?.Arguments[0] as EditForumTopicRequest;

    public static int Count(Mock<ITelegramBotClient> mock, Type requestType) =>
        mock.Invocations.Count(x => requestType.IsInstanceOfType(x.Arguments[0]));
}

/// <summary>
///     Reflection-харнес для FrameMiddleware и CommandControllerBase (internal-setters).
/// </summary>
public static class TestReflection
{
    public static void SetNext<T>(T middleware, FrameUpdateDelegate next) where T : FrameMiddleware =>
        SetPrivate(middleware, "Next", next);

    public static void SetControllerProperties(
        CommandControllerBase controller, FrameContext context, Update update,
        CancellationToken cancellationToken = default)
    {
        SetPrivate(controller, "Context", context);
        SetPrivate(controller, "Update", update);
        SetPrivate(controller, "CancellationToken", cancellationToken);
    }

    public static FrameContext Context(
        long? chatId = null, long? userId = null, int? threadId = null, int? messageId = null,
        string culture = "ru-RU")
    {
        var frameContext = new FrameContext();
        frameContext.Properties["ChatId"] = chatId;
        frameContext.Properties["UserId"] = userId;
        frameContext.Properties["ThreadId"] = threadId;
        frameContext.Properties["MessageId"] = messageId;
        frameContext.Properties["CultureInfo"] = CultureInfo.GetCultureInfo(culture);
        return frameContext;
    }

    private static void SetPrivate(object target, string name, object? value)
    {
        PropertyInfo property = target.GetType().GetProperty(
            name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                       ?? throw new MissingMemberException(target.GetType().Name, name);
        property.SetValue(target, value);
    }
}

public static class TestResources
{
    private static readonly ResourceManager _manager =
        new("RuItUnion.FeedbackBot.Properties.Resources", typeof(QueueService).Assembly);

    public static string GetString(string name, CultureInfo? culture = null) =>
        _manager.GetString(name, culture) ?? throw new MissingManifestResourceException(name);
}


public class TestMeterFactory : IMeterFactory
{
    public void Dispose()
    {
        throw new NotImplementedException();
    }

    public Meter Create(MeterOptions options) => new Meter(options);
}

/// <summary>
///     Фабрика реального QueueService поверх тестовой БД.
/// </summary>
public static class QueueServiceFactory
{
    public const long FeedbackChatId = -100_424_242;
    public static readonly DateTime DefaultNow = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    public static QueueService Create(
        FeedbackBotContext db, ITelegramBotClient botClient,
        bool featureEnabled = true, TimeProvider? clock = null,
        TimeSpan? cooldownDuration = null)
    {
        var options = new OptionsWrapper<AppOptions>(new()
        {
            FeedbackChatId = FeedbackChatId,
            Start = string.Empty,
            CooldownDuration = cooldownDuration ?? TimeSpan.FromHours(6),
        });
        return new QueueService(
            options,
            db,
            new FeedbackMetricsService(new TestMeterFactory()),
            new TestFeatureManager(featureEnabled ? ["Queue"] : []),
            botClient,
            new TopicTitleGenerator(db),
            clock ?? new TestTimeProvider(DefaultNow));
    }
}
