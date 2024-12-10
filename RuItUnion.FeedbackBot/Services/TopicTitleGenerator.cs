using RuItUnion.FeedbackBot.Data.Models;

namespace RuItUnion.FeedbackBot.Services;

public class TopicTitleGenerator
{
    public virtual string GetTopicTitle(in DbTopic topic) => topic.ToString();
}