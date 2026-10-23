using Auction.Business.Auctions;
using Auction.Business.Catalog;
using Auction.Entity.Auctions;
using Auction.Repository.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TestCommon.Fakes;

namespace UnitTests.Auctions;

/// <summary>
/// <see cref="AuctionService"/>'i test edilebilir bağımlılıklarla kurar.
///
/// Bu sınıfın varlığı tasarımın bir kanıtı: servis ne veritabanı ne SignalR
/// ne de RabbitMQ bilmiyor, hepsi arayüz arkasında. Bu yüzden iş kurallarını
/// hiçbir altyapı ayağa kaldırmadan test edebiliyoruz.
/// </summary>
internal sealed class AuctionServiceHarness
{
    public IAuctionItemRepository Auctions { get; } = Substitute.For<IAuctionItemRepository>();

    public IBidRepository Bids { get; } = Substitute.For<IBidRepository>();

    public ICatalogClient Catalog { get; } = Substitute.For<ICatalogClient>();

    public FakeAuctionUnitOfWork UnitOfWork { get; } = new();

    public RecordingAuctionNotifier Notifier { get; } = new();

    public RecordingEventPublisher Events { get; } = new();

    public AuctionService Service { get; }

    public AuctionServiceHarness()
    {
        // GetRecentAsync varsayılanı null dönerse mapping patlar; boş liste veriyoruz.
        Bids.GetRecentAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Bid>());

        Service = new AuctionService(
            Auctions,
            Bids,
            UnitOfWork,
            Notifier,
            Catalog,
            Events,
            NullLogger<AuctionService>.Instance);
    }

    /// <summary>Verilen açık artırmayı hem kilitli okumada hem normal okumada döndürür.</summary>
    public AuctionItem Existing(AuctionItem auction)
    {
        Auctions.GetForUpdateAsync(auction.Id, Arg.Any<CancellationToken>()).Returns(auction);
        Auctions.GetByIdAsync(auction.Id, Arg.Any<CancellationToken>()).Returns(auction);
        return auction;
    }

    public void NoSuchAuction(Guid id)
    {
        Auctions.GetForUpdateAsync(id, Arg.Any<CancellationToken>()).Returns((AuctionItem?)null);
        Auctions.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns((AuctionItem?)null);
    }

    public void CatalogHas(CatalogProduct product) =>
        Catalog.GetProductAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);

    public void CatalogMissing(Guid productId) =>
        Catalog.GetProductAsync(productId, Arg.Any<CancellationToken>()).Returns((CatalogProduct?)null);

    public void ScheduledForProduct(Guid productId, params AuctionItem[] auctions) =>
        Auctions.GetScheduledByProductAsync(productId, Arg.Any<CancellationToken>()).Returns(auctions);
}
