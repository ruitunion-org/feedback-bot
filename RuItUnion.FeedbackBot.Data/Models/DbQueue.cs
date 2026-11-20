namespace RuItUnion.FeedbackBot.Data.Models;

public class DbQueue : IEntityTypeConfiguration<DbQueue>, IEquatable<DbQueue>
{
    public required long UserId { get; init; }
    public bool InQueue { get; set; } = true;
    public DateTime LastAssignmentTime { get; set; } = DateTime.MinValue;
    public TimeSpan AssignmentPenalty { get; set; } = TimeSpan.Zero;
    public DbUser User { get; init; } = null!;
    public IList<DbMentor> Mentors { get; init; } = null!;
    public uint Version { get; init; }

    public void Configure(EntityTypeBuilder<DbQueue> entity)
    {
        entity.HasKey(x => x.UserId);

        entity.HasQueryFilter(x => x.InQueue);
        entity.HasIndex(x => x.InQueue).IsUnique(false).IsDescending(true).HasMethod("HASH");

        entity.Property(x => x.LastAssignmentTime).HasDefaultValue(DateTime.MinValue);
        entity.HasIndex(x => x.LastAssignmentTime).IsUnique(false).IsDescending(false);
        entity.Property(x => x.AssignmentPenalty).HasDefaultValue(TimeSpan.Zero);

        entity.Property(x => x.Version).IsRowVersion();

        entity.HasOne(x => x.User).WithOne().HasForeignKey<DbQueue>(x => x.UserId);
        entity.HasMany(x => x.Mentors).WithOne().HasPrincipalKey(x => x.UserId).HasForeignKey(x => x.NoviceId)
            .IsRequired(false).IsConstrained(false);
    }

    public bool Equals(DbQueue? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return UserId == other.UserId;
    }

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((DbQueue)obj);
    }

    public override int GetHashCode() => UserId.GetHashCode();
}