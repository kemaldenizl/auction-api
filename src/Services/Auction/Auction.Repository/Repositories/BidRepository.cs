using Auction.Core.Abstractions;
using Auction.Entity.Auctions;
using Microsoft.EntityFrameworkCore;

namespace Auction.Repository.Repositories;

public interface IBidRepository : IRepository<Bid>
{
    /// <summary>Bir açık artırmanın son tekliflerini (yeniden eskiye) döner.</summary>
    Task<IReadOnlyList<Bid>> GetRecentAsync(Guid auctionItemId, int take, CancellationToken cancellationToken = default);
}

public sealed class BidRepository : EfRepository<Bid>, IBidRepository
{
    public BidRepository(AuctionDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<Bid>> GetRecentAsync(
        Guid auctionItemId,
        int take,
        CancellationToken cancellationToken = default) =>
        await Context.Bids
            .AsNoTracking()
            .Where(b => b.AuctionItemId == auctionItemId)
            .OrderByDescending(b => b.CreatedAt)
            .ThenByDescending(b => b.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
}
