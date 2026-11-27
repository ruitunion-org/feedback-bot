using System.ComponentModel;
using RuItUnion.FeedbackBot.Data.Models;

namespace RuItUnion.FeedbackBot.Services;

public sealed class TopicTitleGenerator(FeedbackBotContext dataContext)
{
    [Localizable(false)]
    public string GetTopicTitle(in DbTopic topic)
    {
        int topicId = topic.Id;
        long userId = topic.UserChatId;
        string emoji = topic.IsOpen switch
        {
            true when topic.AssigneeId is null => @"🟩",
            true => @"🔷",
            false => @"❌",
        };
        string result = emoji
                        + "\t" + (topicId == 0 ? string.Empty : topicId.ToString("D") + ":")
                        + (string.IsNullOrWhiteSpace(topic.Header) ? string.Empty : "\t") + topic.Header + "\t"
                        + (topic.User?.ToString()
                           ?? dataContext.Users.FirstOrDefault(x => x.Id == userId)?.ToString());
        return result.Length > 128 ? result[..127] + "…" : result;
    }
}