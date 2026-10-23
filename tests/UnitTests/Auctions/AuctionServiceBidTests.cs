using Auction.Business.Auctions.Dtos;
using Auction.Core.Exceptions;
using Auction.Entity.Auctions;
using NSubstitute;
using TestCommon.Builders;
using Xunit;

namespace UnitTests.Auctions;

/// <summary>
/// Teklif verme iş kuralları. Projenin en kritik davranışı burada.
/// </summary>
public sealed class AuctionServiceBidTests
{
    private static readonly BidderIdentity Ayse = new("u-ayse", "Ayşe");
    private static readonly BidderIdentity Mehmet = new("u-mehmet", "Mehmet");

    [Fact]
    public async Task PlaceBid_ilk_teklif_baslangic_fiyatinin_altindaysa_reddedilir()
    {
        var harness = new AuctionServiceHarness();
        var auction = harness.Existing(AuctionItemBuilder.Live().WithPricing(1_000m, 100m).Build());

        var exception = await Assert.ThrowsAsync<BidTooLowException>(
            () => harness.Service.PlaceBidAsync(auction.Id, 999m, Ayse));

        Assert.Equal("bid_too_low", exception.ErrorCode);
        // Hiç teklif yoksa alt sınır başlangıç fiyatıdır (artış şartı eklenmez).
        Assert.Equal(1_000m, exception.MinimumRequired);
        Assert.Empty(harness.Notifier.BidPlaced);
    }

    [Fact]
    public async Task PlaceBid_ilk_teklif_baslangic_fiyatina_esitse_kabul_edilir()
    {
        var harness = new AuctionServiceHarness();
        var auction = harness.Existing(AuctionItemBuilder.Live().WithPricing(1_000m, 100m).Build());

        var result = await harness.Service.PlaceBidAsync(auction.Id, 1_000m, Ayse);

        Assert.Equal(1_000m, result.Bid.Amount);
        Assert.Equal(1_000m, auction.CurrentPrice);
        Assert.Equal(1, auction.BidCount);
    }

    [Fact]
    public async Task PlaceBid_sonraki_teklif_mevcut_fiyat_arti_artis_kadar_olmali()
    {
        var harness = new AuctionServiceHarness();
        var auction = harness.Existing(
            AuctionItemBuilder.Live()
                .WithPricing(1_000m, 250m)
                .WithLeadingBid(1_000m, Mehmet.Id)
                .Build());

        var tooLow = await Assert.ThrowsAsync<BidTooLowException>(
            () => harness.Service.PlaceBidAsync(auction.Id, 1_249m, Ayse));

        Assert.Equal(1_250m, tooLow.MinimumRequired);

        // Tam alt sınır kabul edilmeli.
        var accepted = await harness.Service.PlaceBidAsync(auction.Id, 1_250m, Ayse);
        Assert.Equal(1_250m, accepted.Bid.Amount);
    }

    [Fact]
    public async Task PlaceBid_en_yuksek_teklif_zaten_kendisindeyse_reddedilir()
    {
        var harness = new AuctionServiceHarness();
        var auction = harness.Existing(
            AuctionItemBuilder.Live().WithLeadingBid(2_000m, Ayse.Id, Ayse.Name).Build());

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(
            () => harness.Service.PlaceBidAsync(auction.Id, 5_000m, Ayse));

        Assert.Equal("already_highest_bidder", exception.ErrorCode);
        Assert.Equal(2_000m, auction.CurrentPrice);
    }

    [Theory]
    [InlineData(AuctionStatus.Cancelled, "auction_cancelled")]
    [InlineData(AuctionStatus.Ended, "auction_ended")]
    public async Task PlaceBid_uygun_olmayan_durumlarda_reddedilir(AuctionStatus status, string expectedCode)
    {
        var harness = new AuctionServiceHarness();
        var auction = harness.Existing(AuctionItemBuilder.Live().WithStatus(status).Build());

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(
            () => harness.Service.PlaceBidAsync(auction.Id, 999_999m, Ayse));

        Assert.Equal(expectedCode, exception.ErrorCode);
    }

    [Fact]
    public async Task PlaceBid_baslama_saati_gelmemisse_reddedilir()
    {
        var harness = new AuctionServiceHarness();
        var auction = harness.Existing(AuctionItemBuilder.Scheduled().Build());

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(
            () => harness.Service.PlaceBidAsync(auction.Id, 999_999m, Ayse));

        Assert.Equal("auction_not_started", exception.ErrorCode);
    }

    [Fact]
    public async Task PlaceBid_durum_Live_olsa_bile_sure_dolmussa_reddedilir()
    {
        var harness = new AuctionServiceHarness();
        // Arka plan servisi henüz kapatmamış: durum Live ama bitiş geçmiş.
        var auction = harness.Existing(
            AuctionItemBuilder.Live()
                .WithWindow(DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow.AddMinutes(-1))
                .Build());

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(
            () => harness.Service.PlaceBidAsync(auction.Id, 999_999m, Ayse));

        Assert.Equal("auction_ended", exception.ErrorCode);
    }

    [Fact]
    public async Task PlaceBid_saati_gelmis_Scheduled_acik_artirmayi_canliya_alir_ve_teklifi_kabul_eder()
    {
        var harness = new AuctionServiceHarness();
        // Worker daha sırayı almamış: StartsAt geçmiş ama durum hâlâ Scheduled.
        var auction = harness.Existing(
            AuctionItemBuilder.Live()
                .WithStatus(AuctionStatus.Scheduled)
                .WithWindow(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1))
                .WithPricing(500m, 50m)
                .Build());

        var result = await harness.Service.PlaceBidAsync(auction.Id, 500m, Ayse);

        Assert.Equal(AuctionStatus.Live, auction.Status);
        Assert.Equal(500m, result.Bid.Amount);
    }

    [Fact]
    public async Task PlaceBid_gecerli_teklif_acik_artirmayi_gunceller_ve_teklifi_kaydeder()
    {
        var harness = new AuctionServiceHarness();
        var auction = harness.Existing(
            AuctionItemBuilder.Live().WithPricing(1_000m, 100m).Build());

        await harness.Service.PlaceBidAsync(auction.Id, 1_500m, Ayse);

        Assert.Equal(1_500m, auction.CurrentPrice);
        Assert.Equal(Ayse.Id, auction.HighestBidderId);
        Assert.Equal(Ayse.Name, auction.HighestBidderName);
        Assert.Equal(1, auction.BidCount);

        await harness.Bids.Received(1).AddAsync(
            Arg.Is<Bid>(b => b.Amount == 1_500m && b.BidderId == Ayse.Id && b.AuctionItemId == auction.Id),
            Arg.Any<CancellationToken>());

        // Tüm değişiklik tek transaction içinde kaydedilmeli.
        Assert.Equal(1, harness.UnitOfWork.TransactionCallCount);
        Assert.Equal(1, harness.UnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task PlaceBid_odaya_BidPlaced_yayinlar()
    {
        var harness = new AuctionServiceHarness();
        var auction = harness.Existing(AuctionItemBuilder.Live().WithPricing(1_000m, 200m).Build());

        await harness.Service.PlaceBidAsync(auction.Id, 1_000m, Ayse);

        var published = Assert.Single(harness.Notifier.BidPlaced);
        Assert.Equal(auction.Id, published.AuctionId);
        Assert.Equal(1_000m, published.Amount);
        Assert.Equal(Ayse.Name, published.BidderName);
        Assert.Equal(1, published.BidCount);
        // İstemci bir sonraki geçerli teklifi bilsin: 1000 + 200
        Assert.Equal(1_200m, published.MinimumNextBid);
    }

    [Fact]
    public async Task PlaceBid_Outbid_bildirimini_YALNIZCA_onceki_liderin_kimligine_gonderir()
    {
        var harness = new AuctionServiceHarness();
        var auction = harness.Existing(
            AuctionItemBuilder.Live()
                .WithPricing(1_000m, 100m)
                .WithLeadingBid(1_000m, Mehmet.Id, Mehmet.Name)
                .Build());

        await harness.Service.PlaceBidAsync(auction.Id, 1_100m, Ayse);

        var (recipientId, outbid) = Assert.Single(harness.Notifier.Outbid);
        Assert.Equal(Mehmet.Id, recipientId);
        Assert.Equal(1_000m, outbid.YourBid);
        Assert.Equal(1_100m, outbid.NewPrice);
        Assert.Equal(Ayse.Name, outbid.NewLeaderName);
    }

    [Fact]
    public async Task PlaceBid_ilk_teklifte_Outbid_gondermez()
    {
        var harness = new AuctionServiceHarness();
        var auction = harness.Existing(AuctionItemBuilder.Live().WithPricing(1_000m, 100m).Build());

        await harness.Service.PlaceBidAsync(auction.Id, 1_000m, Ayse);

        Assert.Empty(harness.Notifier.Outbid);
    }

    [Fact]
    public async Task PlaceBid_teklif_tutarini_iki_ondalige_yuvarlar()
    {
        var harness = new AuctionServiceHarness();
        var auction = harness.Existing(AuctionItemBuilder.Live().WithPricing(1_000m, 100m).Build());

        var result = await harness.Service.PlaceBidAsync(auction.Id, 1_234.56789m, Ayse);

        Assert.Equal(1_234.56m, result.Bid.Amount);
    }

    [Fact]
    public async Task PlaceBid_acik_artirma_yoksa_NotFound_firlatir()
    {
        var harness = new AuctionServiceHarness();
        var missingId = Guid.CreateVersion7();
        harness.NoSuchAuction(missingId);

        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => harness.Service.PlaceBidAsync(missingId, 1_000m, Ayse));

        Assert.Equal("not_found", exception.ErrorCode);
    }

    [Theory]
    [InlineData("", "Ayşe")]
    [InlineData("   ", "Ayşe")]
    [InlineData("u-1", "")]
    public async Task PlaceBid_eksik_teklif_sahibi_bilgisi_reddedilir(string bidderId, string bidderName)
    {
        var harness = new AuctionServiceHarness();
        var auction = harness.Existing(AuctionItemBuilder.Live().Build());

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(
            () => harness.Service.PlaceBidAsync(auction.Id, 1_000m, new BidderIdentity(bidderId, bidderName)));

        Assert.Equal("missing_bidder", exception.ErrorCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task PlaceBid_pozitif_olmayan_tutar_reddedilir(decimal amount)
    {
        var harness = new AuctionServiceHarness();
        var auction = harness.Existing(AuctionItemBuilder.Live().Build());

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(
            () => harness.Service.PlaceBidAsync(auction.Id, amount, Ayse));

        Assert.Equal("invalid_amount", exception.ErrorCode);
    }

    [Fact]
    public async Task PlaceBid_reddedilen_teklif_hicbir_sey_degistirmez_ve_yayin_yapmaz()
    {
        var harness = new AuctionServiceHarness();
        var auction = harness.Existing(
            AuctionItemBuilder.Live().WithPricing(1_000m, 100m).Build());

        await Assert.ThrowsAsync<BidTooLowException>(
            () => harness.Service.PlaceBidAsync(auction.Id, 1m, Ayse));

        Assert.Equal(0, auction.BidCount);
        Assert.Equal(1_000m, auction.CurrentPrice);
        Assert.Null(auction.HighestBidderId);
        Assert.Equal(0, harness.UnitOfWork.SaveChangesCallCount);
        Assert.Empty(harness.Notifier.BidPlaced);
        Assert.Empty(harness.Events.Published);
    }
}
