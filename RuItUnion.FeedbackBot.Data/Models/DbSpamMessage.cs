using System.Text.Json;

namespace RuItUnion.FeedbackBot.Data.Models;

public class DbSpamMessage : IEntityTypeConfiguration<DbSpamMessage>, IEquatable<DbSpamMessage>
{
    public required long Id { get; init; }
    public DbUser User { get; init; } = null!;
    public string Reason { get; init; } = string.Empty;
    public required JsonDocument Update { get; init; }
    public uint Version { get; init; }
    public void Configure(EntityTypeBuilder<DbSpamMessage> entity)
    {
        entity.HasKey(x => x.Id);

        entity.Property(x => x.Reason).HasMaxLength(128);

        entity.Property(x => x.Version).IsRowVersion();

        entity.HasOne(x => x.User).WithOne().HasForeignKey<DbSpamMessage>(x => x.Id);
    }

    public bool Equals(DbSpamMessage? other) => other is not null && Id == other.Id;
    public override int GetHashCode() => Id.GetHashCode();

    public override string ToString() => Id.ToString("N");

    public override bool Equals(object? obj) => Equals(obj as DbSpamMessage);
}