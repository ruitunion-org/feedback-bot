using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TgBotFrame.Commands.Authorization.Models;

namespace RuItUnion.FeedbackBot.Data.Models;

public class DbTopic : IEntityTypeConfiguration<DbTopic>, IEquatable<DbTopic>
{
    public int Id { get; init; }
    public required int ThreadId { get; set; }
    public required long UserChatId { get; init; }
    public bool IsOpen { get; set; } = true;
    public uint Version { get; init; }

    public DbUser User { get; init; } = null!;
    public IList<DbReply> Replies { get; init; } = null!;

    public void Configure(EntityTypeBuilder<DbTopic> entity)
    {
        entity.HasKey(x => x.Id);

        entity.Property(x => x.ThreadId);
        entity.Property(x => x.UserChatId);
        entity.Property(x => x.IsOpen);
        entity.Property(x => x.Version).IsRowVersion();

        entity.HasIndex(x => x.UserChatId).IsUnique();
        entity.HasIndex(x => x.ThreadId).IsUnique();

        entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserChatId).IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasMany(x => x.Replies).WithOne(x => x.Topic).HasForeignKey(x => x.ChatThreadId).IsRequired()
            .OnDelete(DeleteBehavior.Cascade).HasPrincipalKey(x => x.ThreadId);
    }

    public bool Equals(DbTopic? other) => other is not null && (ReferenceEquals(this, other) || Id == other.Id);

    public override bool Equals(object? obj) =>
        obj is not null
        && (ReferenceEquals(this, obj)
            || (obj.GetType() == GetType() && Equals((DbTopic)obj)));

    public override int GetHashCode() => Id;

    public override string ToString() =>
        $"{(IsOpen ? "\ud83d\udfe9" : "\ud83d\udfe5")}\t{User?.ToString() ?? UserChatId.ToString(@"D", CultureInfo.InvariantCulture)}";
}