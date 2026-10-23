using System.Linq.Expressions;
using Catalog.Business.Products.Dtos;
using Catalog.Entity.Products;

namespace Catalog.Business.Products.Mapping;

/// <summary>
/// Elle mapping. AutoMapper yerine bunu tercih ettim: derleme zamanında
/// kontrol ediliyor ve <see cref="ToResponse"/> ifadesi doğrudan SQL'e çevrilebiliyor
/// (yani entity'nin tamamını değil sadece gereken kolonları çekiyoruz).
/// </summary>
public static class ProductMapping
{
    /// <summary>IQueryable üzerinde projeksiyon için (SELECT sadece gerekli kolonlar).</summary>
    public static readonly Expression<Func<Product, ProductResponse>> ToResponse =
        product => new ProductResponse(
            product.Id,
            product.Name,
            product.Description,
            product.Category,
            product.StartingPrice,
            product.ImageUrl,
            product.IsActive,
            product.CreatedAt,
            product.UpdatedAt);

    public static ProductResponse MapToResponse(this Product product) =>
        new(
            product.Id,
            product.Name,
            product.Description,
            product.Category,
            product.StartingPrice,
            product.ImageUrl,
            product.IsActive,
            product.CreatedAt,
            product.UpdatedAt);

    public static Product ToEntity(this CreateProductRequest request) =>
        new()
        {
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            Category = request.Category.Trim(),
            StartingPrice = request.StartingPrice,
            ImageUrl = request.ImageUrl?.Trim(),
            IsActive = true
        };

    public static void ApplyTo(this UpdateProductRequest request, Product product)
    {
        product.Name = request.Name.Trim();
        product.Description = request.Description?.Trim();
        product.Category = request.Category.Trim();
        product.StartingPrice = request.StartingPrice;
        product.ImageUrl = request.ImageUrl?.Trim();
        product.IsActive = request.IsActive;
    }
}
