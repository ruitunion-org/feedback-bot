using RuItUnion.FeedbackBot.Data.Models;
using TgBotFrame.Commands.Authorization.Interfaces;

namespace RuItUnion.FeedbackBot.Data;

public class FeedbackBotContext(DbContextOptions<FeedbackBotContext> options) : DbContext(options), IAuthorizationData
{
    public DbSet<DbReply> Replies { get; init; } = null!;
    public DbSet<DbTopic> Topics { get; init; } = null!;

    Task IAuthorizationData.SaveChangesAsync(CancellationToken cancellationToken) =>
        base.SaveChangesAsync(cancellationToken);

    public DbSet<DbRole> Roles { get; init; } = null!;
    public DbSet<DbRoleMember> RoleMembers { get; init; } = null!;
    public DbSet<DbBan> Bans { get; init; } = null!;
    public DbSet<DbUser> Users { get; init; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("FeedbackBot");

        IAuthorizationData.OnModelCreating(modelBuilder);
        modelBuilder.Entity<DbRole>(builder => { builder.Property<uint>("Version").IsRowVersion(); });
        modelBuilder.Entity<DbRoleMember>(builder => { builder.Property<uint>("Version").IsRowVersion(); });
        modelBuilder.Entity<DbBan>(builder => { builder.Property<uint>("Version").IsRowVersion(); });
        modelBuilder.Entity<DbUser>(builder => { builder.Property<uint>("Version").IsRowVersion(); });

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FeedbackBotContext).Assembly);
    }
}