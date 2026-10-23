using Auction.API.Messaging;
using Auction.Business.Auctions;
using Auction.Business.Auctions.Abstractions;
using Auction.Business.Catalog;
using Auction.Entity.Auctions;
using BuildingBlocks.Contracts;
using BuildingBlocks.Contracts.Auctions;
using BuildingBlocks.Contracts.Catalog;
using Catalog.API.Messaging;
using Catalog.Business.Products;
using Catalog.Entity.Products;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TestCommon.Containers;
using TestCommon.Fakes;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// Mesaj tüketicilerini gerçek veritabanı + MassTransit test harness ile doğrular.
///
/// Harness in-memory transport kullanır: RabbitMQ ayağa kaldırmaya gerek yok,
/// ama MassTransit'in kendi boru hattı (tüketici keşfi, DI scope'u, serileştirme)
/// gerçekten çalışır. Yani "consumer kaydı yapılmamış" gibi hatalar yakalanır.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ConsumerTests
{
    private readonly PostgresFixture _postgres;

    public ConsumerTests(PostgresFixture postgres) => _postgres = postgres;

    // ------------------------------------------------- Auction tarafı tüketicileri

    private async Task<ServiceProvider> BuildAuctionSideAsync(RecordingAuctionNotifier notifier)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:AuctionDb"] = _postgres.AuctionConnectionString
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddAuctionRepositoryForTests(configuration);
        services.AddSingleton<IAuctionNotifier>(notifier);
        services.AddSingleton<IEventPublisher>(new RecordingEventPublisher());
        services.AddSingleton(Substitute.For<ICatalogClient>());
        services.AddScoped<IAuctionService, AuctionService>();

        services.AddMassTransitTestHarness(bus =>
        {
            bus.AddConsumer<ProductUpdatedConsumer>();
            bus.AddConsumer<ProductDeletedConsumer>();
        });

        var provider = services.BuildServiceProvider(true);

        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider
            .GetRequiredService<Auction.Repository.AuctionDbContext>()
            .Database.MigrateAsync();

        return provider;
    }

    [Fact]
    public async Task ProductUpdated_tuketicisi_planlanmis_acik_artirmanin_kopyasini_tazeler()
    {
        var notifier = new RecordingAuctionNotifier();
        await using var provider = await BuildAuctionSideAsync(notifier);

        var productId = Guid.CreateVersion7();
        var scheduledId = Guid.CreateVersion7();
        var liveId = Guid.CreateVersion7();

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<Auction.Repository.AuctionDbContext>();

            context.AuctionItems.AddRange(
                NewAuction(scheduledId, productId, "Eski Başlık", AuctionStatus.Scheduled),
                NewAuction(liveId, productId, "Canlı Başlık", AuctionStatus.Live));

            await context.SaveChangesAsync();
        }

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new ProductUpdated(
            productId,
            "Yeni Başlık",
            "yeni açıklama",
            "https://example.com/yeni.png",
            9_999m,
            IsActive: true,
            DateTimeOffset.UtcNow));

        Assert.True(await harness.Consumed.Any<ProductUpdated>());

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<Auction.Repository.AuctionDbContext>();

            var scheduled = await context.AuctionItems.AsNoTracking().SingleAsync(a => a.Id == scheduledId);
            var live = await context.AuctionItems.AsNoTracking().SingleAsync(a => a.Id == liveId);

            // Başlamamış olan tazelenir...
            Assert.Equal("Yeni Başlık", scheduled.Title);
            Assert.Equal("https://example.com/yeni.png", scheduled.ImageUrl);

            // ...canlı olanın snapshot'ı KORUNUR. İnsanlar neye teklif
            // verdiklerini bilmek zorunda; ilan altından değişmez.
            Assert.Equal("Canlı Başlık", live.Title);
        }
    }

    [Fact]
    public async Task ProductDeleted_tuketicisi_planlanmis_acik_artirmayi_iptal_eder_ve_odaya_bildirir()
    {
        var notifier = new RecordingAuctionNotifier();
        await using var provider = await BuildAuctionSideAsync(notifier);

        var productId = Guid.CreateVersion7();
        var scheduledId = Guid.CreateVersion7();
        var endedId = Guid.CreateVersion7();

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<Auction.Repository.AuctionDbContext>();

            context.AuctionItems.AddRange(
                NewAuction(scheduledId, productId, "Planlanmış", AuctionStatus.Scheduled),
                NewAuction(endedId, productId, "Bitmiş", AuctionStatus.Ended));

            await context.SaveChangesAsync();
        }

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new ProductDeleted(productId, DateTimeOffset.UtcNow));

        Assert.True(await harness.Consumed.Any<ProductDeleted>());

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<Auction.Repository.AuctionDbContext>();

            var scheduled = await context.AuctionItems.AsNoTracking().SingleAsync(a => a.Id == scheduledId);
            var ended = await context.AuctionItems.AsNoTracking().SingleAsync(a => a.Id == endedId);

            Assert.Equal(AuctionStatus.Cancelled, scheduled.Status);
            // Bitmiş açık artırmanın geçmişi değiştirilemez.
            Assert.Equal(AuctionStatus.Ended, ended.Status);
        }

        // Zincirin son halkası: odadaki tarayıcıya WebSocket bildirimi.
        var cancelled = Assert.Single(notifier.Cancelled);
        Assert.Equal(scheduledId, cancelled.AuctionId);
    }

    // ------------------------------------------------- Catalog tarafı tüketicisi

    [Fact]
    public async Task AuctionEnded_tuketicisi_satilan_urunu_pasife_ceker()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CatalogDb"] = _postgres.CatalogConnectionString
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddCatalogRepositoryForTests(configuration);
        services.AddSingleton<IEventPublisher>(new RecordingEventPublisher());
        services.AddScoped<IProductService, ProductService>();
        services.AddMassTransitTestHarness(bus => bus.AddConsumer<AuctionEndedConsumer>());

        await using var provider = services.BuildServiceProvider(true);

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<Catalog.Repository.CatalogDbContext>();
            await context.Database.MigrateAsync();
        }

        var soldProductId = Guid.CreateVersion7();
        var unsoldProductId = Guid.CreateVersion7();

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<Catalog.Repository.CatalogDbContext>();

            context.Products.AddRange(
                new Product { Id = soldProductId, Name = $"Satilan {soldProductId}", Category = "Test", StartingPrice = 100m },
                new Product { Id = unsoldProductId, Name = $"Satilmayan {unsoldProductId}", Category = "Test", StartingPrice = 100m });

            await context.SaveChangesAsync();
        }

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        // Teklif gelen açık artırma -> ürün satıldı.
        await harness.Bus.Publish(new AuctionEnded(
            Guid.CreateVersion7(), soldProductId, "Satilan", 5_000m, "u-kazanan", "Kazanan", 3, DateTimeOffset.UtcNow));

        // Teklif GELMEYEN açık artırma -> ürün satılmadı, aktif kalmalı.
        await harness.Bus.Publish(new AuctionEnded(
            Guid.CreateVersion7(), unsoldProductId, "Satilmayan", 100m, null, null, 0, DateTimeOffset.UtcNow));

        Assert.True(await harness.Consumed.Any<AuctionEnded>());

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<Catalog.Repository.CatalogDbContext>();

            var sold = await context.Products.AsNoTracking().SingleAsync(p => p.Id == soldProductId);
            var unsold = await context.Products.AsNoTracking().SingleAsync(p => p.Id == unsoldProductId);

            Assert.False(sold.IsActive);
            Assert.True(unsold.IsActive);
        }
    }

    [Fact]
    public async Task AuctionEnded_tuketicisi_ayni_mesaj_iki_kez_gelse_de_patlamaz()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CatalogDb"] = _postgres.CatalogConnectionString
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddCatalogRepositoryForTests(configuration);
        services.AddSingleton<IEventPublisher>(new RecordingEventPublisher());
        services.AddScoped<IProductService, ProductService>();
        services.AddMassTransitTestHarness(bus => bus.AddConsumer<AuctionEndedConsumer>());

        await using var provider = services.BuildServiceProvider(true);

        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider
                .GetRequiredService<Catalog.Repository.CatalogDbContext>()
                .Database.MigrateAsync();
        }

        var productId = Guid.CreateVersion7();

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<Catalog.Repository.CatalogDbContext>();
            context.Products.Add(new Product
            {
                Id = productId,
                Name = $"Idempotency {productId}",
                Category = "Test",
                StartingPrice = 100m
            });
            await context.SaveChangesAsync();
        }

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var message = new AuctionEnded(
            Guid.CreateVersion7(), productId, "Idempotency", 1_000m, "u-1", "Kazanan", 1, DateTimeOffset.UtcNow);

        // Broker "en az bir kez" teslim eder: aynı mesaj tekrar gelebilir.
        // İkincisi hata fırlatırsa mesaj error kuyruğuna düşer ve gürültü yaratır.
        await harness.Bus.Publish(message);
        await harness.Bus.Publish(message);

        Assert.True(await harness.Consumed.Any<AuctionEnded>());

        // Tüketicide hiçbir hata olmamalı.
        var faults = harness.Published.Select<Fault<AuctionEnded>>().ToList();
        Assert.Empty(faults);

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<Catalog.Repository.CatalogDbContext>();
            var product = await context.Products.AsNoTracking().SingleAsync(p => p.Id == productId);
            Assert.False(product.IsActive);
        }
    }

    private static AuctionItem NewAuction(Guid id, Guid productId, string title, AuctionStatus status) => new()
    {
        Id = id,
        ProductId = productId,
        Title = title,
        StartingPrice = 1_000m,
        CurrentPrice = 1_000m,
        MinimumIncrement = 100m,
        StartsAt = status == AuctionStatus.Scheduled
            ? DateTimeOffset.UtcNow.AddHours(1)
            : DateTimeOffset.UtcNow.AddHours(-1),
        EndsAt = DateTimeOffset.UtcNow.AddHours(3),
        Status = status
    };
}

/// <summary>
/// Üretimdeki kayıt metotları <c>IConfiguration</c> bekliyor; testlerde aynı
/// yolu kullanmak için küçük sarmalayıcılar. Ayrı isim vermemizin sebebi
/// iki servisin uzantı metotlarının aynı adı taşıması.
/// </summary>
internal static class TestRegistrationExtensions
{
    public static IServiceCollection AddAuctionRepositoryForTests(
        this IServiceCollection services,
        IConfiguration configuration) =>
        Auction.Repository.RepositoryServiceRegistration.AddAuctionRepository(services, configuration);

    public static IServiceCollection AddCatalogRepositoryForTests(
        this IServiceCollection services,
        IConfiguration configuration) =>
        Catalog.Repository.RepositoryServiceRegistration.AddCatalogRepository(services, configuration);
}
