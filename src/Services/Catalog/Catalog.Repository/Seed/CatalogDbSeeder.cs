using Catalog.Entity.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Catalog.Repository.Seed;

/// <summary>
/// Demo verisi. Id'ler SABİT: Auction servisinin seed'i bu ürün id'lerine
/// referans verdiği için (servisler arası FK yok, sadece soft reference)
/// iki servis aynı demo verisiyle açılabiliyor.
/// </summary>
public static class CatalogDbSeeder
{
    public static readonly Guid OmegaWatchId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid StratocasterId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid JordanCardId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid DiamondRingId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    public static readonly Guid DuneFirstEditionId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    public static async Task SeedAsync(CatalogDbContext context, ILogger logger, CancellationToken cancellationToken = default)
    {
        if (await context.Products.AnyAsync(cancellationToken))
        {
            logger.LogInformation("Catalog seed atlandı: tabloda zaten veri var.");
            return;
        }

        var products = new List<Product>
        {
            new()
            {
                Id = OmegaWatchId,
                Name = "Omega Seamaster 300 (1964)",
                Description = "Orijinal kadran, servis geçmişi tam. Koleksiyoner parçası.",
                Category = "Saat",
                StartingPrice = 42_000m,
                ImageUrl = "https://placehold.co/600x400?text=Omega+Seamaster"
            },
            new()
            {
                Id = StratocasterId,
                Name = "Fender Stratocaster (1963)",
                Description = "Sunburst finish, orijinal pickup'lar. Hafif yaş izleri mevcut.",
                Category = "Müzik Enstrümanı",
                StartingPrice = 85_000m,
                ImageUrl = "https://placehold.co/600x400?text=Stratocaster+1963"
            },
            new()
            {
                Id = JordanCardId,
                Name = "Michael Jordan Rookie Card (imzalı)",
                Description = "PSA 8 derecelendirme, sertifikalı imza.",
                Category = "Koleksiyon",
                StartingPrice = 120_000m,
                ImageUrl = "https://placehold.co/600x400?text=Jordan+Rookie"
            },
            new()
            {
                Id = DiamondRingId,
                Name = "Art Deco Pırlanta Yüzük",
                Description = "1.2 karat merkez taş, platin montür, 1920'ler.",
                Category = "Mücevher",
                StartingPrice = 31_500m,
                ImageUrl = "https://placehold.co/600x400?text=Art+Deco+Ring"
            },
            new()
            {
                Id = DuneFirstEditionId,
                Name = "Dune - İlk Basım (1965)",
                Description = "Chilton Books, orijinal şömiz. Sırt hafif soluk.",
                Category = "Kitap",
                StartingPrice = 18_000m,
                ImageUrl = "https://placehold.co/600x400?text=Dune+1965"
            }
        };

        context.Products.AddRange(products);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Catalog seed tamamlandı: {Count} ürün eklendi.", products.Count);
    }
}
