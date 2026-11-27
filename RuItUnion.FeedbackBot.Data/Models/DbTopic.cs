using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;

namespace RuItUnion.FeedbackBot.Data.Models;

public class DbTopic : IEntityTypeConfiguration<DbTopic>, IEquatable<DbTopic>
{
    public int Id { get; init; }
    public required int ThreadId { get; set; }
    public required long UserChatId { get; init; }
    public long? AssigneeId { get; set; }
    public DateTime LockedFrom { get; set; } = DateTime.MinValue;
    public long? InterceptCandidateId { get; set; }
    public int? InterceptMessageId { get; set; }
    public IList<long> InterceptVotes { get; set; } = [];
    public long? LastLeavedUserId { get; set; }
    public bool IsQueued { get; set; }
    public bool IsOpen { get; set; } = true;
    public DateTime Created { get; init; } = DateTime.UtcNow;

    [MaxLength(128)]
    public string Header { get; set; } = string.Empty;

    public ImmutableDictionary<string, object?> Tags
    {
        get => TagsRaw.Deserialize<ImmutableDictionary<string, object?>>()
               ?? ImmutableDictionary<string, object?>.Empty;
        set => TagsRaw = JsonSerializer.SerializeToDocument(value);
    }

    public JsonDocument TagsRaw { get; set; } = JsonDocument.Parse("{}");
    public uint Version { get; init; }
    public DbUser? InterceptCandidate { get; init; }
    public DbUser Assignee { get; init; } = null!;
    public DbUser LastLeavedUser { get; init; } = null!;
    public DbUser User { get; init; } = null!;
    public IList<DbReply> Replies { get; init; } = null!;

    public void Configure(EntityTypeBuilder<DbTopic> entity)
    {
        entity.HasKey(x => x.Id);

        entity.Property(x => x.ThreadId);
        entity.Property(x => x.UserChatId);
        entity.Property(x => x.IsOpen);
        entity.Property(x => x.LockedFrom).HasDefaultValue(DateTime.MinValue);
        entity.Property(x => x.IsQueued).HasDefaultValue(false);
        entity.Property(x => x.Version).IsRowVersion();

        entity.Property(x => x.InterceptVotes).HasDefaultValue(new List<long>());

        entity.HasIndex(x => x.UserChatId).IsUnique();
        entity.HasIndex(x => x.ThreadId).IsUnique();
        entity.HasIndex(x => x.IsQueued).IsUnique(false).HasMethod("HASH");

        entity.Property(x => x.Header).IsRequired().HasMaxLength(256).IsUnicode();

        entity.Ignore(x => x.Tags);

        entity.Property(x => x.TagsRaw).HasColumnName(nameof(Tags)).HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb");
        entity.HasIndex(x => x.TagsRaw).HasMethod("GIN");

        entity.HasOne(x => x.InterceptCandidate).WithMany().HasForeignKey(x => x.InterceptCandidateId)
            .IsRequired(false);
        entity.HasOne(x => x.LastLeavedUser).WithMany().HasForeignKey(x => x.LastLeavedUserId).IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
        entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserChatId).IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(x => x.Assignee).WithMany().HasForeignKey(x => x.AssigneeId).IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
        entity.HasMany(x => x.Replies).WithOne(x => x.Topic).HasForeignKey(x => x.ChatThreadId).IsRequired()
            .OnDelete(DeleteBehavior.Cascade).HasPrincipalKey(x => x.ThreadId);
    }

    public bool Equals(DbTopic? other) => other is not null && (ReferenceEquals(this, other) || Id == other.Id);

    public void ResetIntercept(in DateTime now)
    {
        InterceptMessageId = null;
        InterceptCandidateId = null;
        InterceptVotes.Clear();
        LockedFrom = now;
    }

    public override bool Equals(object? obj) =>
        obj is not null
        && (ReferenceEquals(this, obj)
            || (obj.GetType() == GetType() && Equals((DbTopic)obj)));

    public override int GetHashCode() => Id;

    public override string ToString() =>
        $"{(IsOpen ? "\ud83d\udfe9" : "\ud83d\udfe5")}\t{User?.ToString() ?? UserChatId.ToString(@"D", CultureInfo.InvariantCulture)}";
}