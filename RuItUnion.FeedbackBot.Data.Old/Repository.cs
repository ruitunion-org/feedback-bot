using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace RuItUnion.FeedbackBot.Data.Old;

internal interface IRepository
{
    void Create<T>(T entity) where T : class, IEntity;
    bool TryRead<T>(long id, [NotNullWhen(true)] out T? entity) where T : class, IEntity;
    bool TryUpdate<T>(T entity) where T : class, IEntity;
    void Delete<T>(long id) where T : class, IEntity;
}

public class Repository(ILogger<Repository> logger, string connectionString) : IRepository
{
    public void Create<T>(T entity) where T : class, IEntity
    {
        using DatabaseContext db = new(connectionString);
        db.Set<T>().Add(entity);
        db.SaveChanges();
    }

    public bool TryRead<T>(long id, [NotNullWhen(true)] out T? entity) where T : class, IEntity
    {
        using DatabaseContext db = new(connectionString);
        entity = db.Set<T>().FirstOrDefault(x => x.Id == id);
        return entity is not null;
    }

    public bool TryUpdate<T>(T entity) where T : class, IEntity
    {
        try
        {
            using DatabaseContext db = new(connectionString);
            T? existing = db.Set<T>().Find(entity.Id);
            if (existing is null)
            {
                return false;
            }

            db.Entry(existing).CurrentValues.SetValues(entity);
            db.SaveChanges();
        }
        catch (DbUpdateConcurrencyException e)
        {
            logger.LogError(e, "Concurrency violation during updating {entity} with id = {id}", typeof(T).Name,
                entity.Id);
            return false;
        }

        return true;
    }

    public void Delete<T>(long id) where T : class, IEntity
    {
        using DatabaseContext db = new(connectionString);
        db.Set<T>().Where(x => x.Id == id).ExecuteDelete();
    }

    public class DatabaseContext(string connectionString) : DbContext
    {
        public DbSet<Topic> Topic { get; set; } = default!;
        public DbSet<User> Users { get; set; } = default!;
        public DbSet<Reply> Replies { get; set; } = default!;

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseNpgsql(connectionString);

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
}