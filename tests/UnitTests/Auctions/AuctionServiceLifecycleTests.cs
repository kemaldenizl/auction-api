using Auction.Business.Auctions.Dtos;
using Auction.Business.Catalog;
using Auction.Core.Exceptions;
using Auction.Entity.Auctions;
using BuildingBlocks.Contracts.Auctions;
using TestCommon.Builders;
using Xunit;

namespace UnitTests.Auctions;

/// <summary>
/// Açık artırma oluşturma, durum geçişleri ve Catalog olaylarına verilen tepkiler.
/// </summary>
public sealed class AuctionServiceLifecycleTests
{
    private static CatalogProduct Product(Guid id, bool isActive = true) =>
        new(id, "Omega Seamaster", "açıklama", "https://example.com/a.png", 42_000m, isActive);

    // ------------------------------------------------------------- oluşturma

    [Fact]
    public async Task Create_urun_katalogda_yoksa_NotFound_firlatir()
    {
        var harness = new AuctionServiceHarness();
        var productId = Guid.CreateVersion7();
        harness.CatalogMissing(productId);

        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.Service.CreateAsync(new CreateAuctionRequest { ProductId = productId }));
    }

    [Fact]
    public async Task Create_pasif_urun_icin_acik_artirma_acilmaz()
    {
        var harness = new AuctionServiceHarness();
        var productId = Guid.CreateVersion7();
        harness.CatalogHas(Product(productId, isActive: false));

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(
            () => harness.Service.CreateAsync(new CreateAuctionRequest { ProductId = productId }));

        Assert.Equal("product_inactive", exception.ErrorCode);
    }

    [Fact]
    public async Task Create_urun_bilgisini_kopyalar_snapshot()
    {
        var harness = new AuctionServiceHarness();
        var productId = Guid.CreateVersion7();
        harness.CatalogHas(Product(productId));

        var detail = await harness.Service.CreateAsync(new CreateAuctionRequest
        {
            ProductId = productId,
            MinimumIncrement = 500m,
            DurationMinutes = 90
        });

        // Başlık/açıklama/görsel katalogdan kopyalanmış olmalı.
        Assert.Equal("Omega Seamaster", detail.Title);
        Assert.Equal("açıklama", detail.Description);
        Assert.Equal("https://example.com/a.png", detail.ImageUrl);
        // StartingPrice verilmediyse katalogdaki fiyat kullanılır.
        Assert.Equal(42_000m, detail.StartingPrice);
        Assert.Equal(42_000m, detail.CurrentPrice);
        Assert.Equal(productId, detail.ProductId);
        Assert.Equal(90, (int)Math.Round((detail.EndsAt - detail.StartsAt).TotalMinutes));
    }

    [Fact]
    public async Task Create_baslangic_fiyati_verilirse_katalogdakini_ezer()
    {
        var harness = new AuctionServiceHarness();
        var productId = Guid.CreateVersion7();
        harness.CatalogHas(Product(productId));

        var detail = await harness.Service.CreateAsync(new CreateAuctionRequest
        {
            ProductId = productId,
            StartingPrice = 10_000m,
            MinimumIncrement = 100m
        });

        Assert.Equal(10_000m, detail.StartingPrice);
    }

    [Fact]
    public async Task Create_StartsAt_bos_ise_hemen_canli_baslar()
    {
        var harness = new AuctionServiceHarness();
        var productId = Guid.CreateVersion7();
        harness.CatalogHas(Product(productId));

        var detail = await harness.Service.CreateAsync(new CreateAuctionRequest { ProductId = productId });

        Assert.Equal(AuctionStatus.Live, detail.Status);
    }

    [Fact]
    public async Task Create_StartsAt_ileride_ise_Scheduled_olur()
    {
        var harness = new AuctionServiceHarness();
        var productId = Guid.CreateVersion7();
        harness.CatalogHas(Product(productId));

        var detail = await harness.Service.CreateAsync(new CreateAuctionRequest
        {
            ProductId = productId,
            StartsAt = DateTimeOffset.UtcNow.AddHours(2)
        });

        Assert.Equal(AuctionStatus.Scheduled, detail.Status);
    }

    [Fact]
    public async Task Create_gecmis_bir_baslama_zamani_reddedilir()
    {
        var harness = new AuctionServiceHarness();
        var productId = Guid.CreateVersion7();
        harness.CatalogHas(Product(productId));

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(
            () => harness.Service.CreateAsync(new CreateAuctionRequest
            {
                ProductId = productId,
                StartsAt = DateTimeOffset.UtcNow.AddHours(-1)
            }));

        Assert.Equal("invalid_start_time", exception.ErrorCode);
    }

    // --------------------------------------------------------- durum geçişleri

    [Fact]
    public async Task TryEndDue_suresi_dolmus_canli_acik_artirmayi_bitirir()
    {
        var harness = new AuctionServiceHarness();
        var auction = harness.Existing(
            AuctionItemBuilder.Live()
                .WithWindow(DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow.AddMinutes(-1))
                .WithLeadingBid(5_000m, "u-kazanan", "Kazanan", bidCount: 3)
                .Build());

        var result = await harness.Service.TryEndDueAsync(auction.Id);

        Assert.True(result);
        Assert.Equal(AuctionStatus.Ended, auction.Status);

        // SignalR: odadaki herkese
        var signalR = Assert.Single(harness.Notifier.Ended);
        Assert.Equal("Kazanan", signalR.WinnerName);
        Assert.Equal(5_000m, signalR.FinalPrice);
    }

    [Fact]
    public async Task TryEndDue_servis_disina_AuctionEnded_mesaji_yayinlar()
    {
        var harness = new AuctionServiceHarness();
        var productId = Guid.CreateVersion7();
        var auction = harness.Existing(
            AuctionItemBuilder.Live()
                .WithProductId(productId)
                .WithWindow(DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow.AddMinutes(-1))
                .WithLeadingBid(7_500m, "u-kazanan", "Kazanan", bidCount: 2)
                .Build());

        await harness.Service.TryEndDueAsync(auction.Id);

        // Catalog'un dinlediği mesaj. ProductId taşımak zorunda:
        // Catalog hangi ürünü pasife çekeceğini bundan öğreniyor.
        var message = harness.Events.Single<AuctionEnded>();
        Assert.Equal(auction.Id, message.AuctionId);
        Assert.Equal(productId, message.ProductId);
        Assert.Equal(7_500m, message.FinalPrice);
        Assert.Equal("u-kazanan", message.WinnerId);
        Assert.Equal(2, message.BidCount);
    }

    [Fact]
    public async Task TryEndDue_sure_dolmadiysa_hicbir_sey_yapmaz()
    {
        var harness = new AuctionServiceHarness();
        var auction = harness.Existing(
            AuctionItemBuilder.Live()
                .WithWindow(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1))
                .Build());

        var result = await harness.Service.TryEndDueAsync(auction.Id);

        Assert.False(result);
        Assert.Equal(AuctionStatus.Live, auction.Status);
        Assert.Empty(harness.Notifier.Ended);
        Assert.Empty(harness.Events.Published);
    }

    [Fact]
    public async Task TryEndDue_zaten_bitmis_acik_artirmada_ikinci_kez_olay_yayinlamaz()
    {
        // Çoklu instance senaryosu: ikinci replica kilidi aldığında
        // durumu Ended görür ve çekilir — çift bildirim olmaz.
        var harness = new AuctionServiceHarness();
        var auction = harness.Existing(AuctionItemBuilder.Ended().Build());

        var result = await harness.Service.TryEndDueAsync(auction.Id);

        Assert.False(result);
        Assert.Empty(harness.Notifier.Ended);
        Assert.Empty(harness.Events.Published);
    }

    [Fact]
    public async Task TryStartDue_Scheduled_acik_artirmayi_canliya_alir()
    {
        var harness = new AuctionServiceHarness();
        var auction = harness.Existing(AuctionItemBuilder.Scheduled().Build());

        var result = await harness.Service.TryStartDueAsync(auction.Id);

        Assert.True(result);
        Assert.Equal(AuctionStatus.Live, auction.Status);
        Assert.Single(harness.Notifier.Started);
        // Başlangıç servis dışını ilgilendirmiyor: mesaj yayınlanmaz.
        Assert.Empty(harness.Events.Published);
    }

    [Fact]
    public async Task TryStartDue_zaten_canli_ise_tekrar_yayin_yapmaz()
    {
        var harness = new AuctionServiceHarness();
        var auction = harness.Existing(AuctionItemBuilder.Live().Build());

        var result = await harness.Service.TryStartDueAsync(auction.Id);

        Assert.False(result);
        Assert.Empty(harness.Notifier.Started);
    }

    [Fact]
    public async Task Cancel_bitmis_acik_artirma_iptal_edilemez()
    {
        var harness = new AuctionServiceHarness();
        var auction = harness.Existing(AuctionItemBuilder.Ended().Build());

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(
            () => harness.Service.CancelAsync(auction.Id));

        Assert.Equal("invalid_status_transition", exception.ErrorCode);
    }

    [Fact]
    public async Task StartNow_iptal_edilmis_acik_artirma_baslatilamaz()
    {
        var harness = new AuctionServiceHarness();
        var auction = harness.Existing(AuctionItemBuilder.Cancelled().Build());

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(
            () => harness.Service.StartNowAsync(auction.Id));

        Assert.Equal("invalid_status_transition", exception.ErrorCode);
    }

    [Fact]
    public async Task StartNow_zaten_canli_ise_idempotenttir()
    {
        var harness = new AuctionServiceHarness();
        var auction = harness.Existing(AuctionItemBuilder.Live().Build());

        var detail = await harness.Service.StartNowAsync(auction.Id);

        Assert.Equal(AuctionStatus.Live, detail.Status);
        Assert.Empty(harness.Notifier.Started);
    }

    // ------------------------------------------------- Catalog olaylarına tepki

    [Fact]
    public async Task ProductUpdated_sadece_baslamamis_acik_artirmalarin_kopyasini_tazeler()
    {
        var harness = new AuctionServiceHarness();
        var productId = Guid.CreateVersion7();

        // Repository zaten yalnızca Scheduled olanları döndürüyor;
        // burada güncellemenin gerçekten uygulandığını doğruluyoruz.
        var scheduled = AuctionItemBuilder.Scheduled()
            .WithProductId(productId)
            .WithTitle("Eski Başlık")
            .Build();

        harness.ScheduledForProduct(productId, scheduled);

        var count = await harness.Service.RefreshScheduledSnapshotsAsync(
            new ProductSnapshot(productId, "Yeni Başlık", "yeni açıklama", "https://example.com/yeni.png"));

        Assert.Equal(1, count);
        Assert.Equal("Yeni Başlık", scheduled.Title);
        Assert.Equal("yeni açıklama", scheduled.Description);
        Assert.Equal("https://example.com/yeni.png", scheduled.ImageUrl);
        Assert.Equal(1, harness.UnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task ProductUpdated_ilgili_acik_artirma_yoksa_kaydetmeye_calismaz()
    {
        var harness = new AuctionServiceHarness();
        var productId = Guid.CreateVersion7();
        harness.ScheduledForProduct(productId);

        var count = await harness.Service.RefreshScheduledSnapshotsAsync(
            new ProductSnapshot(productId, "Yeni", null, null));

        Assert.Equal(0, count);
        Assert.Equal(0, harness.UnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task ProductDeleted_baslamamis_acik_artirmalari_iptal_eder_ve_odaya_bildirir()
    {
        var harness = new AuctionServiceHarness();
        var productId = Guid.CreateVersion7();

        var first = AuctionItemBuilder.Scheduled().WithProductId(productId).WithTitle("Birinci").Build();
        var second = AuctionItemBuilder.Scheduled().WithProductId(productId).WithTitle("İkinci").Build();

        harness.ScheduledForProduct(productId, first, second);

        var count = await harness.Service.CancelScheduledForProductAsync(productId);

        Assert.Equal(2, count);
        Assert.Equal(AuctionStatus.Cancelled, first.Status);
        Assert.Equal(AuctionStatus.Cancelled, second.Status);

        // Her oda kendi bildirimini almalı.
        Assert.Equal(2, harness.Notifier.Cancelled.Count);
        Assert.Contains(harness.Notifier.Cancelled, c => c.AuctionId == first.Id);
        Assert.Contains(harness.Notifier.Cancelled, c => c.AuctionId == second.Id);
    }
}
