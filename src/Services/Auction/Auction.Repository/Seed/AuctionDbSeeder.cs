using Auction.Entity.Auctions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Auction.Repository.Seed;

/// <summary>
/// Demo verisi: biri hemen canlı, biri ileride başlayan iki açık artırma.
/// ProductId'ler Catalog servisinin seed'indeki sabit id'ler
/// (servisler arası FK yok, sadece soft reference) — böylece
/// <c>docker compose up</c> sonrası teklif vermeyi hemen deneyebiliyorsun.
/// </summary>
public static class AuctionDbSeeder
{
    private static readonly Guid OmegaWatchProductId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid StratocasterProductId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static readonly Guid LiveAuctionId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    public static readonly Guid ScheduledAuctionId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    public static async Task SeedAsync(AuctionDbContext context, ILogger logger, CancellationToken cancellationToken = default)
    {
        if (await context.AuctionItems.AnyAsync(cancellationToken))
        {
            logger.LogInformation("Auction seed atlandı: tabloda zaten veri var.");
            return;
        }

        var now = DateTimeOffset.UtcNow;

        var auctions = new List<AuctionItem>
        {
            new()
            {
                Id = LiveAuctionId,
                ProductId = OmegaWatchProductId,
                Title = "Omega Seamaster 300 (1964)",
                Description = "Orijinal kadran, servis geçmişi tam. Koleksiyoner parçası.",
                ImageUrl = "https://placehold.co/600x400?text=Omega+Seamaster",
                StartingPrice = 42_000m,
                CurrentPrice = 42_000m,
                MinimumIncrement = 500m,
                StartsAt = now.AddMinutes(-5),
                EndsAt = now.AddHours(4),
                Status = AuctionStatus.Live
            },
            new()
            {
                Id = ScheduledAuctionId,
                ProductId = StratocasterProductId,
                Title = "Fender Stratocaster (1963)",
                Description = "Sunburst finish, orijinal pickup'lar.",
                ImageUrl = "https://placehold.co/600x400?text=Stratocaster+1963",
                StartingPrice = 85_000m,
                CurrentPrice = 85_000m,
                MinimumIncrement = 1_000m,
                // 2 dakika sonra başlar: lifecycle worker'ın Scheduled -> Live
                // geçişini ve AuctionStarted olayını canlı izleyebilmek için.
                StartsAt = now.AddMinutes(2),
                EndsAt = now.AddHours(6),
                Status = AuctionStatus.Scheduled
            }
        };

        context.AuctionItems.AddRange(auctions);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Auction seed tamamlandı. Canlı açık artırma: {LiveId}, planlanmış: {ScheduledId}",
            LiveAuctionId,
            ScheduledAuctionId);
    }
}
