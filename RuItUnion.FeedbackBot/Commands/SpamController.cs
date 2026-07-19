using System.Globalization;
using System.Text.Json;
using RuItUnion.FeedbackBot.Data.Models;
using Telegram.Bot.Types.Enums;
using TgBotFrame.Commands;
using TgBotFrame.Commands.Attributes;
using TgBotFrame.Commands.Authorization.Attributes;
using TgBotFrame.Services;

namespace RuItUnion.FeedbackBot.Commands;

[CommandController("Spam")]
[Restricted("admin")]
public class SpamController(
    IOptions<AppOptions> options,
    BotService botService,
    IFeedbackBotContext db,
    ITelegramBotClient botClient) : CommandControllerBase
{
    private readonly long _chatId = options.Value.FeedbackChatId;

    [Command(nameof(SpamInfo))]
    public async Task SpamInfo(long id)
    {
        DbSpamMessage? spam = await db.SpamMessages.AsNoTracking().Include(x => x.User)
            .FirstOrDefaultAsync(x => x.Id == id).ConfigureAwait(false);
        if (spam is null)
        {
            await botClient.SendMessage(_chatId,
                    ResourceManager.GetString(nameof(SpamController_NotFound), Context.GetCultureInfo())!,
                    messageThreadId: Update.Message?.MessageThreadId,
                    parseMode: ParseMode.Html)
                .ConfigureAwait(false);
            return;
        }

        CultureInfo culture = Context.GetCultureInfo();
        string noData = ResourceManager.GetString(nameof(UserInfoMessage_NoData), culture)!;
        string head = string.Format(ResourceManager.GetString(nameof(UserInfoMessage), culture)!
            , spam.User.UserName ?? noData
            , spam.User.FirstName
            , spam.User.LastName ?? noData
            , spam.User.Id
            , culture.NativeName);
        string message = head + Environment.NewLine + Environment.NewLine;
        Update? update = spam.Update.Deserialize<Update>();
        message += update?.Message?.Text;
        if (message.Length > 4096)
        {
            message = message[..4095];
            message += @"…";
        }

        await botClient.SendMessage(_chatId,
                message,
                messageThreadId: Update.Message?.MessageThreadId,
                parseMode: ParseMode.Html)
            .ConfigureAwait(false);
    }

    [Command(nameof(SpamDownload))]
    public async Task SpamDownload(long id)
    {
        DbSpamMessage? spam = await db.SpamMessages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id)
            .ConfigureAwait(false);
        if (spam is not null)
        {
            using MemoryStream stream = new();
            await JsonSerializer.SerializeAsync(stream, spam.Update).ConfigureAwait(false);
            stream.Position = 0;
            await botClient.SendDocument(_chatId, InputFile.FromStream(stream),
                messageThreadId: Update.Message?.MessageThreadId,
                replyParameters: new()
                {
                    AllowSendingWithoutReply = true,
                    ChatId = _chatId,
                    MessageId = Update.Message?.Id ?? 0,
                }).ConfigureAwait(false);
        }
        else
        {
            await botClient.SendMessage(_chatId,
                    ResourceManager.GetString(nameof(SpamController_NotFound), Context.GetCultureInfo())!,
                    messageThreadId: Update.Message?.MessageThreadId)
                .ConfigureAwait(false);
        }
    }

    [Command(nameof(SpamRestore))]
    public async Task SpamRestore(long id)
    {
        DbSpamMessage? spam = await db.SpamMessages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id)
            .ConfigureAwait(false);
        if (spam?.Update is not null)
        {
            Update? update = spam.Update.Deserialize<Update>();
            await db.Bans.Where(x => x.UserId == spam.Id).ExecuteDeleteAsync().ConfigureAwait(false);
            if (update is not null)
                await botService.HandleUpdateAsync(botClient, update, CancellationToken).ConfigureAwait(false);
        }
        else
        {
            await botClient.SendMessage(_chatId,
                    ResourceManager.GetString(nameof(SpamController_NotFound), Context.GetCultureInfo())!,
                    messageThreadId: Update.Message?.MessageThreadId)
                .ConfigureAwait(false);
        }
    }
}