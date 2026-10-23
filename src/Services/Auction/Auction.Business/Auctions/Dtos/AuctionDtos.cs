using System.ComponentModel.DataAnnotations;
using Auction.Core.Paging;
using Auction.Entity.Auctions;

namespace Auction.Business.Auctions.Dtos;

/// <summary>Liste kartı için yeterli alanlar.</summary>
public sealed record AuctionSummaryResponse(
    Guid Id,
    Guid ProductId,
    string Title,
    string? ImageUrl,
    decimal StartingPrice,
    decimal CurrentPrice,
    decimal MinimumIncrement,
    decimal MinimumNextBid,
    int BidCount,
    string? HighestBidderName,
    AuctionStatus Status,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt);

/// <summary>Detay ekranı: özet + açıklama + son teklifler.</summary>
public sealed record AuctionDetailResponse(
    Guid Id,
    Guid ProductId,
    string Title,
    string? Description,
    string? ImageUrl,
    decimal StartingPrice,
    decimal CurrentPrice,
    decimal MinimumIncrement,
    decimal MinimumNextBid,
    int BidCount,
    string? HighestBidderId,
    string? HighestBidderName,
    AuctionStatus Status,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    IReadOnlyList<BidResponse> RecentBids);

public sealed record BidResponse(
    Guid Id,
    Guid AuctionId,
    decimal Amount,
    string BidderId,
    string BidderName,
    DateTimeOffset CreatedAt);

/// <summary>Teklif başarıyla kaydedildiğinde dönen sonuç.</summary>
public sealed record PlaceBidResult(BidResponse Bid, AuctionSummaryResponse Auction);

/// <summary>
/// Teklif sahibinin kimliği. Şu an auth yok:
/// REST'te gövdeden, SignalR'da bağlantı query string'inden geliyor.
/// JWT eklendiğinde ikisi de token'dan doldurulacak (tek değişiklik noktası).
/// </summary>
public sealed record BidderIdentity(string Id, string Name);

public sealed record CreateAuctionRequest
{
    /// <summary>Catalog servisindeki ürünün id'si. Doğrulanır ve bilgileri kopyalanır.</summary>
    [Required]
    public Guid ProductId { get; init; }

    /// <summary>Boş bırakılırsa ürünün katalogdaki başlangıç fiyatı kullanılır.</summary>
    [Range(0.01, 100_000_000)]
    public decimal? StartingPrice { get; init; }

    /// <summary>Her yeni teklifin mevcut fiyatı en az bu kadar aşması gerekir.</summary>
    [Range(0.01, 1_000_000)]
    public decimal MinimumIncrement { get; init; } = 100m;

    /// <summary>Boş bırakılırsa hemen başlar (durum doğrudan Live olur).</summary>
    public DateTimeOffset? StartsAt { get; init; }

    /// <summary>Açık artırmanın süresi (dakika). En fazla 7 gün.</summary>
    [Range(1, 10_080)]
    public int DurationMinutes { get; init; } = 60;
}

public sealed record PlaceBidRequest
{
    [Range(0.01, 1_000_000_000, ErrorMessage = "Teklif tutarı 0'dan büyük olmalı.")]
    public decimal Amount { get; init; }

    [Required(ErrorMessage = "Teklif sahibi id'si zorunlu.")]
    [StringLength(100, MinimumLength = 1)]
    public string BidderId { get; init; } = string.Empty;

    [Required(ErrorMessage = "Teklif sahibi adı zorunlu.")]
    [StringLength(150, MinimumLength = 1)]
    public string BidderName { get; init; } = string.Empty;

    public BidderIdentity ToIdentity() => new(BidderId, BidderName);
}

/// <summary>
/// Catalog'dan gelen ürün bilgisinin açık artırmaya kopyalanacak hali.
/// <c>ProductUpdated</c> olayını tüketirken kullanılıyor; Business katmanının
/// mesaj tipine değil kendi şekline bağlı kalması için ayrı tutuldu.
/// </summary>
public sealed record ProductSnapshot(
    Guid ProductId,
    string Name,
    string? Description,
    string? ImageUrl);

public sealed record AuctionListQuery
{
    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = PageRequest.DefaultPageSize;

    /// <summary>Scheduled | Live | Ended | Cancelled</summary>
    public AuctionStatus? Status { get; init; }

    public string? Search { get; init; }

    public Guid? ProductId { get; init; }

    public PageRequest ToPageRequest() => new() { Page = Page, PageSize = PageSize };
}
