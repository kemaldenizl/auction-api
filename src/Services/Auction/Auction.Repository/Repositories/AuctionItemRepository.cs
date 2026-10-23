using Auction.Core.Abstractions;
using Auction.Entity.Auctions;
using Microsoft.EntityFrameworkCore;

namespace Auction.Repository.Repositories;

public interface IAuctionItemRepository : IRepository<AuctionItem>
{
    /// <summary>
    /// Açık artırma satırını <c>FOR UPDATE</c> ile kilitleyerek okur.
    ///
    /// Bu metot teklif akışının kalbi: iki kullanıcı aynı anda teklif verdiğinde
    /// ikisi de aynı CurrentPrice'ı okuyup ikisi de geçerli sanabilir (lost update).
    /// Satır kilidi ikinciyi birincinin commit'ine kadar bekletir, böylece ikinci
    /// istek güncel fiyatı görür ve kural doğru uygulanır.
    ///
    /// Açık bir transaction içinde çağrılmalıdır; yoksa kilit hemen serbest kalır.
    /// </summary>
    Task<AuctionItem?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bir ürüne ait henüz başlamamış açık artırmalar (takip edilen/tracked).
    /// Catalog'dan gelen ürün güncelleme/silme olaylarını işlerken kullanılıyor.
    /// </summary>
    Task<IReadOnlyList<AuctionItem>> GetScheduledByProductAsync(
        Guid productId,
        CancellationToken cancellationToken = default);

    /// <summary>Başlama saati gelmiş <see cref="AuctionStatus.Scheduled"/> kayıtlar.</summary>
    Task<IReadOnlyList<Guid>> GetDueToStartAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken = default);

    /// <summary>Bitiş saati geçmiş <see cref="AuctionStatus.Live"/> kayıtlar.</summary>
    Task<IReadOnlyList<Guid>> GetDueToEndAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken = default);
}

public sealed class AuctionItemRepository : EfRepository<AuctionItem>, IAuctionItemRepository
{
    public AuctionItemRepository(AuctionDbContext context) : base(context)
    {
    }

    public async Task<AuctionItem?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // FromSql parametreli çalışır (SQL injection yok).
        // ToListAsync kullanıyoruz: FirstOrDefaultAsync sorguyu alt sorguya sarar
        // ve FOR UPDATE'in davranışını belirsizleştirir.
        var rows = await Context.AuctionItems
            .FromSql($"""SELECT * FROM "AuctionItems" WHERE "Id" = {id} FOR UPDATE""")
            .ToListAsync(cancellationToken);

        return rows.Count > 0 ? rows[0] : null;
    }

    public async Task<IReadOnlyList<AuctionItem>> GetScheduledByProductAsync(
        Guid productId,
        CancellationToken cancellationToken = default) =>
        // AsNoTracking YOK: çağıran bu nesneleri değiştirip SaveChanges çağırıyor.
        await Context.AuctionItems
            .Where(a => a.ProductId == productId && a.Status == AuctionStatus.Scheduled)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetDueToStartAsync(
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken = default) =>
        await Context.AuctionItems
            .AsNoTracking()
            .Where(a => a.Status == AuctionStatus.Scheduled && a.StartsAt <= now)
            .OrderBy(a => a.StartsAt)
            .Take(limit)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetDueToEndAsync(
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken = default) =>
        await Context.AuctionItems
            .AsNoTracking()
            .Where(a => a.Status == AuctionStatus.Live && a.EndsAt <= now)
            .OrderBy(a => a.EndsAt)
            .Take(limit)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);
}
