namespace RuItUnion.FeedbackBot.Data.Models;

public class DbMentor : IEntityTypeConfiguration<DbMentor>, IEquatable<DbMentor>
{
    public required long NoviceId { get; init; }
    public required long MentorId { get; init; }
    public bool IsActive { get; set; } = true;

    public uint Version { get; init; }

    public DbUser Novice { get; init; } = null!;
    public DbUser Mentor { get; init; } = null!;

    public void Configure(EntityTypeBuilder<DbMentor> entity)
    {
        entity.HasKey(x => new { x.NoviceId, x.MentorId });
        entity.HasIndex(x => x.NoviceId).IsUnique(false).HasMethod("HASH");

        entity.HasQueryFilter(x => x.IsActive);
        entity.HasIndex(x => x.IsActive).IsDescending(true).HasMethod("HASH");

        entity.Property(x => x.Version).IsRowVersion();

        entity.HasOne(x => x.Novice).WithMany().HasForeignKey(x => x.NoviceId).IsRequired();
        entity.HasOne(x => x.Mentor).WithMany().HasForeignKey(x => x.MentorId).IsRequired();
    }

    public bool Equals(DbMentor? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return NoviceId == other.NoviceId && MentorId == other.MentorId;
    }

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((DbMentor)obj);
    }

    public override int GetHashCode() => HashCode.Combine(NoviceId, MentorId);
}