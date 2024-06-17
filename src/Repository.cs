using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bot;

public interface IRepository
{
    void Create<T>(T entity) where T : class, IEntity;
    bool TryRead<T>(long id, [NotNullWhen(true)] out T? entity) where T : class, IEntity;
    bool TryUpdate<T>(T entity) where T : class, IEntity;
    void Delete<T>(long id) where T : class, IEntity;
}

public class Repository(ILogger<Repository> _logger, IOptions<AppOptions> _options) : IRepository
{
    public class DatabaseContext(IOptions<AppOptions> _options) : DbContext
    {
        public DbSet<Topic> Topic { get; set; } = default!;
        public DbSet<User> Users { get; set; } = default!;
        public DbSet<Reply> Replies { get; set; } = default!;

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseNpgsql(_options.Value.DbConnectionString);
        }

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

    public void Create<T>(T entity) where T : class, IEntity
    {
        using var db = new DatabaseContext(_options);
        db.Set<T>().Add(entity);
        db.SaveChanges();
    }

    public bool TryRead<T>(long id, [NotNullWhen(true)] out T? entity) where T : class, IEntity
    {
        using var db = new DatabaseContext(_options);
        entity = db.Set<T>().FirstOrDefault(x => x.Id == id);
        return entity is not null;
    }

    public bool TryUpdate<T>(T entity) where T : class, IEntity
    {
        try
        {
            using var db = new DatabaseContext(_options);
            var existing = db.Set<T>().Find(entity.Id);
            if (existing is null) return false;
            db.Entry(existing).CurrentValues.SetValues(entity);
            db.SaveChanges();
        }
        catch (DbUpdateConcurrencyException e)
        {
            _logger.LogError(e, "Concurrency violation during updating {entity} with id = {id}", typeof(T).Name, entity.Id);
            return false;
        }

        return true;
    }

    public void Delete<T>(long id) where T : class, IEntity
    {
        using var db = new DatabaseContext(_options);
        db.Set<T>().Where(x => x.Id == id).ExecuteDelete();
    }
}
