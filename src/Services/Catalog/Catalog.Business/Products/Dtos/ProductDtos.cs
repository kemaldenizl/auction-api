using System.ComponentModel.DataAnnotations;
using Catalog.Core.Paging;

namespace Catalog.Business.Products.Dtos;

/// <summary>İstemciye dönen ürün gösterimi. Entity asla dışarı sızdırılmaz.</summary>
public sealed record ProductResponse(
    Guid Id,
    string Name,
    string? Description,
    string Category,
    decimal StartingPrice,
    string? ImageUrl,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record CreateProductRequest
{
    [Required(ErrorMessage = "Ürün adı zorunlu.")]
    [StringLength(200, MinimumLength = 3)]
    public string Name { get; init; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; init; }

    [Required(ErrorMessage = "Kategori zorunlu.")]
    [StringLength(100)]
    public string Category { get; init; } = string.Empty;

    [Range(0.01, 100_000_000, ErrorMessage = "Başlangıç fiyatı 0'dan büyük olmalı.")]
    public decimal StartingPrice { get; init; }

    [StringLength(500)]
    [Url(ErrorMessage = "Geçerli bir URL girin.")]
    public string? ImageUrl { get; init; }
}

public sealed record UpdateProductRequest
{
    [Required(ErrorMessage = "Ürün adı zorunlu.")]
    [StringLength(200, MinimumLength = 3)]
    public string Name { get; init; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; init; }

    [Required(ErrorMessage = "Kategori zorunlu.")]
    [StringLength(100)]
    public string Category { get; init; } = string.Empty;

    [Range(0.01, 100_000_000, ErrorMessage = "Başlangıç fiyatı 0'dan büyük olmalı.")]
    public decimal StartingPrice { get; init; }

    [StringLength(500)]
    [Url(ErrorMessage = "Geçerli bir URL girin.")]
    public string? ImageUrl { get; init; }

    public bool IsActive { get; init; } = true;
}

/// <summary>Liste filtresi. Sıralama alanı whitelist ile sınırlı (SQL injection yüzeyi yok).</summary>
public sealed record ProductListQuery
{
    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = PageRequest.DefaultPageSize;

    /// <summary>Ad ve açıklama içinde arar.</summary>
    public string? Search { get; init; }

    public string? Category { get; init; }

    /// <summary>name | price | createdAt (varsayılan: createdAt)</summary>
    public string? SortBy { get; init; }

    public bool Descending { get; init; } = true;

    public bool? IsActive { get; init; }

    public PageRequest ToPageRequest() => new() { Page = Page, PageSize = PageSize };
}
