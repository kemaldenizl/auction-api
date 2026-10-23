using Catalog.Business.Products.Dtos;
using Catalog.Business.Products.Mapping;
using Catalog.Core.Abstractions;
using Catalog.Core.Exceptions;
using Catalog.Core.Paging;
using Catalog.Entity.Products;
using Catalog.Repository.Repositories;
using BuildingBlocks.Contracts;
using BuildingBlocks.Contracts.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Catalog.Business.Products;

public sealed class ProductService : IProductService
{
    private readonly IProductRepository _products;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEventPublisher _events;
    private readonly ILogger<ProductService> _logger;

    public ProductService(
        IProductRepository products,
        IUnitOfWork unitOfWork,
        IEventPublisher events,
        ILogger<ProductService> logger)
    {
        _products = products;
        _unitOfWork = unitOfWork;
        _events = events;
        _logger = logger;
    }

    public async Task<PagedResult<ProductResponse>> GetListAsync(
        ProductListQuery query,
        CancellationToken cancellationToken = default)
    {
        var page = query.ToPageRequest();
        var source = _products.Query();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = $"%{query.Search.Trim()}%";
            source = source.Where(p =>
                EF.Functions.ILike(p.Name, term) ||
                (p.Description != null && EF.Functions.ILike(p.Description, term)));
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            var category = query.Category.Trim();
            source = source.Where(p => p.Category == category);
        }

        if (query.IsActive is not null)
        {
            source = source.Where(p => p.IsActive == query.IsActive.Value);
        }

        var totalCount = await source.LongCountAsync(cancellationToken);
        if (totalCount == 0)
        {
            return PagedResult<ProductResponse>.Empty(page);
        }

        var items = await ApplySort(source, query.SortBy, query.Descending)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(ProductMapping.ToResponse)
            .ToListAsync(cancellationToken);

        return new PagedResult<ProductResponse>(items, page.Page, page.PageSize, totalCount);
    }

    public async Task<ProductResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await _products.Query()
            .Where(p => p.Id == id)
            .Select(ProductMapping.ToResponse)
            .FirstOrDefaultAsync(cancellationToken);

        return product ?? throw new NotFoundException("Ürün", id);
    }

    public async Task<ProductResponse> CreateAsync(
        CreateProductRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = request.Name.Trim();

        if (await _products.NameExistsAsync(name, excludeId: null, cancellationToken))
        {
            throw new BusinessRuleException($"'{name}' adında bir ürün zaten var.", "duplicate_product_name");
        }

        var product = request.ToEntity();

        await _products.AddAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Ürün oluşturuldu: {ProductId} - {Name}", product.Id, product.Name);

        return product.MapToResponse();
    }

    public async Task<ProductResponse> UpdateAsync(
        Guid id,
        UpdateProductRequest request,
        CancellationToken cancellationToken = default)
    {
        // Query() AsNoTracking döndüğü için güncelleme yolunda GetByIdAsync kullanıyoruz (tracked).
        var product = await _products.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("Ürün", id);

        var name = request.Name.Trim();

        if (await _products.NameExistsAsync(name, excludeId: id, cancellationToken))
        {
            throw new BusinessRuleException($"'{name}' adında başka bir ürün var.", "duplicate_product_name");
        }

        request.ApplyTo(product);

        // Publish, SaveChanges'ten ÖNCE: outbox mesajı aynı transaction'a yazılsın.
        await _events.PublishAsync(
            new ProductUpdated(
                product.Id,
                product.Name,
                product.Description,
                product.ImageUrl,
                product.StartingPrice,
                product.IsActive,
                DateTimeOffset.UtcNow),
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Ürün güncellendi: {ProductId}", product.Id);

        return product.MapToResponse();
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await _products.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("Ürün", id);

        _products.Remove(product);

        await _events.PublishAsync(
            new ProductDeleted(id, DateTimeOffset.UtcNow),
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Ürün silindi: {ProductId}", id);
    }

    public async Task<bool> MarkAsSoldAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var product = await _products.GetByIdAsync(productId, cancellationToken);

        if (product is null)
        {
            // Ürün silinmiş olabilir; mesajı yeniden denemenin anlamı yok.
            _logger.LogWarning("Satıldı işaretlenecek ürün bulunamadı: {ProductId}", productId);
            return false;
        }

        if (!product.IsActive)
        {
            // Idempotency: aynı mesaj iki kez gelirse ikincisi hiçbir şey yapmaz.
            _logger.LogInformation("Ürün zaten pasif: {ProductId}", productId);
            return false;
        }

        product.IsActive = false;
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Ürün satıldı olarak işaretlendi: {ProductId}", productId);
        return true;
    }

    public Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
        _products.GetCategoriesAsync(cancellationToken);

    /// <summary>
    /// Sıralama alanı whitelist. İstemciden gelen string doğrudan sorguya girmiyor;
    /// tanımadığı değer gelirse varsayılana düşer.
    /// </summary>
    private static IQueryable<Product> ApplySort(IQueryable<Product> source, string? sortBy, bool descending) =>
        (sortBy?.ToLowerInvariant()) switch
        {
            "name" => descending
                ? source.OrderByDescending(p => p.Name)
                : source.OrderBy(p => p.Name),
            "price" => descending
                ? source.OrderByDescending(p => p.StartingPrice)
                : source.OrderBy(p => p.StartingPrice),
            _ => descending
                ? source.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id)
                : source.OrderBy(p => p.CreatedAt).ThenBy(p => p.Id)
        };
}
