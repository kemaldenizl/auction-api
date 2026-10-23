using Auction.Business.Auctions;
using Auction.Business.Auctions.Abstractions;
using Auction.Business.Auctions.Dtos;
using Auction.Business.Auctions.Events;
using Auction.Core.Abstractions;
using Auction.Core.Exceptions;
using Microsoft.AspNetCore.SignalR;

namespace Auction.API.Hubs;

/// <summary>
/// Canlı açık artırma hub'ı. İstemci yolu: <c>/hubs/auction</c>
///
/// Tasarım notları:
/// - Hub'da iş kuralı YOK. Teklif doğrulaması <see cref="IAuctionService"/> içinde;
///   REST ucu da aynı servisi çağırdığı için iki yol asla ayrışmaz.
/// - Yayın işini hub değil <see cref="IAuctionNotifier"/> yapar. Böylece arka plan
///   servisi (süre bitti) de aynı kanaldan yayın yapabiliyor.
/// - Kimlik şimdilik query string'den: <c>?bidderId=...&amp;bidderName=...</c>
///   JWT eklendiğinde tek değiştirilecek yer <see cref="ResolveBidder"/>.
/// </summary>
public sealed class AuctionHub : Hub<IAuctionClient>
{
    private const string BidderIdKey = "bidderId";
    private const string BidderNameKey = "bidderName";
    private const string JoinedAuctionsKey = "joinedAuctions";

    private readonly IAuctionService _auctions;
    private readonly IAuctionNotifier _notifier;
    private readonly IPresenceTracker _presence;
    private readonly ILogger<AuctionHub> _logger;

    public AuctionHub(
        IAuctionService auctions,
        IAuctionNotifier notifier,
        IPresenceTracker presence,
        ILogger<AuctionHub> logger)
    {
        _auctions = auctions;
        _notifier = notifier;
        _presence = presence;
        _logger = logger;
    }

    // -------------------------------------------------------- bağlantı yaşamı

    public override async Task OnConnectedAsync()
    {
        var bidder = ResolveBidder();

        Context.Items[BidderIdKey] = bidder.Id;
        Context.Items[BidderNameKey] = bidder.Name;

        // Kullanıcı başına grup: aynı kişinin birden çok sekmesi varsa
        // "teklifin geçildi" bildirimi hepsine gitsin.
        await Groups.AddToGroupAsync(Context.ConnectionId, AuctionGroups.ForBidder(bidder.Id));

        _logger.LogInformation(
            "Bağlantı kuruldu: {ConnectionId} ({BidderName})",
            Context.ConnectionId,
            bidder.Name);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Bağlantı kapanınca girdiği tüm odalardan düş ve sayaçları güncelle.
        foreach (var auctionId in JoinedAuctions().ToArray())
        {
            var viewerCount = await _presence.LeaveAsync(auctionId, Context.ConnectionId);
            await _notifier.ViewerCountChangedAsync(new ViewerCountChangedEvent(auctionId, viewerCount));
        }

        _logger.LogInformation("Bağlantı kapandı: {ConnectionId}", Context.ConnectionId);

        await base.OnDisconnectedAsync(exception);
    }

    // ---------------------------------------------------------------- odalar

    /// <summary>
    /// Bir açık artırma odasına katılır ve o anki durumu döner.
    ///
    /// Hub metodu değer döndürebiliyor: istemci <c>invoke</c> ile çağırıp
    /// ilk durumu alır, sonrasını olaylardan dinler. Böylece "önce REST'ten
    /// çek sonra socket'e bağlan" yarışı oluşmuyor.
    /// </summary>
    public async Task<AuctionDetailResponse> JoinAuction(Guid auctionId)
    {
        var detail = await _auctions.GetDetailAsync(auctionId, Context.ConnectionAborted);

        await Groups.AddToGroupAsync(Context.ConnectionId, AuctionGroups.ForAuction(auctionId));
        JoinedAuctions().Add(auctionId);

        var viewerCount = await _presence.JoinAsync(auctionId, Context.ConnectionId);
        await _notifier.ViewerCountChangedAsync(new ViewerCountChangedEvent(auctionId, viewerCount));

        return detail;
    }

    public async Task LeaveAuction(Guid auctionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, AuctionGroups.ForAuction(auctionId));
        JoinedAuctions().Remove(auctionId);

        var viewerCount = await _presence.LeaveAsync(auctionId, Context.ConnectionId);
        await _notifier.ViewerCountChangedAsync(new ViewerCountChangedEvent(auctionId, viewerCount));
    }

    // ---------------------------------------------------------------- teklif

    /// <summary>
    /// Teklif verir. Başarılıysa odadaki herkese <c>BidPlaced</c> gider
    /// (servis içindeki notifier yayınlar); reddedilirse yalnızca çağırana
    /// <c>BidRejected</c> döner.
    /// </summary>
    public async Task PlaceBid(Guid auctionId, decimal amount)
    {
        var bidder = CurrentBidder();

        try
        {
            await _auctions.PlaceBidAsync(auctionId, amount, bidder, Context.ConnectionAborted);
        }
        catch (AppException exception)
        {
            // Beklenen iş kuralı hatası: bağlantıyı düşürmeden çağırana bildir.
            await Clients.Caller.BidRejected(new BidRejectedEvent(
                auctionId,
                exception.Message,
                exception.ErrorCode,
                exception is BidTooLowException tooLow ? tooLow.MinimumRequired : null));
        }
    }

    /// <summary>Bağlantının kim olduğunu öğrenmek için (frontend doğrulaması).</summary>
    public BidderIdentity WhoAmI() => CurrentBidder();

    // ----------------------------------------------------------------- yardımcı

    private BidderIdentity CurrentBidder()
    {
        var id = Context.Items[BidderIdKey] as string ?? $"anon-{Context.ConnectionId}";
        var name = Context.Items[BidderNameKey] as string ?? "Misafir";
        return new BidderIdentity(id, name);
    }

    /// <summary>
    /// Kimliği bağlantı query string'inden okur.
    ///
    /// JWT eklendiğinde burası şöyle olur ve BAŞKA HİÇBİR YER değişmez:
    ///   var id = Context.UserIdentifier!;
    ///   var name = Context.User?.FindFirst("name")?.Value ?? id;
    /// </summary>
    private BidderIdentity ResolveBidder()
    {
        var request = Context.GetHttpContext()?.Request;

        var id = request?.Query["bidderId"].ToString();
        var name = request?.Query["bidderName"].ToString();

        if (string.IsNullOrWhiteSpace(id))
        {
            id = $"anon-{Context.ConnectionId}";
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = "Misafir";
        }

        return new BidderIdentity(id.Trim(), name.Trim());
    }

    /// <summary>
    /// Bu bağlantının katıldığı odalar. <see cref="HubCallerContext.Items"/>
    /// bağlantı başına sözlük; disconnect'te presence temizliği için gerekli.
    /// </summary>
    private HashSet<Guid> JoinedAuctions()
    {
        if (Context.Items.TryGetValue(JoinedAuctionsKey, out var existing) &&
            existing is HashSet<Guid> joined)
        {
            return joined;
        }

        var created = new HashSet<Guid>();
        Context.Items[JoinedAuctionsKey] = created;
        return created;
    }
}
