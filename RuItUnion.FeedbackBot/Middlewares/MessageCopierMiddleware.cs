﻿using System.Collections.Frozen;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RuItUnion.FeedbackBot.Data;
using RuItUnion.FeedbackBot.Data.Models;
using RuItUnion.FeedbackBot.Options;
using RuItUnion.FeedbackBot.Services;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TgBotFrame.Commands.Extensions;
using TgBotFrame.Middleware;

namespace RuItUnion.FeedbackBot.Middlewares;

public class MessageCopierMiddleware(
    IOptions<AppOptions> options,
    ITelegramBotClient botClient,
    FeedbackBotContext db,
    FeedbackMetricsService feedbackMetricsService) : FrameMiddleware
{
    private static readonly FrozenSet<ReactionTypeEmoji> _highVoltageEmoji =
        new[] { new ReactionTypeEmoji { Emoji = @"⚡" } }.ToFrozenSet();

    private readonly long _chatId = options.Value.FeedbackChatId;

    public override async Task InvokeAsync(Update update, FrameContext context, CancellationToken ct = new())
    {
        if (string.IsNullOrEmpty(context.GetCommandName())
            && update.Message?.MessageThreadId is not null
            && update.Message.Chat.Id == _chatId
            && update.Message.ReplyToMessage is not null
            && update.Message.ReplyToMessage.Type == MessageType.Text
            && update.Message.ReplyToMessage.From?.Username == context.GetBotUsername())
        {
            DbTopic? topic = await db.Topics.AsNoTracking()
                .FirstOrDefaultAsync(x => x.ThreadId == update.Message.MessageThreadId, ct).ConfigureAwait(false);
            if (topic?.ThreadId is null)
            {
                return;
            }

            MessageId result;
            try
            {
                result = await botClient.CopyMessage(topic.UserChatId, update.Message.Chat.Id,
                    update.Message.MessageId,
                    cancellationToken: ct).ConfigureAwait(false);
            }
            catch (ApiRequestException ex)
                when (ex.Message == @"Forbidden: bot was blocked by the user")

            {
                await botClient.SendMessage(_chatId,
                    ResourceManager.GetString(nameof(MessageCopier_BotBanned), context.GetCultureInfo())!,
                    messageThreadId: update.Message.MessageThreadId, replyParameters: new()
                    {
                        ChatId = _chatId,
                        MessageId = update.Message.MessageId,
                        AllowSendingWithoutReply = true,
                    }, cancellationToken: ct).ConfigureAwait(false);
                return;
            }

            await db.Replies.AddAsync(new()
            {
                ChatThreadId = topic.ThreadId,
                ChatMessageId = update.Message.MessageId,
                UserMessageId = result.Id,
            }, ct).ConfigureAwait(false);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await botClient.SetMessageReaction(update.Message.Chat.Id, update.Message.MessageId, _highVoltageEmoji,
                cancellationToken: ct).ConfigureAwait(false);
            feedbackMetricsService.IncMessagesCopied(topic.ThreadId, topic.UserChatId);
        }

        await Next(update, context, ct).ConfigureAwait(false);
    }
}