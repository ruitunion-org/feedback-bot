using System.ComponentModel;
using System.Runtime.CompilerServices;
using RuItUnion.FeedbackBot.Commands;
using TgBotFrame.Commands.Middleware;

namespace RuItUnion.FeedbackBot.Middlewares;

public class HashMiddleware(IServiceScopeFactory serviceScopeFactory, IOptions<AppOptions> options) : FrameMiddleware
{
    internal const string HASH_KEY = "IsHashSyntax";
    [Localizable(false)] private static readonly string[] _separators = ["\r\n", "\n", " "];

    private readonly long _chatId = options.Value.FeedbackChatId;

    public override async Task InvokeAsync(Update update, FrameContext context, CancellationToken ct = default)
    {
        if (update.Message is not null && (update.Message.Text?.StartsWith('#') ?? false)
                                       && update.Message?.Chat.Id == _chatId
                                       && update.Message?.MessageThreadId is not null)
        {
            AsyncServiceScope scope = serviceScopeFactory.CreateAsyncScope();
            await using ConfiguredAsyncDisposable _ = scope.ConfigureAwait(false);
            FeedbackBotContext dataContext = scope.ServiceProvider.GetRequiredService<FeedbackBotContext>();

            if (await dataContext.Topics.AsNoTracking().AnyAsync(x => x.ThreadId == update.Message.MessageThreadId, ct)
                    .ConfigureAwait(false))
            {
                string[] split =
                    update.Message.Text.Split(_separators, 2, StringSplitOptions.TrimEntries);
                if (split.Length == 2)
                {
                    context.Properties[CommandSplitterMiddleware.COMMAND_NAME_KEY] = nameof(TagController.Tag);
                    split[0] = split[0][1..];
                    context.Properties[CommandSplitterMiddleware.COMMAND_ARGS_KEY] = split;
                    context.Properties[HASH_KEY] = true;
                }
            }
        }

        await Next(update, context, ct).ConfigureAwait(false);
    }
}