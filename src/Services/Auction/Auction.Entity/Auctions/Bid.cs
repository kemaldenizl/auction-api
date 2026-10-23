using Auction.Entity.Common;

namespace Auction.Entity.Auctions;

/// <summary>
/// Verilmiş bir teklif. Append-only: teklifler güncellenmez/silinmez,
/// böylece açık artırmanın denetim izi bozulmaz.
/// </summary>
public class Bid : BaseEntity
{
    public Guid AuctionItemId { get; set; }

    public decimal Amount { get; set; }

    /// <summary>
    /// Teklif sahibinin kimliği. Şu an auth yok, istemci kendi id'sini gönderiyor.
    /// JWT eklendiğinde buraya token'daki subject (<c>sub</c>) yazılacak.
    /// </summary>
    public required string BidderId { get; set; }

    public required string BidderName { get; set; }

    public AuctionItem? AuctionItem { get; set; }
}
