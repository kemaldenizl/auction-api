using Catalog.Core.Abstractions;
using Catalog.Entity.Common;
using Catalog.Entity.Products;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Repository;

public class CatalogDbContext : DbContext, IUnitOfWork
{
    public CatalogDbContext(DbContextOptions<CatalogDbContext> options) : base(options)
    {
    }

    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configurations/ altındaki IEntityTypeConfiguration sınıflarını otomatik uygular.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly);

        // MassTransit transactional outbox/inbox tabloları (InboxState, OutboxMessage,
        // OutboxState). Olay yayınlama ile veri değişikliğinin aynı transaction'da
        // olabilmesi için mesajların bu veritabanında durması gerekiyor.
        modelBuilder.AddTransactionalOutboxEntities();

        base.OnModelCreating(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampAuditFields();
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>UpdatedAt'i elle set etmeyi unutmamak için tek noktada basıyoruz.</summary>
    private void StampAuditFields()
    {
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }
    }
}
