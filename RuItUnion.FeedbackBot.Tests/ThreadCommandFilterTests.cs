using System.Reflection;
using Microsoft.Extensions.Options;
using RuItUnion.FeedbackBot.Middlewares;
using RuItUnion.FeedbackBot.Options;
using Telegram.Bot.Types;
using TgBotFrame.Middleware;

namespace RuItUnion.FeedbackBot.Tests;

public class ThreadCommandFilterTests
{
    public const int CHAT_ID = -100123;

    private readonly ThreadCommandFilterMiddleware _commandFilterMiddleware = new(new OptionsWrapper<AppOptions>(new()
    {
        FeedbackChatId = CHAT_ID,
        Start = "",
    }), null!);

    public ThreadCommandFilterTests()
    {
        Delegate = DelegateAction;
        PropertyInfo property = typeof(ThreadCommandFilterMiddleware).GetProperty("Next", BindingFlags.NonPublic
            | BindingFlags.GetProperty
            | BindingFlags.SetProperty
            | BindingFlags.Instance) ?? throw new MethodAccessException();
        property.SetValue(_commandFilterMiddleware, Delegate);
        Passed = false;
    }

    public bool Passed { get; set; }
    public FrameUpdateDelegate Delegate { get; set; }

    private Task DelegateAction(Update update, FrameContext context, CancellationToken ct = default)
    {
        Passed = true;
        return Task.CompletedTask;
    }

    [Fact]
    public async Task PassCommand()
    {
        await _commandFilterMiddleware.InvokeAsync(new()
        {
            Message = new()
            {
                Id = 1,
                Text = "123",
            },
        }, new()
        {
            Properties =
            {
                { "CommandName", "help" },
                { "ChatId", (long?)CHAT_ID },
                { "UserId", (long?)CHAT_ID },
                { "ThreadId", (int?)2 },
            },
        }, CancellationToken.None);

        Assert.True(Passed);
    }

    [Fact]
    public async Task PassCommandNotInDm()
    {
        await _commandFilterMiddleware.InvokeAsync(new()
        {
            Message = new()
            {
                Id = 1,
                Text = "123",
            },
        }, new()
        {
            Properties =
            {
                { "CommandName", "help" },
                { "ChatId", (long?)CHAT_ID },
                { "UserId", (long?)CHAT_ID+1 },
                { "ThreadId", (int?)2 },
            },
        }, CancellationToken.None);

        Assert.False(Passed);
    }

    [Fact]
    public async Task PassStart()
    {
        await _commandFilterMiddleware.InvokeAsync(new()
        {
            Message = new()
            {
                Id = 1,
                Text = "123",
            },
        }, new()
        {
            Properties =
            {
                { "CommandName", "start" },
                { "ChatId", 1 },
                { "ThreadId", null },
            },
        }, CancellationToken.None);

        Assert.True(Passed);
    }

    [Fact]
    public async Task PassEmpty()
    {
        await _commandFilterMiddleware.InvokeAsync(new()
        {
            Message = new()
            {
                Id = 1,
                Text = "123",
            },
        }, new()
        {
            Properties =
            {
                { "CommandName", null },
                { "ChatId", 1 },
                { "ThreadId", null },
            },
        }, CancellationToken.None);

        Assert.True(Passed);
    }

    //[Fact]
    //public async Task NonPassCommand()
    //{
    //    await _commandFilterMiddleware.InvokeAsync(new()
    //    {
    //        Message = new()
    //        {
    //            Id = 1,
    //            Text = "123",
    //        },
    //    }, new()
    //    {
    //        Properties =
    //        {
    //            { "CommandName", "open" },
    //            { "ChatId", (long?)1L },
    //            { "ThreadId", null },
    //        },
    //    }, CancellationToken.None);

    //    Assert.False(Passed);
    //}
}