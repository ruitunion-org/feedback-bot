using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace RuItUnion.FeedbackBot.Data.Models;

public class DbReply : IEntityTypeConfiguration<DbReply>, IEquatable<DbReply>
{
    public required int ChatMessageId { get; init; }
    public required int ChatThreadId { get; init; }
    public required int UserMessageId { get; init; }

    public uint Version { get; init; }

    public DbTopic Topic { get; init; } = null!;

    public void Configure(EntityTypeBuilder<DbReply> entity)
    {
        entity.HasKey(x => x.ChatMessageId);
        entity.Property(x => x.ChatMessageId).ValueGeneratedNever();

        entity.Property(x => x.Version).IsRowVersion();

        entity.HasOne(x => x.Topic).WithMany(x => x.Replies).HasForeignKey(x => x.ChatThreadId).IsRequired()
            .OnDelete(DeleteBehavior.Cascade).HasPrincipalKey(x => x.ThreadId);
    }

    public bool Equals(DbReply? other) =>
        other is not null && (ReferenceEquals(this, other) || ChatMessageId == other.ChatMessageId);

    public override bool Equals(object? obj) =>
        obj is not null
        && (ReferenceEquals(this, obj)
            || (obj.GetType() == GetType() && Equals((DbReply)obj)));

    public override int GetHashCode() => ChatMessageId;

    public override string ToString() => $"{ChatMessageId:D}";
}