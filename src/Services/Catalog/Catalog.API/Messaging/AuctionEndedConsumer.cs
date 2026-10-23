using BuildingBlocks.Contracts.Auctions;
using Catalog.Business.Products;
using MassTransit;

namespace Catalog.API.Messaging;

/// <summary>
/// Auction servisinden gelen <see cref="AuctionEnded"/> olayını tüketir ve
/// satılan ürünü pasife çeker.
///
/// Tüketici bilinçli olarak ince: iş kuralı <see cref="IProductService"/> içinde.
/// Controller'lar ve hub'lar gibi bu da sadece bir adaptör.
///
/// Hata politikası: servis metodu ürün bulunamazsa/zaten pasifse istisna
/// fırlatmaz, çünkü fırlatsa mesaj yeniden denenir ve sonunda error kuyruğuna
/// düşerdi. Gerçek hatalarda (ör. veritabanı erişilemez) istisna çıkar ve
/// MassTransit'in retry politikası devreye girer — bu doğru davranış.
/// </summary>
public sealed class AuctionEndedConsumer : IConsumer<AuctionEnded>
{
    private readonly IProductService _products;
    private readonly ILogger<AuctionEndedConsumer> _logger;

    public AuctionEndedConsumer(IProductService products, ILogger<AuctionEndedConsumer> logger)
    {
        _products = products;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AuctionEnded> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "AuctionEnded alındı: açıkArtırma={AuctionId}, ürün={ProductId}, kazanan={Winner}",
            message.AuctionId,
            message.ProductId,
            message.WinnerName ?? "(teklif yok)");

        // Teklif gelmediyse ürün satılmamıştır; katalogda aktif kalsın.
        if (message.BidCount == 0 || message.WinnerId is null)
        {
            _logger.LogInformation(
                "Açık artırmaya teklif gelmemiş, ürün aktif bırakıldı: {ProductId}",
                message.ProductId);
            return;
        }

        await _products.MarkAsSoldAsync(message.ProductId, context.CancellationToken);
    }
}
