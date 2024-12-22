using RuItUnion.FeedbackBot.Data.Models;
using TgBotFrame.Commands.Authorization.Interfaces;

namespace RuItUnion.FeedbackBot.Data;

public interface IFeedbackBotContext : IAuthorizationData
{
    DbSet<DbReply> Replies { get; init; }
    DbSet<DbTopic> Topics { get; init; }
}