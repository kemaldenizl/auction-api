using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Auction.Business.Auctions.Dtos;
using Auction.Business.Catalog;
using Auction.Entity.Auctions;
using NSubstitute;
using TestCommon.Containers;
using Xunit;

namespace FunctionalTests;

/// <summary>Auction API'nin HTTP yüzeyi.</summary>
[Collection(PostgresCollection.Name)]
public sealed class AuctionApiTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private AuctionApiFactory _factory = null!;
    private HttpClient _client = null!;

    public AuctionApiTests(PostgresFixture postgres) => _postgres = postgres;

    public Task InitializeAsync()
    {
        _factory = new AuctionApiFactory(_postgres.AuctionConnectionString);
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    /// <summary>Catalog servisinin o ürünü döndüreceğini söyler.</summary>
    private Guid GivenCatalogProduct(decimal startingPrice = 1_000m, bool isActive = true)
    {
        var productId = Guid.CreateVersion7();

        _factory.Catalog.GetProductAsync(productId, Arg.Any<CancellationToken>())
            .Returns(new CatalogProduct(
                productId,
                $"Ürün {productId}",
                "katalogdan gelen açıklama",
                "https://example.com/p.png",
                startingPrice,
                isActive));

        return productId;
    }

    private async Task<AuctionDetailResponse> CreateAuctionAsync(Guid productId, decimal minimumIncrement = 100m)
    {
        var response = await _client.PostAsJsonAsync("/api/auctions", new CreateAuctionRequest
        {
            ProductId = productId,
            MinimumIncrement = minimumIncrement,
            DurationMinutes = 60
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var detail = await response.Content.ReadAsync<AuctionDetailResponse>();
        Assert.NotNull(detail);
        return detail;
    }

    [Fact]
    public async Task Acik_artirma_olusturulurken_urun_bilgisi_katalogdan_kopyalanir()
    {
        var productId = GivenCatalogProduct(startingPrice: 4_200m);

        var detail = await CreateAuctionAsync(productId);

        Assert.Equal($"Ürün {productId}", detail.Title);
        Assert.Equal("katalogdan gelen açıklama", detail.Description);
        Assert.Equal(4_200m, detail.StartingPrice);
        Assert.Equal(AuctionStatus.Live, detail.Status);
        Assert.Empty(detail.RecentBids);
    }

    [Fact]
    public async Task Katalogda_olmayan_urun_icin_404_doner()
    {
        var missingProductId = Guid.CreateVersion7();
        _factory.Catalog.GetProductAsync(missingProductId, Arg.Any<CancellationToken>())
            .Returns((CatalogProduct?)null);

        var response = await _client.PostAsJsonAsync("/api/auctions", new CreateAuctionRequest
        {
            ProductId = missingProductId,
            MinimumIncrement = 100m
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Catalog_servisi_erisilemezse_502_doner()
    {
        var productId = Guid.CreateVersion7();
        _factory.Catalog.GetProductAsync(productId, Arg.Any<CancellationToken>())
            .Returns<CatalogProduct?>(_ => throw new Auction.Core.Exceptions.ExternalServiceException(
                "Catalog", "bağlantı kurulamadı"));

        var response = await _client.PostAsJsonAsync("/api/auctions", new CreateAuctionRequest
        {
            ProductId = productId,
            MinimumIncrement = 100m
        });

        // Bağımlı servisin hatası 500 değil 502 olmalı: sorun bizde değil.
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);

        var problem = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        Assert.Equal("external_service_error", problem.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task REST_ile_teklif_verilebilir_ve_acik_artirma_guncellenir()
    {
        var productId = GivenCatalogProduct(startingPrice: 1_000m);
        var auction = await CreateAuctionAsync(productId, minimumIncrement: 250m);

        var response = await _client.PostAsJsonAsync($"/api/auctions/{auction.Id}/bids", new PlaceBidRequest
        {
            Amount = 1_000m,
            BidderId = "u-ayse",
            BidderName = "Ayşe"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var result = await response.Content.ReadAsync<PlaceBidResult>();
        Assert.NotNull(result);
        Assert.Equal(1_000m, result.Bid.Amount);
        Assert.Equal(1, result.Auction.BidCount);
        Assert.Equal(1_250m, result.Auction.MinimumNextBid);

        var bids = await _client.GetJsonAsync<List<BidResponse>>($"/api/auctions/{auction.Id}/bids");
        Assert.NotNull(bids);
        Assert.Single(bids);
        Assert.Equal("Ayşe", bids[0].BidderName);
    }

    [Fact]
    public async Task Dusuk_teklif_409_ve_minimumRequired_doner()
    {
        var productId = GivenCatalogProduct(startingPrice: 5_000m);
        var auction = await CreateAuctionAsync(productId);

        var response = await _client.PostAsJsonAsync($"/api/auctions/{auction.Id}/bids", new PlaceBidRequest
        {
            Amount = 10m,
            BidderId = "u-ayse",
            BidderName = "Ayşe"
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        Assert.Equal("bid_too_low", problem.GetProperty("errorCode").GetString());
        // İstemci input'u düzeltebilsin diye alt sınır cevapta olmalı.
        Assert.Equal(5_000m, problem.GetProperty("minimumRequired").GetDecimal());
    }

    [Fact]
    public async Task Iptal_edilen_acik_artirmaya_teklif_verilemez()
    {
        var productId = GivenCatalogProduct();
        var auction = await CreateAuctionAsync(productId);

        var cancelled = await _client.PostAsync($"/api/auctions/{auction.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);

        var detail = await cancelled.Content.ReadAsync<AuctionDetailResponse>();
        Assert.NotNull(detail);
        Assert.Equal(AuctionStatus.Cancelled, detail.Status);

        var bid = await _client.PostAsJsonAsync($"/api/auctions/{auction.Id}/bids", new PlaceBidRequest
        {
            Amount = 999_999m,
            BidderId = "u-ayse",
            BidderName = "Ayşe"
        });

        Assert.Equal(HttpStatusCode.Conflict, bid.StatusCode);

        var problem = JsonSerializer.Deserialize<JsonElement>(await bid.Content.ReadAsStringAsync());
        Assert.Equal("auction_cancelled", problem.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Planlanmis_acik_artirma_start_ile_hemen_canliya_alinir()
    {
        var productId = GivenCatalogProduct();

        var created = await _client.PostAsJsonAsync("/api/auctions", new CreateAuctionRequest
        {
            ProductId = productId,
            MinimumIncrement = 100m,
            DurationMinutes = 60,
            StartsAt = DateTimeOffset.UtcNow.AddHours(5)
        });

        var scheduled = await created.Content.ReadAsync<AuctionDetailResponse>();
        Assert.NotNull(scheduled);
        Assert.Equal(AuctionStatus.Scheduled, scheduled.Status);

        // Planlanmışken teklif reddedilmeli.
        var early = await _client.PostAsJsonAsync($"/api/auctions/{scheduled.Id}/bids", new PlaceBidRequest
        {
            Amount = 999_999m,
            BidderId = "u-1",
            BidderName = "Erken"
        });
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);

        var started = await _client.PostAsync($"/api/auctions/{scheduled.Id}/start", null);
        var live = await started.Content.ReadAsync<AuctionDetailResponse>();
        Assert.NotNull(live);
        Assert.Equal(AuctionStatus.Live, live.Status);

        // Artık kabul edilmeli.
        var accepted = await _client.PostAsJsonAsync($"/api/auctions/{scheduled.Id}/bids", new PlaceBidRequest
        {
            Amount = live.MinimumNextBid,
            BidderId = "u-1",
            BidderName = "Erken"
        });
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
    }

    [Fact]
    public async Task Durum_filtresi_enum_adiyla_calisir()
    {
        var productId = GivenCatalogProduct();
        var auction = await CreateAuctionAsync(productId);

        var page = await _client.GetJsonAsync<Auction.Core.Paging.PagedResult<AuctionSummaryResponse>>(
            "/api/auctions?status=Live&pageSize=100");

        Assert.NotNull(page);
        Assert.Contains(page.Items, item => item.Id == auction.Id);
        Assert.All(page.Items, item => Assert.Equal(AuctionStatus.Live, item.Status));
    }

    [Fact]
    public async Task Swagger_dokumani_hub_bilgisini_icerir()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());

        // Swagger hub'ları gösteremediği için sözleşmeyi açıklamaya yazdık;
        // bu test o açıklamanın silinmesini fark eder.
        var description = document.GetProperty("info").GetProperty("description").GetString();
        Assert.NotNull(description);
        Assert.Contains("/hubs/auction", description);
        Assert.Contains("PlaceBid", description);
    }
}
