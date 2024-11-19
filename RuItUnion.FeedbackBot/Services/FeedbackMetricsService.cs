using System.Diagnostics.Metrics;

namespace RuItUnion.FeedbackBot.Services;

public sealed class FeedbackMetricsService : IDisposable
{
    private readonly Counter<int> _messagesCopied;
    private readonly Counter<int> _messagesDeleted;
    private readonly Counter<int> _messagesEdited;
    private readonly Counter<int> _messagesForwarded;
    private readonly Meter _meter;

    public FeedbackMetricsService(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(@"FeedbackBot");

        _messagesCopied = _meter.CreateCounter<int>(@"messages_copied");
        _messagesForwarded = _meter.CreateCounter<int>(@"messages_forwarded");
        _messagesEdited = _meter.CreateCounter<int>(@"messages_edited");
        _messagesDeleted = _meter.CreateCounter<int>(@"messages_deleted");
    }

    public void Dispose() => _meter.Dispose();

    public void IncMessagesCopied(in int threadId, in long authorId) =>
        _messagesCopied.Add(
            1,
            new(@"thread_id", threadId),
            new(@"author_id", authorId));

    public void IncMessagesForwarded(in int threadId, in long authorId) =>
        _messagesForwarded.Add(
            1,
            new(@"thread_id", threadId),
            new(@"author_id", authorId));

    public void IncMessagesEdited(in int threadId, in long authorId) =>
        _messagesEdited.Add(
            1,
            new(@"thread_id", threadId),
            new(@"author_id", authorId));

    public void IncMessagesDeleted(in int threadId, in long authorId) =>
        _messagesDeleted.Add(
            1,
            new(@"thread_id", threadId),
            new(@"author_id", authorId));
}