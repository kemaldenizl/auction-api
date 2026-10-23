using Auction.API.Hubs;
using Auction.Business.Auctions.Abstractions;
using Auction.Business.Auctions.Events;
using Microsoft.AspNetCore.SignalR;

namespace Auction.API.Infrastructure;

/// <summary>
/// <see cref="IAuctionNotifier"/>'ın SignalR implementasyonu: "kim ne alacak"
/// kararının tek verildiği yer.
///
/// <see cref="IHubContext{THub, TClient}"/> sayesinde hub'ın dışından da
/// (örneğin arka plan servisinden, REST controller'ından) yayın yapılabiliyor.
///
/// Redis backplane açıkken bu çağrılar yalnızca bu sunucudaki bağlantılara değil,
/// tüm replica'lardaki bağlantılara ulaşır — kod aynı kalır.
/// </summary>
public sealed class SignalRAuctionNotifier : IAuctionNotifier
{
    private readonly IHubContext<AuctionHub, IAuctionClient> _hub;

    public SignalRAuctionNotifier(IHubContext<AuctionHub, IAuctionClient> hub)
    {
        _hub = hub;
    }

    public Task BidPlacedAsync(BidPlacedEvent notification, CancellationToken cancellationToken = default) =>
        Room(notification.AuctionId).BidPlaced(notification);

    /// <summary>Hedefli: sadece o kullanıcının bağlantıları.</summary>
    public Task OutbidAsync(string bidderId, OutbidEvent notification, CancellationToken cancellationToken = default) =>
        _hub.Clients.Group(AuctionGroups.ForBidder(bidderId)).Outbid(notification);

    public Task AuctionStartedAsync(AuctionStartedEvent notification, CancellationToken cancellationToken = default) =>
        Room(notification.AuctionId).AuctionStarted(notification);

    public Task AuctionEndedAsync(AuctionEndedEvent notification, CancellationToken cancellationToken = default) =>
        Room(notification.AuctionId).AuctionEnded(notification);

    public Task AuctionCancelledAsync(AuctionCancelledEvent notification, CancellationToken cancellationToken = default) =>
        Room(notification.AuctionId).AuctionCancelled(notification);

    public Task ViewerCountChangedAsync(ViewerCountChangedEvent notification, CancellationToken cancellationToken = default) =>
        Room(notification.AuctionId).ViewerCountChanged(notification);

    private IAuctionClient Room(Guid auctionId) =>
        _hub.Clients.Group(AuctionGroups.ForAuction(auctionId));
}
