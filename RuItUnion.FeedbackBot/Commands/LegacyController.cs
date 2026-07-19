using System.Reflection;
using Microsoft.FeatureManagement.Mvc;
using TgBotFrame.Commands;
using TgBotFrame.Commands.Attributes;
using TgBotFrame.Commands.Authorization.Attributes;
using TgBotFrame.Commands.Authorization.Commands;

namespace RuItUnion.FeedbackBot.Commands;

[CommandController("Legacy")]
[FeatureGate("LegacyCommands")]
public class LegacyController(
    ITelegramBotClient botClient,
    ThreadController threadController,
    TagController tagController,
    RoleController roleController)
    : CommandControllerBase
{
    [Command(nameof(AddAdmin))]
    [Restricted("admin")]
    public async Task AddAdmin()
    {
        long? id = Update.Message?.ReplyToMessage?.From?.Id;
        if (!id.HasValue)
        {
            await botClient.SendMessage(Context.GetChatId()!,
                    ResourceManager.GetString(nameof(LegacyController_AddAdmin_NotReply), Context.GetCultureInfo())!,
                    messageThreadId: Update.Message?.MessageThreadId,
                    replyParameters: new()
                    {
                        AllowSendingWithoutReply = true,
                        ChatId = Context.GetChatId()!,
                        MessageId = Context.GetMessageId().GetValueOrDefault(-1),
                    })
                .ConfigureAwait(false);
            return;
        }

        SetContext(roleController);
        await roleController.Add(@"admin", id.Value).ConfigureAwait(false);
    }

    [Command(nameof(CloseTask))]
    [Restricted("admin")]
    public Task CloseTask()
    {
        SetContext(threadController);
        return threadController.Close();
    }

    [Command(nameof(SetTaskName))]
    [Restricted("admin")]
    public Task SetTaskName(string name)
    {
        SetContext(tagController);
        return tagController.Tag(@"header", name);
    }

    private void SetContext(in CommandControllerBase controller)
    {
        Type type = controller.GetType();

        PropertyInfo updateProperty =
            type.GetProperty(nameof(Update), BindingFlags.Public | BindingFlags.SetProperty | BindingFlags.Instance)
            ?? throw new ArgumentException(string.Empty, nameof(controller));
        updateProperty.SetMethod!.Invoke(controller, [Update]);
        PropertyInfo contextProperty =
            type.GetProperty(nameof(Context), BindingFlags.Public | BindingFlags.SetProperty | BindingFlags.Instance)
            ?? throw new ArgumentException(string.Empty, nameof(controller));
        contextProperty.SetMethod!.Invoke(controller, [Context]);
    }
}