using Catalog.Business.Products.Dtos;
using Catalog.Core.Paging;

namespace Catalog.Business.Products;

public interface IProductService
{
    Task<PagedResult<ProductResponse>> GetListAsync(ProductListQuery query, CancellationToken cancellationToken = default);

    Task<ProductResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ProductResponse> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken = default);

    Task<ProductResponse> UpdateAsync(Guid id, UpdateProductRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ürünü satıldı olarak işaretler (pasife çeker). Auction servisinden gelen
    /// <c>AuctionEnded</c> olayı üzerine çağrılır.
    ///
    /// Bilinçli olarak İSTİSNA FIRLATMAZ: mesaj tüketicisinden çağrıldığı için
    /// ürün bulunamazsa (silinmiş olabilir) ya da zaten pasifse sessizce geçer.
    /// Fırlatsaydı mesaj sürekli yeniden denenir, sonunda error kuyruğuna düşerdi.
    /// </summary>
    /// <returns>Bir değişiklik yapıldıysa <c>true</c>.</returns>
    Task<bool> MarkAsSoldAsync(Guid productId, CancellationToken cancellationToken = default);
}
