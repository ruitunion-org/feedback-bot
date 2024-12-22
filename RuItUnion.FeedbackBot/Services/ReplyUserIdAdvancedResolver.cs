using TgBotFrame.Commands.Authorization.Services;

namespace RuItUnion.FeedbackBot.Services;

public class ReplyUserIdAdvancedResolver(IFeedbackBotContext db) : ReplyUserIdResolver
{
    public override async ValueTask<long?> GetReplyUserId(Update update, CancellationToken ct = default)
    {
        if (update.Message?.ReplyToMessage?.From?.IsBot == true && update.Message.MessageThreadId is not null)
        {
            string? name = update.Message.ReplyToMessage.ForwardSenderName;
            long userId = await db.Topics
                .AsNoTracking()
                .Where(x => x.ThreadId == update.Message.MessageThreadId
                            && (x.User.LastName == null
                                ? x.User.FirstName
                                : x.User.FirstName + @" " + x.User.LastName) == name)
                .Select(x => x.User.Id)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
            return userId == 0 ? null : userId;
        }

        return update.Message?.ReplyToMessage?.From?.Id;
    }
}