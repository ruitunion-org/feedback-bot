using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RuItUnion.FeedbackBot.Data.Models;
using static RuItUnion.FeedbackBot.Data.Old.Repository;

namespace RuItUnion.FeedbackBot.Data.Old;

public class Migrator(FeedbackBotContext newContext, DatabaseContext oldContext, ILogger<Migrator> logger)
{
    public virtual async Task Migrate(CancellationToken cancellationToken = default)
    {
        string[] oldMigrations =
            (await oldContext.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false)).ToArray();
        if (!oldMigrations.Any(x => x.EndsWith("_Initial")))
        {
            logger.LogInformation("No old migration in database, skipping...");
            return;
        }

        foreach (User oldUser in oldContext.Users.AsNoTracking())
        {
            await newContext.Users.AddAsync(new()
            {
                Id = oldUser.Id,
                FirstName = string.Empty,
                LastName = null,
                UserName = null,
            }, cancellationToken).ConfigureAwait(false);
            if (oldUser.Banned)
            {
                await newContext.Bans.AddAsync(new()
                {
                    UserId = oldUser.Id,
                    Until = DateTime.MaxValue,
                    Description = string.Empty,
                }, cancellationToken).ConfigureAwait(false);
            }
        }

        foreach (Topic topic in oldContext.Topic.AsNoTracking().Include(x => x.User).Include(x => x.Replies))
        {
            await newContext.Topics.AddAsync(new()
            {
                ThreadId = (int)topic.Id,
                UserChatId = topic.UserId,
                IsOpen = topic.IsOpen,
                Replies = topic.Replies.Select(x => new DbReply
                {
                    ChatThreadId = (int)x.TopicId,
                    UserMessageId = (int)x.BotMessageId,
                    ChatMessageId = (int)x.Id,
                }).ToList(),
            }, cancellationToken).ConfigureAwait(false);
        }

        await newContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        int deleted = await oldContext.Replies.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        deleted += await oldContext.Topic.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        deleted += await oldContext.Users.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

        if (deleted > 0)
        {
            logger.LogWarning("Use /sync command in group chat for update topic headers");
        }
    }
}