using Auction.Business.Auctions.Dtos;
using Auction.Core.Exceptions;
using Auction.Entity.Auctions;
using BuildingBlocks.Contracts.Auctions;
using Microsoft.EntityFrameworkCore;
using TestCommon.Containers;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// Projenin en önemli testleri. Burada doğrulanan şey birim testle
/// doğrulanamaz: gerçek Postgres satır kilidinin eşzamanlı teklifleri
/// doğru sıralayıp sıralamadığı.
///
/// Kilit olmasaydı iki istek aynı <c>CurrentPrice</c>'ı okur, ikisi de
/// kuralı geçer ve biri diğerinin teklifini sessizce ezer (lost update).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BidConcurrencyTests
{
    private readonly PostgresFixture _postgres;

    public BidConcurrencyTests(PostgresFixture postgres) => _postgres = postgres;

    private async Task<(AuctionTestHost Host, Guid AuctionId)> SetupLiveAuctionAsync(
        decimal startingPrice = 1_000m,
        decimal minimumIncrement = 100m)
    {
        var host = await AuctionTestHost.CreateAsync(_postgres.AuctionConnectionString);

        var auctionId = Guid.CreateVersion7();

        await host.InScopeAsync(async context =>
        {
            context.AuctionItems.Add(new AuctionItem
            {
                Id = auctionId,
                ProductId = Guid.CreateVersion7(),
                Title = "Yarış Testi",
                StartingPrice = startingPrice,
                CurrentPrice = startingPrice,
                MinimumIncrement = minimumIncrement,
                StartsAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                EndsAt = DateTimeOffset.UtcNow.AddHours(1),
                Status = AuctionStatus.Live
            });

            await context.SaveChangesAsync();
        });

        return (host, auctionId);
    }

    [Fact]
    public async Task Ayni_tutarla_gelen_eszamanli_teklifler_arasinda_yalnizca_biri_kabul_edilir()
    {
        const int bidderCount = 12;
        var (host, auctionId) = await SetupLiveAuctionAsync();
        await using var _ = host;

        // 12 kişi aynı anda tam alt sınırı (1000) teklif ediyor.
        // Kilit doğru çalışıyorsa ilki geçer, kalan 11'i artık 1100 görmek zorunda.
        var attempts = Enumerable.Range(0, bidderCount).Select(index =>
            host.InScopeAsync(async service =>
            {
                try
                {
                    await service.PlaceBidAsync(
                        auctionId,
                        1_000m,
                        new BidderIdentity($"racer-{index}", $"Yarışmacı {index}"));
                    return (Accepted: true, ErrorCode: (string?)null);
                }
                catch (AppException exception)
                {
                    return (Accepted: false, ErrorCode: exception.ErrorCode);
                }
            }));

        var results = await Task.WhenAll(attempts);

        var accepted = results.Count(r => r.Accepted);
        Assert.Equal(1, accepted);

        // Reddedilenlerin hepsi iş kuralıyla reddedilmeli; beklenmeyen hata olmamalı.
        Assert.Equal(bidderCount - 1, results.Count(r => r.ErrorCode == "bid_too_low"));

        // Veritabanı tutarlı mı: tek teklif, fiyat değişmemiş.
        var auction = await host.QueryAsync(context =>
            context.AuctionItems.AsNoTracking().SingleAsync(a => a.Id == auctionId));

        Assert.Equal(1, auction.BidCount);
        Assert.Equal(1_000m, auction.CurrentPrice);

        var bidRows = await host.QueryAsync(context =>
            context.Bids.AsNoTracking().CountAsync(b => b.AuctionItemId == auctionId));

        Assert.Equal(1, bidRows);

        // Her kabul edilen teklif için tam bir BidPlaced yayını olmalı.
        Assert.Single(host.Notifier.BidPlaced);
    }

    [Fact]
    public async Task Artan_tutarlarla_gelen_eszamanli_teklifler_tutarli_bir_zincir_olusturur()
    {
        const int bidderCount = 10;
        var (host, auctionId) = await SetupLiveAuctionAsync(startingPrice: 1_000m, minimumIncrement: 100m);
        await using var _ = host;

        // Herkes farklı (ve geçerli olabilecek) tutarlar veriyor: 1000, 1100, ... 1900.
        // Sıralama rastgele olacak; kilit sayesinde yalnızca o anki alt sınırı
        // aşanlar geçmeli ve sonuç her zaman tutarlı olmalı.
        var attempts = Enumerable.Range(0, bidderCount).Select(index =>
            host.InScopeAsync(async service =>
            {
                try
                {
                    var result = await service.PlaceBidAsync(
                        auctionId,
                        1_000m + index * 100m,
                        new BidderIdentity($"racer-{index}", $"Yarışmacı {index}"));
                    return (Accepted: true, result.Bid.Amount);
                }
                catch (AppException)
                {
                    return (Accepted: false, Amount: 0m);
                }
            }));

        var results = await Task.WhenAll(attempts);
        var acceptedAmounts = results.Where(r => r.Accepted).Select(r => r.Amount).ToList();

        Assert.NotEmpty(acceptedAmounts);

        var auction = await host.QueryAsync(context =>
            context.AuctionItems.AsNoTracking().SingleAsync(a => a.Id == auctionId));

        // Değişmezler (invariants):
        Assert.Equal(acceptedAmounts.Count, auction.BidCount);
        Assert.Equal(acceptedAmounts.Max(), auction.CurrentPrice);

        // Kaydedilen teklifler zaman sırasında artan olmalı ve her adım
        // en az minimumIncrement kadar yükselmeli.
        var storedBids = await host.QueryAsync(context => context.Bids
            .AsNoTracking()
            .Where(b => b.AuctionItemId == auctionId)
            .OrderBy(b => b.CreatedAt)
            .ThenBy(b => b.Id)
            .Select(b => b.Amount)
            .ToListAsync());

        Assert.Equal(acceptedAmounts.Count, storedBids.Count);

        for (var i = 1; i < storedBids.Count; i++)
        {
            Assert.True(
                storedBids[i] >= storedBids[i - 1] + 100m,
                $"Teklif zinciri bozuk: {storedBids[i - 1]} -> {storedBids[i]}");
        }
    }

    [Fact]
    public async Task Ayni_kullanici_eszamanli_iki_teklif_verirse_ikincisi_reddedilir()
    {
        var (host, auctionId) = await SetupLiveAuctionAsync();
        await using var _ = host;

        var bidder = new BidderIdentity("u-ayni", "Aynı Kişi");

        // "En yüksek teklif zaten sizde" kuralı kilit altında da tutmalı.
        var attempts = Enumerable.Range(0, 2).Select(_ =>
            host.InScopeAsync(async service =>
            {
                try
                {
                    await service.PlaceBidAsync(auctionId, 1_000m, bidder);
                    return (string?)null;
                }
                catch (AppException exception)
                {
                    return exception.ErrorCode;
                }
            }));

        var errors = await Task.WhenAll(attempts);

        Assert.Single(errors, error => error is null);

        var auction = await host.QueryAsync(context =>
            context.AuctionItems.AsNoTracking().SingleAsync(a => a.Id == auctionId));

        Assert.Equal(1, auction.BidCount);
    }

    [Fact]
    public async Task Eszamanli_bitirme_denemelerinde_olay_yalnizca_bir_kez_yayinlanir()
    {
        // Çoklu instance senaryosu: her replica'nın arka plan servisi aynı
        // açık artırmayı aynı anda bitirmeye çalışır. Kilit sayesinde
        // yalnızca biri geçişi yapar — kullanıcı tek bildirim alır,
        // Catalog tek mesaj görür (ürün iki kez satılmış sayılmaz).
        var host = await AuctionTestHost.CreateAsync(_postgres.AuctionConnectionString);
        await using var _ = host;

        var auctionId = Guid.CreateVersion7();

        await host.InScopeAsync(async context =>
        {
            context.AuctionItems.Add(new AuctionItem
            {
                Id = auctionId,
                ProductId = Guid.CreateVersion7(),
                Title = "Bitirme Yarışı",
                StartingPrice = 1_000m,
                CurrentPrice = 2_500m,
                MinimumIncrement = 100m,
                BidCount = 4,
                HighestBidderId = "u-kazanan",
                HighestBidderName = "Kazanan",
                StartsAt = DateTimeOffset.UtcNow.AddHours(-2),
                EndsAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                Status = AuctionStatus.Live
            });

            await context.SaveChangesAsync();
        });

        var attempts = Enumerable.Range(0, 8)
            .Select(_ => host.InScopeAsync(service => service.TryEndDueAsync(auctionId)));

        var results = await Task.WhenAll(attempts);

        Assert.Equal(1, results.Count(won => won));
        Assert.Single(host.Notifier.Ended);
        Assert.Single(host.Events.OfType<AuctionEnded>());

        var auction = await host.QueryAsync(context =>
            context.AuctionItems.AsNoTracking().SingleAsync(a => a.Id == auctionId));

        Assert.Equal(AuctionStatus.Ended, auction.Status);
    }
}
