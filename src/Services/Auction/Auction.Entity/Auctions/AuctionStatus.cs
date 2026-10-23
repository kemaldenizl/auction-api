namespace Auction.Entity.Auctions;

/// <summary>
/// Açık artırmanın yaşam döngüsü.
/// Geçişleri arka plan servisi (AuctionLifecycleWorker) saate göre yapar.
/// </summary>
public enum AuctionStatus
{
    /// <summary>Başlama saati gelmedi; teklif kabul edilmez.</summary>
    Scheduled = 0,

    /// <summary>Canlı; teklif kabul edilir.</summary>
    Live = 1,

    /// <summary>Süresi doldu; kazanan belirlendi.</summary>
    Ended = 2,

    /// <summary>Elle iptal edildi.</summary>
    Cancelled = 3
}
