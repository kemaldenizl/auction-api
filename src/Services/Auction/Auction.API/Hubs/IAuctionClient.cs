using Auction.Business.Auctions.Events;

namespace Auction.API.Hubs;

/// <summary>
/// Sunucudan istemciye gidebilecek çağrılar.
///
/// <c>Hub&lt;IAuctionClient&gt;</c> ile birlikte kullanıldığında avantajı:
/// olay adını string yazmıyoruz, derleyici kontrol ediyor.
/// Buradaki METOT ADLARI frontend'in dinlediği adlardır:
/// <c>connection.on("BidPlaced", ...)</c>
/// </summary>
public interface IAuctionClient
{
    /// <summary>Odaya yeni teklif düştü.</summary>
    Task BidPlaced(BidPlacedEvent notification);

    /// <summary>Senin teklifin geçildi (yalnızca ilgili kullanıcıya gider).</summary>
    Task Outbid(OutbidEvent notification);

    /// <summary>Açık artırma canlıya geçti.</summary>
    Task AuctionStarted(AuctionStartedEvent notification);

    /// <summary>Açık artırma bitti, kazanan belli.</summary>
    Task AuctionEnded(AuctionEndedEvent notification);

    /// <summary>Açık artırma iptal edildi.</summary>
    Task AuctionCancelled(AuctionCancelledEvent notification);

    /// <summary>Odadaki izleyici sayısı değişti.</summary>
    Task ViewerCountChanged(ViewerCountChangedEvent notification);

    /// <summary>Teklifin reddedildi (yalnızca teklifi gönderen bağlantıya gider).</summary>
    Task BidRejected(BidRejectedEvent notification);
}
