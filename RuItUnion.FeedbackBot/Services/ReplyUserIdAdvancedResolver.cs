using Telegram.Bot.Types.Enums;
using TgBotFrame.Commands.Authorization.Services;

namespace RuItUnion.FeedbackBot.Services;

public class ReplyUserIdAdvancedResolver(IFeedbackBotContext db) : ReplyUserIdResolver
{
    public override async ValueTask<long?> GetReplyUserId(Update update, CancellationToken ct = default)
    {
        if (update.Message?.ReplyToMessage?.From?.IsBot != true || update.Message.MessageThreadId is null)
        {
            return update.Message?.ReplyToMessage?.From?.Id;
        }

        long userId = 0;
        switch (update.Message?.ReplyToMessage?.ForwardOrigin?.Type)
        {
            case MessageOriginType.Chat:
                userId = ((MessageOriginChat)update.Message.ReplyToMessage.ForwardOrigin).SenderChat.Id;
                break;
            case MessageOriginType.Channel:
                userId = ((MessageOriginChannel)update.Message.ReplyToMessage.ForwardOrigin).Chat.Id;
                break;
            case MessageOriginType.User:
                userId = ((MessageOriginUser)update.Message.ReplyToMessage.ForwardOrigin).SenderUser.Id;
                break;
            case MessageOriginType.HiddenUser:
                if (update.Message?.MessageThreadId is not null
                    && update.Message.ReplyToMessage?.Type is < MessageType.ForumTopicCreated
                        or > MessageType.GeneralForumTopicUnhidden
                    && update.Message?.ReplyToMessage?.From?.IsBot == true
                    && update.Message.ReplyToMessage.ForwardOrigin is not null)
                {
                    userId = await db.Topics
                        .Where(x => x.ThreadId == update.Message.MessageThreadId)
                        .Select(x => x.UserChatId)
                        .FirstOrDefaultAsync(ct).ConfigureAwait(false);
                }

                break;
            case null:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        return userId == 0 ? null : userId;
    }
}