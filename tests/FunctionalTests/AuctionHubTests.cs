using System.Net.Http.Json;
using Auction.Business.Auctions.Dtos;
using Auction.Business.Auctions.Events;
using Auction.Business.Catalog;
using Auction.Entity.Auctions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TestCommon.Containers;
using Xunit;

namespace FunctionalTests;

/// <summary>
/// SignalR hub'ının uçtan uca davranışı: gruplar, hedefli bildirimler,
/// REST ile hub'ın aynı kuralları paylaşması.
///
/// <para><b>Taşıma katmanı notu:</b> burada long polling kullanıyoruz.
/// TestServer WebSocket'i destekliyor ama bağlamak ek kurulum gerektiriyor ve
/// test edilen şey taşıma katmanı değil — hub mantığı. Gerçek WebSocket yolu
/// çalışan sistem üzerinde ayrıca doğrulandı (bkz. README).</para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AuctionHubTests : IAsyncLifetime
{
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(10);

    private readonly PostgresFixture _postgres;
    private AuctionApiFactory _factory = null!;
    private HttpClient _client = null!;
    private readonly List<HubConnection> _connections = [];

    public AuctionHubTests(PostgresFixture postgres) => _postgres = postgres;

    public Task InitializeAsync()
    {
        _factory = new AuctionApiFactory(_postgres.AuctionConnectionString);
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        foreach (var connection in _connections)
        {
            await connection.DisposeAsync();
        }

        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<HubConnection> ConnectAsync(string bidderId, string bidderName)
    {
        var query = $"?bidderId={Uri.EscapeDataString(bidderId)}&bidderName={Uri.EscapeDataString(bidderName)}";

        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "hubs/auction" + query), options =>
            {
                // İstek TestServer'a yönlendirilir; gerçek ağ yok.
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            // Hub protokolü kendi serileştiricisini kullanır: sunucu enum'ları
            // string gönderdiği için istemcide de aynı converter gerekli.
            .AddJsonProtocol(protocol =>
                protocol.PayloadSerializerOptions.Converters.Add(
                    new System.Text.Json.Serialization.JsonStringEnumConverter()))
            .Build();

        _connections.Add(connection);
        await connection.StartAsync();

        return connection;
    }

    /// <summary>Belirli bir olayı yakalamak için tek kullanımlık bekleyici.</summary>
    private static TaskCompletionSource<T> Expect<T>(HubConnection connection, string eventName)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<T>(eventName, payload => completion.TrySetResult(payload));
        return completion;
    }

    private static async Task<T> AwaitEventAsync<T>(TaskCompletionSource<T> completion, string what)
    {
        var finished = await Task.WhenAny(completion.Task, Task.Delay(EventTimeout));

        Assert.True(finished == completion.Task, $"'{what}' olayı {EventTimeout.TotalSeconds} sn içinde gelmedi.");

        return await completion.Task;
    }

    private static async Task AssertNotReceivedAsync<T>(TaskCompletionSource<T> completion, string what)
    {
        // Olmaması gereken bir olay için kısa bir pencere yeterli.
        var finished = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(1)));

        Assert.True(finished != completion.Task, $"'{what}' olayı gelmemeliydi ama geldi.");
    }

    private async Task<AuctionDetailResponse> GivenLiveAuctionAsync(
        decimal startingPrice = 1_000m,
        decimal minimumIncrement = 100m)
    {
        var productId = Guid.CreateVersion7();

        _factory.Catalog.GetProductAsync(productId, Arg.Any<CancellationToken>())
            .Returns(new CatalogProduct(
                productId, $"Ürün {productId}", "açıklama", "https://example.com/p.png", startingPrice, true));

        var response = await _client.PostAsJsonAsync("/api/auctions", new CreateAuctionRequest
        {
            ProductId = productId,
            MinimumIncrement = minimumIncrement,
            DurationMinutes = 60
        });

        var detail = await response.Content.ReadAsync<AuctionDetailResponse>();
        Assert.NotNull(detail);
        return detail;
    }

    [Fact]
    public async Task JoinAuction_odaya_katar_ve_guncel_durumu_doner()
    {
        var auction = await GivenLiveAuctionAsync(startingPrice: 7_500m, minimumIncrement: 500m);
        var connection = await ConnectAsync("u-ayse", "Ayşe");

        // Hub metodunun değer döndürmesi sayesinde "önce REST'ten çek, sonra
        // socket'e bağlan" yarışı oluşmuyor.
        var detail = await connection.InvokeAsync<AuctionDetailResponse>("JoinAuction", auction.Id);

        Assert.Equal(auction.Id, detail.Id);
        Assert.Equal(AuctionStatus.Live, detail.Status);
        Assert.Equal(7_500m, detail.CurrentPrice);
        Assert.Equal(7_500m, detail.MinimumNextBid);
        Assert.Equal(0, detail.BidCount);
    }

    [Fact]
    public async Task WhoAmI_query_stringdeki_kimligi_doner()
    {
        var connection = await ConnectAsync("u-kemal", "Kemal Deniz");

        var identity = await connection.InvokeAsync<BidderIdentity>("WhoAmI");

        Assert.Equal("u-kemal", identity.Id);
        Assert.Equal("Kemal Deniz", identity.Name);
    }

    [Fact]
    public async Task Kimlik_verilmezse_misafir_olarak_baglanir()
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "hubs/auction"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();

        _connections.Add(connection);
        await connection.StartAsync();

        var identity = await connection.InvokeAsync<BidderIdentity>("WhoAmI");

        Assert.Equal("Misafir", identity.Name);
        Assert.StartsWith("anon-", identity.Id);
    }

    [Fact]
    public async Task Hub_uzerinden_verilen_teklif_odadaki_herkese_ulasir()
    {
        var auction = await GivenLiveAuctionAsync(startingPrice: 1_000m, minimumIncrement: 100m);

        var ayse = await ConnectAsync("u-ayse", "Ayşe");
        var mehmet = await ConnectAsync("u-mehmet", "Mehmet");

        await ayse.InvokeAsync<AuctionDetailResponse>("JoinAuction", auction.Id);
        await mehmet.InvokeAsync<AuctionDetailResponse>("JoinAuction", auction.Id);

        var ayseGets = Expect<BidPlacedEvent>(ayse, "BidPlaced");
        var mehmetGets = Expect<BidPlacedEvent>(mehmet, "BidPlaced");

        await mehmet.InvokeAsync("PlaceBid", auction.Id, 1_000m);

        var received = await AwaitEventAsync(ayseGets, "Ayşe için BidPlaced");
        await AwaitEventAsync(mehmetGets, "Mehmet için BidPlaced");

        Assert.Equal(1_000m, received.Amount);
        Assert.Equal("Mehmet", received.BidderName);
        Assert.Equal(1, received.BidCount);
        Assert.Equal(1_100m, received.MinimumNextBid);
    }

    [Fact]
    public async Task Outbid_bildirimi_YALNIZCA_teklifi_gecilen_kullaniciya_gider()
    {
        var auction = await GivenLiveAuctionAsync(startingPrice: 1_000m, minimumIncrement: 100m);

        var ayse = await ConnectAsync("u-ayse", "Ayşe");
        var mehmet = await ConnectAsync("u-mehmet", "Mehmet");

        await ayse.InvokeAsync<AuctionDetailResponse>("JoinAuction", auction.Id);
        await mehmet.InvokeAsync<AuctionDetailResponse>("JoinAuction", auction.Id);

        // Mehmet lider oluyor.
        var mehmetFirstBid = Expect<BidPlacedEvent>(mehmet, "BidPlaced");
        await mehmet.InvokeAsync("PlaceBid", auction.Id, 1_000m);
        await AwaitEventAsync(mehmetFirstBid, "ilk BidPlaced");

        var mehmetOutbid = Expect<OutbidEvent>(mehmet, "Outbid");
        var ayseOutbid = Expect<OutbidEvent>(ayse, "Outbid");

        // Ayşe geçiyor.
        await ayse.InvokeAsync("PlaceBid", auction.Id, 1_100m);

        var outbid = await AwaitEventAsync(mehmetOutbid, "Mehmet için Outbid");
        Assert.Equal(1_000m, outbid.YourBid);
        Assert.Equal(1_100m, outbid.NewPrice);
        Assert.Equal("Ayşe", outbid.NewLeaderName);

        // Teklifi veren kişi kendi "geçildin" bildirimini almamalı.
        await AssertNotReceivedAsync(ayseOutbid, "Ayşe için Outbid");
    }

    [Fact]
    public async Task BidRejected_YALNIZCA_teklifi_gonderen_baglantiya_gider()
    {
        var auction = await GivenLiveAuctionAsync(startingPrice: 5_000m, minimumIncrement: 100m);

        var ayse = await ConnectAsync("u-ayse", "Ayşe");
        var mehmet = await ConnectAsync("u-mehmet", "Mehmet");

        await ayse.InvokeAsync<AuctionDetailResponse>("JoinAuction", auction.Id);
        await mehmet.InvokeAsync<AuctionDetailResponse>("JoinAuction", auction.Id);

        var mehmetRejected = Expect<BidRejectedEvent>(mehmet, "BidRejected");
        var ayseRejected = Expect<BidRejectedEvent>(ayse, "BidRejected");

        await mehmet.InvokeAsync("PlaceBid", auction.Id, 1m);

        var rejected = await AwaitEventAsync(mehmetRejected, "Mehmet için BidRejected");
        Assert.Equal("bid_too_low", rejected.ErrorCode);
        Assert.Equal(5_000m, rejected.MinimumRequired);

        // Başkasının reddi kimseyi ilgilendirmez.
        await AssertNotReceivedAsync(ayseRejected, "Ayşe için BidRejected");
    }

    [Fact]
    public async Task Gecersiz_teklif_baglantiyi_dusurmez()
    {
        var auction = await GivenLiveAuctionAsync(startingPrice: 5_000m);
        var mehmet = await ConnectAsync("u-mehmet", "Mehmet");
        await mehmet.InvokeAsync<AuctionDetailResponse>("JoinAuction", auction.Id);

        var rejected = Expect<BidRejectedEvent>(mehmet, "BidRejected");
        await mehmet.InvokeAsync("PlaceBid", auction.Id, 1m);
        await AwaitEventAsync(rejected, "BidRejected");

        // İş kuralı hatası istisna olarak fırlatılsa bağlantı kapanırdı;
        // hub bunları yakalayıp olaya çeviriyor.
        Assert.Equal(HubConnectionState.Connected, mehmet.State);

        // Ve aynı bağlantıdan geçerli teklif verilebilmeli.
        var accepted = Expect<BidPlacedEvent>(mehmet, "BidPlaced");
        await mehmet.InvokeAsync("PlaceBid", auction.Id, 5_000m);
        await AwaitEventAsync(accepted, "geçerli BidPlaced");
    }

    [Fact]
    public async Task REST_ile_verilen_teklif_WebSocket_istemcilerine_dusur()
    {
        var auction = await GivenLiveAuctionAsync(startingPrice: 1_000m, minimumIncrement: 100m);

        var ayse = await ConnectAsync("u-ayse", "Ayşe");
        await ayse.InvokeAsync<AuctionDetailResponse>("JoinAuction", auction.Id);

        var ayseGets = Expect<BidPlacedEvent>(ayse, "BidPlaced");

        // Swagger'dan/başka bir istemciden gelen REST isteği.
        var response = await _client.PostAsJsonAsync($"/api/auctions/{auction.Id}/bids", new PlaceBidRequest
        {
            Amount = 1_000m,
            BidderId = "u-swagger",
            BidderName = "Swagger Kullanıcısı"
        });

        response.EnsureSuccessStatusCode();

        // İki yolun aynı iş servisini kullanmasının somut kanıtı.
        var received = await AwaitEventAsync(ayseGets, "REST teklifi sonrası BidPlaced");
        Assert.Equal("Swagger Kullanıcısı", received.BidderName);
        Assert.Equal(1_000m, received.Amount);
    }

    [Fact]
    public async Task Odaya_katilinca_izleyici_sayisi_yayinlanir()
    {
        var auction = await GivenLiveAuctionAsync();

        var ayse = await ConnectAsync("u-ayse", "Ayşe");
        var firstJoin = Expect<ViewerCountChangedEvent>(ayse, "ViewerCountChanged");

        await ayse.InvokeAsync<AuctionDetailResponse>("JoinAuction", auction.Id);

        var count = await AwaitEventAsync(firstJoin, "ilk ViewerCountChanged");
        Assert.Equal(auction.Id, count.AuctionId);
        Assert.Equal(1, count.ViewerCount);

        // İkinci izleyici katılınca ilk izleyici de haberdar olmalı.
        var secondJoin = Expect<ViewerCountChangedEvent>(ayse, "ViewerCountChanged");
        var mehmet = await ConnectAsync("u-mehmet", "Mehmet");
        await mehmet.InvokeAsync<AuctionDetailResponse>("JoinAuction", auction.Id);

        var updated = await AwaitEventAsync(secondJoin, "ikinci ViewerCountChanged");
        Assert.Equal(2, updated.ViewerCount);
    }

    [Fact]
    public async Task LeaveAuction_sonrasi_odanin_olaylari_gelmez()
    {
        var auction = await GivenLiveAuctionAsync(startingPrice: 1_000m, minimumIncrement: 100m);

        var ayse = await ConnectAsync("u-ayse", "Ayşe");
        var mehmet = await ConnectAsync("u-mehmet", "Mehmet");

        await ayse.InvokeAsync<AuctionDetailResponse>("JoinAuction", auction.Id);
        await mehmet.InvokeAsync<AuctionDetailResponse>("JoinAuction", auction.Id);

        await ayse.InvokeAsync("LeaveAuction", auction.Id);

        var ayseGets = Expect<BidPlacedEvent>(ayse, "BidPlaced");

        await mehmet.InvokeAsync("PlaceBid", auction.Id, 1_000m);

        await AssertNotReceivedAsync(ayseGets, "odadan çıkan Ayşe için BidPlaced");
    }

    [Fact]
    public async Task Iptal_edilen_acik_artirma_odaya_bildirilir()
    {
        var auction = await GivenLiveAuctionAsync();

        var ayse = await ConnectAsync("u-ayse", "Ayşe");
        await ayse.InvokeAsync<AuctionDetailResponse>("JoinAuction", auction.Id);

        var cancelled = Expect<AuctionCancelledEvent>(ayse, "AuctionCancelled");

        var response = await _client.PostAsync($"/api/auctions/{auction.Id}/cancel", null);
        response.EnsureSuccessStatusCode();

        var notification = await AwaitEventAsync(cancelled, "AuctionCancelled");
        Assert.Equal(auction.Id, notification.AuctionId);
    }
}
