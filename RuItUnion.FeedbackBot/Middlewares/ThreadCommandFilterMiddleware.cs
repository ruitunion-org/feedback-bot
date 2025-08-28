namespace RuItUnion.FeedbackBot.Middlewares;

public class ThreadCommandFilterMiddleware(IOptions<AppOptions> options, IFeedbackBotContext db) : FrameMiddleware
{
    private readonly long _chatId = options.Value.FeedbackChatId;

    public override async Task InvokeAsync(Update update, FrameContext context, CancellationToken ct = default)
    {
        string? commandName = context.GetCommandName();
        if (string.IsNullOrEmpty(commandName)
            || string.Equals(commandName, @"start", StringComparison.OrdinalIgnoreCase)
            || (_chatId == context.GetChatId() && context.GetThreadId() is not null)
            || await db.RoleMembers.AnyAsync(x => x.RoleId == -1 && x.UserId == context.GetUserId(), ct).ConfigureAwait(false))
        {
            if (string.Equals(commandName, @"help", StringComparison.OrdinalIgnoreCase) && context.GetChatId() != context.GetUserId()) return;
            await Next(update, context, ct).ConfigureAwait(false);
        }
    }
}