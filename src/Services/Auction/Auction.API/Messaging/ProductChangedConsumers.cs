using Auction.Business.Auctions;
using Auction.Business.Auctions.Dtos;
using BuildingBlocks.Contracts.Catalog;
using MassTransit;

namespace Auction.API.Messaging;

/// <summary>
/// Catalog'da ürün güncellendiğinde <b>henüz başlamamış</b> açık artırmaların
/// ürün kopyasını tazeler.
///
/// Neden sadece başlamamış olanlar: açık artırma açılırken ürün bilgisi
/// bilinçli olarak kopyalanıyor (snapshot). Canlı ya da bitmiş bir ilanın
/// başlığı/fotoğrafı altından değişmemeli — insanlar neye teklif verdiklerini
/// bilmek zorunda. Ama henüz başlamamış, kimsenin teklif vermediği bir ilanın
/// güncel bilgiyi göstermesi doğru olan.
/// </summary>
public sealed class ProductUpdatedConsumer : IConsumer<ProductUpdated>
{
    private readonly IAuctionService _auctions;
    private readonly ILogger<ProductUpdatedConsumer> _logger;

    public ProductUpdatedConsumer(IAuctionService auctions, ILogger<ProductUpdatedConsumer> logger)
    {
        _auctions = auctions;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ProductUpdated> context)
    {
        var message = context.Message;

        _logger.LogInformation("ProductUpdated alındı: {ProductId} ({Name})", message.ProductId, message.Name);

        await _auctions.RefreshScheduledSnapshotsAsync(
            new ProductSnapshot(message.ProductId, message.Name, message.Description, message.ImageUrl),
            context.CancellationToken);
    }
}

/// <summary>
/// Catalog'dan ürün silindiğinde o ürüne ait başlamamış açık artırmaları iptal eder.
///
/// Zinciri tamamlar: Catalog'da DELETE -> RabbitMQ -> bu tüketici -> veritabanı
/// -> SignalR. Yani odada açık duran bir tarayıcı, başka bir serviste yapılan
/// silme işlemi yüzünden "iptal edildi" bildirimini anında alır.
/// </summary>
public sealed class ProductDeletedConsumer : IConsumer<ProductDeleted>
{
    private readonly IAuctionService _auctions;
    private readonly ILogger<ProductDeletedConsumer> _logger;

    public ProductDeletedConsumer(IAuctionService auctions, ILogger<ProductDeletedConsumer> logger)
    {
        _auctions = auctions;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ProductDeleted> context)
    {
        _logger.LogInformation("ProductDeleted alındı: {ProductId}", context.Message.ProductId);

        await _auctions.CancelScheduledForProductAsync(
            context.Message.ProductId,
            context.CancellationToken);
    }
}
