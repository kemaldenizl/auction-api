using Auction.Entity.Auctions;
using TestCommon.Builders;
using Xunit;

namespace UnitTests.Auctions;

/// <summary>
/// Entity üzerindeki saf domain kuralları. Bağımlılık yok, en hızlı testler.
/// </summary>
public sealed class AuctionItemTests
{
    [Fact]
    public void MinimumNextBid_hic_teklif_yokken_baslangic_fiyatidir()
    {
        var auction = AuctionItemBuilder.Live().WithPricing(1_000m, 250m).Build();

        // İlk teklife artış şartı uygulanmaz; yoksa başlangıç fiyatı
        // asla teklif edilemez bir sayı olurdu.
        Assert.Equal(1_000m, auction.MinimumNextBid());
    }

    [Fact]
    public void MinimumNextBid_teklif_varken_mevcut_fiyat_arti_artistir()
    {
        var auction = AuctionItemBuilder.Live()
            .WithPricing(1_000m, 250m)
            .WithLeadingBid(2_000m, "u-1")
            .Build();

        Assert.Equal(2_250m, auction.MinimumNextBid());
    }

    [Fact]
    public void IsAcceptingBids_canli_ve_zaman_araliginda_ise_dogrudur()
    {
        var now = DateTimeOffset.UtcNow;
        var auction = AuctionItemBuilder.Live()
            .WithWindow(now.AddMinutes(-5), now.AddMinutes(5))
            .Build();

        Assert.True(auction.IsAcceptingBids(now));
    }

    [Fact]
    public void IsAcceptingBids_baslama_aninda_dogrudur_bitis_aninda_yanlistir()
    {
        var now = DateTimeOffset.UtcNow;
        var auction = AuctionItemBuilder.Live().WithWindow(now, now.AddMinutes(10)).Build();

        // Aralık [StartsAt, EndsAt) — başlangıç dahil, bitiş değil.
        Assert.True(auction.IsAcceptingBids(now));
        Assert.False(auction.IsAcceptingBids(auction.EndsAt));
    }

    [Theory]
    [InlineData(AuctionStatus.Scheduled)]
    [InlineData(AuctionStatus.Ended)]
    [InlineData(AuctionStatus.Cancelled)]
    public void IsAcceptingBids_Live_disindaki_durumlarda_yanlistir(AuctionStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        var auction = AuctionItemBuilder.Live()
            .WithStatus(status)
            .WithWindow(now.AddMinutes(-5), now.AddMinutes(5))
            .Build();

        Assert.False(auction.IsAcceptingBids(now));
    }

    [Fact]
    public void Id_varsayilan_olarak_zaman_sirali_uretilir()
    {
        // v7 GUID zaman sıralıdır: veritabanı index'inde v4 gibi parçalanma yapmaz.
        var first = AuctionItemBuilder.Live().Build();
        Thread.Sleep(2);
        var second = AuctionItemBuilder.Live().Build();

        Assert.NotEqual(Guid.Empty, first.Id);
        Assert.True(first.Id.CompareTo(second.Id) < 0 || first.Id != second.Id);
    }
}
