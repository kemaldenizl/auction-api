namespace Auction.Business.Auctions.Events;

// Bu dosya SignalR'ın "sözleşmesi". Frontend tam olarak bu alan adlarını
// dinler (JSON'da camelCase olurlar), bu yüzden değiştirmek kırıcı bir değişikliktir.

/// <summary>Yeni teklif geldi -> odadaki herkese.</summary>
public sealed record BidPlacedEvent(
    Guid AuctionId,
    Guid BidId,
    decimal Amount,
    decimal MinimumNextBid,
    string BidderId,
    string BidderName,
    int BidCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset EndsAt);

/// <summary>
/// Teklifin geçildi -> SADECE önceki en yüksek teklif sahibine.
/// SignalR'da hedefli mesajlaşmanın örneği; broadcast ile karıştırılmamalı.
/// </summary>
public sealed record OutbidEvent(
    Guid AuctionId,
    string AuctionTitle,
    decimal YourBid,
    decimal NewPrice,
    string NewLeaderName,
    decimal MinimumNextBid);

/// <summary>Planlanmış açık artırma canlıya geçti -> odadaki herkese.</summary>
public sealed record AuctionStartedEvent(
    Guid AuctionId,
    string Title,
    decimal CurrentPrice,
    decimal MinimumNextBid,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt);

/// <summary>Süre doldu -> odadaki herkese. Kazanan bilgisi burada.</summary>
public sealed record AuctionEndedEvent(
    Guid AuctionId,
    string Title,
    decimal FinalPrice,
    string? WinnerId,
    string? WinnerName,
    int BidCount,
    DateTimeOffset EndedAt);

/// <summary>Açık artırma iptal edildi -> odadaki herkese.</summary>
public sealed record AuctionCancelledEvent(Guid AuctionId, string Title);

/// <summary>Odadaki izleyici sayısı değişti -> odadaki herkese.</summary>
public sealed record ViewerCountChangedEvent(Guid AuctionId, int ViewerCount);

/// <summary>Teklif reddedildi -> SADECE teklifi gönderen bağlantıya.</summary>
public sealed record BidRejectedEvent(
    Guid AuctionId,
    string Reason,
    string ErrorCode,
    decimal? MinimumRequired);

/// <summary>
/// SignalR grup adlarının tek kaynağı. Hub ve notifier aynı string'i
/// üretmek zorunda; elle yazmak sessiz hatanın en kolay yolu.
/// </summary>
public static class AuctionGroups
{
    /// <summary>Bir açık artırmanın odası.</summary>
    public static string ForAuction(Guid auctionId) => $"auction:{auctionId}";

    /// <summary>
    /// Tek bir kullanıcının tüm bağlantıları (çok sekme açmış olabilir).
    /// Auth olsaydı <c>Clients.User(id)</c> kullanırdık; auth yokken
    /// kullanıcı başına grup aynı işi görür ve Redis backplane ile de çalışır.
    /// </summary>
    public static string ForBidder(string bidderId) => $"bidder:{bidderId}";
}
