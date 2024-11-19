using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RuItUnion.FeedbackBot.Data;

public class FeedbackBotContextFactory : IDesignTimeDbContextFactory<FeedbackBotContext>
{
    public FeedbackBotContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<FeedbackBotContext> builder = new();
        string connectionString = args.Length != 0
            ? string.Join(' ', args)
            : @"User ID=postgres;Password=postgres;Host=localhost;Port=5432;";
        Console.WriteLine(@"connectionString = " + connectionString);
        return new(builder
            .UseNpgsql(connectionString)
            .Options);
    }
}