using Telegram.Bot.Types.Enums;

namespace RuItUnion.FeedbackBot.Middlewares;

public class AdminRoleSyncMiddleware(IOptions<AppOptions> options, ITelegramBotClient botClient, FeedbackBotContext db)
    : FrameMiddleware
{
    private readonly long _chatId = options.Value.FeedbackChatId;

    public override async Task InvokeAsync(Update update, FrameContext context, CancellationToken ct = default)
    {
        if (update.Message?.Chat.Id == _chatId)
        {
            if (update.Message.LeftChatMember is not null)
            {
                await db.RoleMembers.Where(x => x.UserId == update.Message.LeftChatMember.Id).ExecuteDeleteAsync(ct)
                    .ConfigureAwait(false);
            }
            else if (context.GetCommandName() is not null)
            {
                long? chatId = context.GetChatId()!;
                long? userId = context.GetUserId()!;
                ChatMember[] admins = await botClient.GetChatAdministrators(chatId, ct).ConfigureAwait(false);
                bool isChatAdmin = admins.Any(x =>
                    x.User.Id == userId && x.Status is ChatMemberStatus.Creator or ChatMemberStatus.Administrator);
                if (isChatAdmin && !await db.RoleMembers.AnyAsync(x => x.UserId == userId && x.RoleId == -1, ct)
                        .ConfigureAwait(false))
                {
                    await db.RoleMembers.AddAsync(new()
                    {
                        RoleId = -1,
                        UserId = userId.Value,
                    }, ct).ConfigureAwait(false);
                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                }
            }
        }

        await Next(update, context, ct).ConfigureAwait(false);
    }
}