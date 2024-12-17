using RuItUnion.FeedbackBot.Data.Models;

namespace RuItUnion.FeedbackBot.Services;

public class TopicTitleGenerator
{
    public virtual string GetTopicTitle(in DbTopic topic)
    {
        string result = topic.ToString();
        return result.Length > 128 ? result[..127] + @"…" : result;
    }
}