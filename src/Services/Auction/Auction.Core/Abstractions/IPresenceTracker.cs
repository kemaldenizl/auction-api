namespace Auction.Core.Abstractions;

/// <summary>
/// "Bu açık artırmayı kaç kişi izliyor?" sayacı.
/// Soyut tutuluyor çünkü tek instance'ta bellek yeter, ölçeklenince
/// (docker compose --scale) Redis gerekir. Hub hangisi olduğunu bilmez.
/// </summary>
public interface IPresenceTracker
{
    /// <summary>Bağlantıyı odaya ekler ve odadaki güncel izleyici sayısını döner.</summary>
    Task<int> JoinAsync(Guid auctionId, string connectionId, CancellationToken cancellationToken = default);

    /// <summary>Bağlantıyı odadan çıkarır ve odadaki güncel izleyici sayısını döner.</summary>
    Task<int> LeaveAsync(Guid auctionId, string connectionId, CancellationToken cancellationToken = default);

    Task<int> CountAsync(Guid auctionId, CancellationToken cancellationToken = default);
}
