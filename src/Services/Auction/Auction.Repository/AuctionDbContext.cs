using Auction.Core.Abstractions;
using Auction.Entity.Auctions;
using Auction.Entity.Common;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Auction.Repository;

public class AuctionDbContext : DbContext, IUnitOfWork
{
    public AuctionDbContext(DbContextOptions<AuctionDbContext> options) : base(options)
    {
    }

    public DbSet<AuctionItem> AuctionItems => Set<AuctionItem>();

    public DbSet<Bid> Bids => Set<Bid>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuctionDbContext).Assembly);

        // MassTransit transactional outbox/inbox tabloları.
        // AuctionEnded olayı, açık artırmayı bitiren transaction'ın İÇİNDE
        // outbox'a yazılır; böylece "bitti ama olay gitmedi" durumu oluşamaz.
        modelBuilder.AddTransactionalOutboxEntities();

        base.OnModelCreating(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }

    public Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        var strategy = Database.CreateExecutionStrategy();

        return strategy.ExecuteAsync(async () =>
        {
            // Yeniden deneme olursa önceki denemenin takip edilen değişiklikleri
            // temizlenmeli; yoksa aynı kayıt iki kez eklenmeye çalışılır.
            ChangeTracker.Clear();

            await using var transaction = await Database.BeginTransactionAsync(cancellationToken);

            var result = await operation(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }
}
