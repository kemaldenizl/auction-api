using Auction.Business.Auctions.Abstractions;
using Auction.Business.Auctions.Events;

namespace TestCommon.Fakes;

/// <summary>
/// SignalR yerine yayınlanan olayları listeye yazan fake.
///
/// Mock kütüphanesi yerine elle yazılmış bir kayıt tutucu tercih ettim:
/// "hangi olay, kime, hangi sırayla gitti" sorularını okunabilir şekilde
/// doğrulamayı sağlıyor.
/// </summary>
public sealed class RecordingAuctionNotifier : IAuctionNotifier
{
    public List<BidPlacedEvent> BidPlaced { get; } = [];

    /// <summary>Hedefli bildirimler: (alıcının bidderId'si, olay).</summary>
    public List<(string BidderId, OutbidEvent Event)> Outbid { get; } = [];

    public List<AuctionStartedEvent> Started { get; } = [];

    public List<AuctionEndedEvent> Ended { get; } = [];

    public List<AuctionCancelledEvent> Cancelled { get; } = [];

    public List<ViewerCountChangedEvent> ViewerCounts { get; } = [];

    public Task BidPlacedAsync(BidPlacedEvent notification, CancellationToken cancellationToken = default)
    {
        BidPlaced.Add(notification);
        return Task.CompletedTask;
    }

    public Task OutbidAsync(string bidderId, OutbidEvent notification, CancellationToken cancellationToken = default)
    {
        Outbid.Add((bidderId, notification));
        return Task.CompletedTask;
    }

    public Task AuctionStartedAsync(AuctionStartedEvent notification, CancellationToken cancellationToken = default)
    {
        Started.Add(notification);
        return Task.CompletedTask;
    }

    public Task AuctionEndedAsync(AuctionEndedEvent notification, CancellationToken cancellationToken = default)
    {
        Ended.Add(notification);
        return Task.CompletedTask;
    }

    public Task AuctionCancelledAsync(AuctionCancelledEvent notification, CancellationToken cancellationToken = default)
    {
        Cancelled.Add(notification);
        return Task.CompletedTask;
    }

    public Task ViewerCountChangedAsync(ViewerCountChangedEvent notification, CancellationToken cancellationToken = default)
    {
        ViewerCounts.Add(notification);
        return Task.CompletedTask;
    }
}
