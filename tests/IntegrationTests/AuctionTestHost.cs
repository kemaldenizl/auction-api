using Auction.Business.Auctions;
using Auction.Business.Auctions.Abstractions;
using Auction.Business.Catalog;
using Auction.Repository;
using BuildingBlocks.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TestCommon.Fakes;

namespace IntegrationTests;

/// <summary>
/// Gerçek Postgres'e bağlı, gerçek EF Core kullanan bir servis sağlayıcı.
///
/// Birim testlerden farkı: burada <c>IUnitOfWork</c> fake değil — gerçek
/// transaction'lar ve gerçek <c>FOR UPDATE</c> kilidi çalışıyor.
/// SignalR ve RabbitMQ yine fake, çünkü test edilen şey onlar değil.
///
/// DI kullanmamızın sebebi: eşzamanlılık testlerinde her paralel istek
/// kendi scope'unu (yani kendi DbContext'ini) almalı — gerçek HTTP
/// isteklerinde olduğu gibi. Tek DbContext paylaşılsa yarış testi anlamsızlaşır.
/// </summary>
internal sealed class AuctionTestHost : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    public RecordingAuctionNotifier Notifier { get; } = new();

    public RecordingEventPublisher Events { get; } = new();

    public ICatalogClient Catalog { get; } = Substitute.For<ICatalogClient>();

    private AuctionTestHost(string connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:AuctionDb"] = connectionString
            })
            .Build();

        var services = new ServiceCollection();

        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));

        // Üretimdeki kayıt yolunun aynısı: DbContext, UnitOfWork, repository'ler.
        services.AddAuctionRepository(configuration);

        // Altyapı sınırları fake: yayınlananları kaydediyoruz.
        services.AddSingleton<IAuctionNotifier>(Notifier);
        services.AddSingleton<IEventPublisher>(Events);
        services.AddSingleton(Catalog);

        services.AddScoped<IAuctionService, AuctionService>();

        _provider = services.BuildServiceProvider();
    }

    public static async Task<AuctionTestHost> CreateAsync(string connectionString)
    {
        var host = new AuctionTestHost(connectionString);

        await using var scope = host._provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
        await context.Database.MigrateAsync();

        return host;
    }

    /// <summary>Her çağrı yeni bir DbContext'li scope verir (bir HTTP isteği gibi).</summary>
    public AsyncServiceScope CreateScope() => _provider.CreateAsyncScope();

    public async Task<TResult> InScopeAsync<TResult>(Func<IAuctionService, Task<TResult>> work)
    {
        await using var scope = CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<IAuctionService>());
    }

    public async Task InScopeAsync(Func<AuctionDbContext, Task> work)
    {
        await using var scope = CreateScope();
        await work(scope.ServiceProvider.GetRequiredService<AuctionDbContext>());
    }

    public async Task<TResult> QueryAsync<TResult>(Func<AuctionDbContext, Task<TResult>> query)
    {
        await using var scope = CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AuctionDbContext>());
    }

    public ValueTask DisposeAsync() => _provider.DisposeAsync();
}
