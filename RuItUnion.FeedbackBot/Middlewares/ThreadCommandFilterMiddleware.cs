namespace RuItUnion.FeedbackBot.Middlewares;

public class ThreadCommandFilterMiddleware(IOptions<AppOptions> options) : FrameMiddleware
{
    private readonly long _chatId = options.Value.FeedbackChatId;

    public override async Task InvokeAsync(Update update, FrameContext context, CancellationToken ct = default)
    {
        string? commandName = context.GetCommandName();
        if (string.IsNullOrEmpty(commandName)
            || string.Equals(commandName, @"start", StringComparison.OrdinalIgnoreCase)
            || (_chatId == context.GetChatId() && context.GetThreadId() is not null))
        {
            await Next(update, context, ct).ConfigureAwait(false);
        }
    }
}