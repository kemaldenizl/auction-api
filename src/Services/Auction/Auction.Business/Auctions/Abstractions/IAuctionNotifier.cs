using Auction.Business.Auctions.Events;

namespace Auction.Business.Auctions.Abstractions;

/// <summary>
/// Canlı bildirim kanalı.
///
/// Dikkat: Business katmanı SignalR'ı TANIMAZ. Burada sadece "şu olay oldu"
/// denir; bunu WebSocket'e çeviren implementasyon API katmanındadır
/// (SignalRAuctionNotifier). Faydası:
///   - iş kurallarını SignalR ayağa kaldırmadan test edebilirsin,
///   - yarın kanalı değiştirirsen (SSE, Kafka, webhook) Business'a dokunmazsın.
/// </summary>
public interface IAuctionNotifier
{
    Task BidPlacedAsync(BidPlacedEvent notification, CancellationToken cancellationToken = default);

    /// <summary>Hedefli: yalnızca teklifi geçilen kullanıcıya gider.</summary>
    Task OutbidAsync(string bidderId, OutbidEvent notification, CancellationToken cancellationToken = default);

    Task AuctionStartedAsync(AuctionStartedEvent notification, CancellationToken cancellationToken = default);

    Task AuctionEndedAsync(AuctionEndedEvent notification, CancellationToken cancellationToken = default);

    Task AuctionCancelledAsync(AuctionCancelledEvent notification, CancellationToken cancellationToken = default);

    Task ViewerCountChangedAsync(ViewerCountChangedEvent notification, CancellationToken cancellationToken = default);
}
