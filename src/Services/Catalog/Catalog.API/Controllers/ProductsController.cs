using Catalog.Business.Products;
using Catalog.Business.Products.Dtos;
using Catalog.Core.Paging;
using Microsoft.AspNetCore.Mvc;

namespace Catalog.API.Controllers;

/// <summary>Ürün CRUD uçları.</summary>
[ApiController]
[Route("api/products")]
[Produces("application/json")]
public sealed class ProductsController : ControllerBase
{
    private readonly IProductService _products;

    public ProductsController(IProductService products)
    {
        _products = products;
    }

    /// <summary>Ürünleri sayfalı, filtreli ve sıralı listeler.</summary>
    /// <remarks>Örnek: <c>/api/products?page=1&amp;pageSize=10&amp;search=omega&amp;sortBy=price&amp;descending=false</c></remarks>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ProductResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ProductResponse>>> GetList(
        [FromQuery] ProductListQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _products.GetListAsync(query, cancellationToken);
        return Ok(result);
    }

    /// <summary>Katalogda kullanılan kategorileri döner (filtre açılır listesi için).</summary>
    [HttpGet("categories")]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<string>>> GetCategories(CancellationToken cancellationToken)
    {
        var categories = await _products.GetCategoriesAsync(cancellationToken);
        return Ok(categories);
    }

    /// <summary>Tek bir ürünü getirir. Auction servisi de açık artırma açarken burayı çağırıyor.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var product = await _products.GetByIdAsync(id, cancellationToken);
        return Ok(product);
    }

    /// <summary>Yeni ürün oluşturur.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductResponse>> Create(
        [FromBody] CreateProductRequest request,
        CancellationToken cancellationToken)
    {
        var product = await _products.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = product.Id }, product);
    }

    /// <summary>Ürünü tamamen günceller.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductResponse>> Update(
        Guid id,
        [FromBody] UpdateProductRequest request,
        CancellationToken cancellationToken)
    {
        var product = await _products.UpdateAsync(id, request, cancellationToken);
        return Ok(product);
    }

    /// <summary>Ürünü siler. Açık artırmalar ürün verisinin kopyasını tuttuğu için etkilenmez.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _products.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
