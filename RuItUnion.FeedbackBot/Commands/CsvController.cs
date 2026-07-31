using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Csv;
using Microsoft.FeatureManagement.Mvc;
using RuItUnion.FeedbackBot.Data.Models;
using TgBotFrame.Commands;
using TgBotFrame.Commands.Attributes;
using TgBotFrame.Commands.Authorization.Attributes;

namespace RuItUnion.FeedbackBot.Commands;

[CommandController("ThreadInternal")]
[Localizable(false)]
[FeatureGate("CsvReports")]
public class CsvController(FeedbackBotContext dataContext, ITelegramBotClient botClient) : CommandControllerBase
{
    [Command(nameof(TopicCsv))]
    [Restricted("admin")]
    public Task TopicCsv() => TopicCsv(DateOnly.MinValue, DateOnly.MaxValue);


    [Command(nameof(TopicCsv))]
    [Restricted("admin")]
    public Task TopicCsv(DateOnly start) => TopicCsv(start, DateOnly.MaxValue);

    [Command(nameof(TopicCsv))]
    [Restricted("admin")]
    public async Task TopicCsv(DateOnly start, DateOnly end)
    {
        DateTime startDate = start.ToDateTime(new(00, 00), DateTimeKind.Utc);
        DateTime endDate = end.ToDateTime(new(23, 59), DateTimeKind.Utc);

        DbTopic[] topics = await dataContext.Topics
            .AsNoTracking()
            .Include(x => x.User)
            .Where(x => x.Created > startDate && x.Created <= endDate)
            .OrderBy(x => x.Id)
            .ToArrayAsync().ConfigureAwait(false);
        Stream stream = new MemoryStream();
        await using ConfiguredAsyncDisposable stream1 = stream.ConfigureAwait(false);
        TextWriter textWriter = new StreamWriter(stream, Encoding.UTF8, -1, false);
        await using ConfiguredAsyncDisposable writer = textWriter.ConfigureAwait(false);

        string[] tagNames = topics.SelectMany(x => x.Tags.Keys).Distinct(StringComparer.Ordinal).ToArray();
        List<string> headersList =
            new(5 + tagNames.Length)
            {
                "Task ID",
                "Task Status",
                "Username",
                "Task Description",
                "Task Start Date",
            };
        headersList.AddRange(tagNames);
        string[] headers = headersList.ToArray();
        List<string> rowBuffer = new(5 + tagNames.Length);

        await CsvWriter.WriteAsync(textWriter,
            headers
            , topics.Select(topic =>
            {
                rowBuffer.Clear();
                rowBuffer.Add(topic.Id.ToString(@"D"));
                rowBuffer.Add(topic.IsOpen ? "Opened" : "Closed");
                rowBuffer.Add(topic.User.ToString());
                rowBuffer.Add(topic.Header);
                rowBuffer.Add(topic.Created.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

                rowBuffer.AddRange(tagNames.GroupJoin(topic.Tags, x => x, x => x.Key,
                    (_, x) => x.FirstOrDefault().Value?.ToString() ?? string.Empty));

                return rowBuffer.ToArray();
            }), ';').ConfigureAwait(false);
        await textWriter.FlushAsync(CancellationToken).ConfigureAwait(false);
        stream.Position = 0L;

        InputFileStream inputFile = InputFile.FromStream(stream, "report.csv");
        await botClient.SendDocument(Context.GetChatId()!, inputFile, messageThreadId: Context.GetThreadId(),
            replyParameters: Context.GetMessageId() is not null
                ? new ReplyParameters
                {
                    ChatId = Context.GetChatId(),
                    MessageId = Context.GetMessageId()!.Value,
                    AllowSendingWithoutReply = true,
                }
                : null,
            cancellationToken: CancellationToken).ConfigureAwait(true);
    }
}