using Auction.Entity.Common;

namespace Auction.Entity.Auctions;

/// <summary>
/// Bir açık artırma kaydı (lot).
///
/// Sınıf adının "Auction" değil "AuctionItem" olmasının sebebi teknik:
/// proje/namespace zaten <c>Auction.*</c> olduğu için <c>Auction.Entity.Auction</c>
/// tip-namespace çakışmasına yol açıyor.
/// </summary>
public class AuctionItem : BaseEntity
{
    /// <summary>
    /// Catalog servisindeki ürünün id'si. Bilinçli olarak FK DEĞİL:
    /// mikroservis sınırını geçen foreign key olmaz, sadece soft reference olur.
    /// </summary>
    public Guid ProductId { get; set; }

    /// <summary>
    /// Ürün bilgisinin açık artırma anındaki kopyası (snapshot).
    /// Catalog'daki ürün sonradan değişse/silinse bile burada ne satıldığı sabit kalır.
    /// </summary>
    public required string Title { get; set; }

    public string? Description { get; set; }

    public string? ImageUrl { get; set; }

    public decimal StartingPrice { get; set; }

    /// <summary>Yeni teklifin mevcut fiyatı en az bu kadar aşması gerekir.</summary>
    public decimal MinimumIncrement { get; set; }

    /// <summary>Hiç teklif gelmediyse <see cref="StartingPrice"/>'a eşittir.</summary>
    public decimal CurrentPrice { get; set; }

    public int BidCount { get; set; }

    /// <summary>En yüksek teklifi veren. Auth yok; istemciden gelen kimlik.</summary>
    public string? HighestBidderId { get; set; }

    public string? HighestBidderName { get; set; }

    public DateTimeOffset StartsAt { get; set; }

    public DateTimeOffset EndsAt { get; set; }

    public AuctionStatus Status { get; set; } = AuctionStatus.Scheduled;

    public ICollection<Bid> Bids { get; set; } = new List<Bid>();

    /// <summary>Şu an teklif kabul ediyor mu?</summary>
    public bool IsAcceptingBids(DateTimeOffset now) =>
        Status == AuctionStatus.Live && now >= StartsAt && now < EndsAt;

    /// <summary>Geçerli bir sonraki teklifin alt sınırı.</summary>
    public decimal MinimumNextBid() =>
        BidCount == 0 ? StartingPrice : CurrentPrice + MinimumIncrement;
}
