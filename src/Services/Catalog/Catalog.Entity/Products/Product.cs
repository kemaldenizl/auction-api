using Catalog.Entity.Common;

namespace Catalog.Entity.Products;

/// <summary>
/// Açık artırmaya çıkabilecek ürün. Catalog servisinin tek aggregate'i.
/// Not: Fiyat burada "başlangıç fiyatı"dır; güncel fiyat Auction servisinin işi.
/// </summary>
public class Product : BaseEntity
{
    public required string Name { get; set; }

    public string? Description { get; set; }

    public required string Category { get; set; }

    public decimal StartingPrice { get; set; }

    public string? ImageUrl { get; set; }

    public bool IsActive { get; set; } = true;
}
