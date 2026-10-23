using Auction.Business.Auctions.Dtos;
using Auction.Core.Paging;

namespace Auction.Business.Auctions;

/// <summary>
/// Açık artırma iş kuralları. REST controller'ı ve SignalR hub'ı AYNI bu servisi
/// çağırır; bu yüzden teklif hangi yoldan gelirse gelsin kurallar ve yayınlanan
/// canlı olaylar birebir aynı olur.
/// </summary>
public interface IAuctionService
{
    Task<PagedResult<AuctionSummaryResponse>> GetListAsync(
        AuctionListQuery query,
        CancellationToken cancellationToken = default);

    Task<AuctionDetailResponse> GetDetailAsync(Guid auctionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BidResponse>> GetBidsAsync(
        Guid auctionId,
        int take,
        CancellationToken cancellationToken = default);

    Task<AuctionDetailResponse> CreateAsync(
        CreateAuctionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Teklif verir. Satır kilidi altında çalışır, başarılı olursa
    /// commit SONRASI canlı olayları yayınlar.
    /// </summary>
    Task<PlaceBidResult> PlaceBidAsync(
        Guid auctionId,
        decimal amount,
        BidderIdentity bidder,
        CancellationToken cancellationToken = default);

    /// <summary>Demo kolaylığı: planlanmış açık artırmayı hemen canlıya alır.</summary>
    Task<AuctionDetailResponse> StartNowAsync(Guid auctionId, CancellationToken cancellationToken = default);

    Task<AuctionDetailResponse> CancelAsync(Guid auctionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Arka plan servisi için: saati gelmiş açık artırmayı canlıya alır.
    /// Geçişi bu instance yaptıysa <c>true</c> döner (olayı yalnızca o yayınlar).
    /// </summary>
    Task<bool> TryStartDueAsync(Guid auctionId, CancellationToken cancellationToken = default);

    /// <summary>Arka plan servisi için: süresi dolmuş açık artırmayı bitirir.</summary>
    Task<bool> TryEndDueAsync(Guid auctionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Catalog'da ürün güncellendiğinde <b>henüz başlamamış</b> açık artırmaların
    /// ürün kopyasını tazeler. Canlı ve bitmiş olanlara dokunulmaz — satışa
    /// çıkmış bir ilanın başlığı altından değişmemeli.
    ///
    /// Mesaj tüketicisinden çağrıldığı için istisna fırlatmaz.
    /// </summary>
    /// <returns>Güncellenen açık artırma sayısı.</returns>
    Task<int> RefreshScheduledSnapshotsAsync(
        ProductSnapshot snapshot,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Catalog'dan ürün silindiğinde o ürüne ait <b>henüz başlamamış</b>
    /// açık artırmaları iptal eder ve odalara <c>AuctionCancelled</c> yayınlar.
    /// Canlı/bitmiş olanlara dokunulmaz.
    ///
    /// Mesaj tüketicisinden çağrıldığı için istisna fırlatmaz.
    /// </summary>
    /// <returns>İptal edilen açık artırma sayısı.</returns>
    Task<int> CancelScheduledForProductAsync(
        Guid productId,
        CancellationToken cancellationToken = default);
}
