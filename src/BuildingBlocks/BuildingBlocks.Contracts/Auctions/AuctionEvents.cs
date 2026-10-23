namespace BuildingBlocks.Contracts.Auctions;

/// <summary>
/// Bir açık artırma sona erdi (süre doldu).
///
/// Catalog servisi bunu dinleyip satılan ürünü pasife çeker.
/// İleride e-posta/fatura/ödeme gibi tüketiciler eklendiğinde Auction
/// servisinde hiçbir değişiklik gerekmez — olayın tüketicisini bilmemesi
/// mesajlaşmanın asıl kazancı.
/// </summary>
public sealed record AuctionEnded(
    Guid AuctionId,
    Guid ProductId,
    string Title,
    decimal FinalPrice,
    string? WinnerId,
    string? WinnerName,
    int BidCount,
    DateTimeOffset OccurredAt);
