using Auction.Business.Auctions;
using Auction.Repository.Repositories;

namespace Auction.API.Infrastructure;

/// <summary>
/// Saate bağlı durum geçişlerini yapan arka plan servisi:
/// Scheduled -> Live (başlama saati geldi) ve Live -> Ended (süre doldu).
///
/// Canlı demo açısından önemli: kullanıcı hiçbir şey yapmasa bile
/// açık artırma kendi kendine açılıp kapanır ve olaylar WebSocket'ten düşer.
///
/// Çoklu instance: geçişi "UPDATE ... WHERE Status = beklenen" ile yaptığımız
/// için yalnızca bir replica 1 satır etkiler ve olayı yalnızca o yayınlar.
/// </summary>
public sealed class AuctionLifecycleWorker : BackgroundService
{
    private const int BatchSize = 50;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuctionLifecycleWorker> _logger;
    private readonly TimeSpan _interval;

    public AuctionLifecycleWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<AuctionLifecycleWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        var seconds = configuration.GetValue<int?>("AuctionLifecycle:IntervalSeconds") ?? 2;
        _interval = TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 60));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Açık artırma yaşam döngüsü servisi başladı (her {Interval} saniye).",
            _interval.TotalSeconds);

        using var timer = new PeriodicTimer(_interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Tek bir tur patlasa bile servis ölmemeli.
                _logger.LogError(exception, "Yaşam döngüsü turu başarısız oldu, bir sonraki turda denenecek.");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Açık artırma yaşam döngüsü servisi durdu.");
    }

    private async Task ProcessOnceAsync(CancellationToken cancellationToken)
    {
        // BackgroundService singleton; DbContext scoped. Her tur için yeni scope şart.
        using var scope = _scopeFactory.CreateScope();

        var repository = scope.ServiceProvider.GetRequiredService<IAuctionItemRepository>();
        var auctionService = scope.ServiceProvider.GetRequiredService<IAuctionService>();

        var now = DateTimeOffset.UtcNow;

        foreach (var auctionId in await repository.GetDueToStartAsync(now, BatchSize, cancellationToken))
        {
            await auctionService.TryStartDueAsync(auctionId, cancellationToken);
        }

        foreach (var auctionId in await repository.GetDueToEndAsync(now, BatchSize, cancellationToken))
        {
            await auctionService.TryEndDueAsync(auctionId, cancellationToken);
        }
    }
}
