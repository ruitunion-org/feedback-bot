using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RuItUnion.FeedbackBot.Data.Old.Models;

namespace RuItUnion.FeedbackBot.Data.Old;


public class OldDatabaseContext(DbContextOptions<FeedbackBotContext> options) : DbContext(options)
{
    public DbSet<Topic> Topic { get; set; } = default!;
    public DbSet<User> Users { get; set; } = default!;
    public DbSet<Reply> Replies { get; set; } = default!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Topic>().Property(x => x.Version).IsConcurrencyToken();
        modelBuilder.Entity<User>().Property(r => r.Version).IsConcurrencyToken();
        modelBuilder.Entity<Reply>().Property(r => r.Version).IsConcurrencyToken();

        modelBuilder.Entity<Topic>().HasMany(x => x.Replies).WithOne(x => x.Topic);
        modelBuilder.Entity<Topic>()
            .HasOne(x => x.User)
            .WithOne(x => x.Topic)
            .HasForeignKey<Topic>(x => x.UserId);
        modelBuilder.Entity<User>()
            .HasOne(x => x.Topic)
            .WithOne(x => x.User)
            .HasForeignKey<User>(x => x.TopicId);
    }
}