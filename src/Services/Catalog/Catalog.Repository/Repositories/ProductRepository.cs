using Catalog.Core.Abstractions;
using Catalog.Entity.Products;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Repository.Repositories;

public interface IProductRepository : IRepository<Product>
{
    /// <summary>Aynı isimde başka bir ürün var mı? (kendisi hariç)</summary>
    Task<bool> NameExistsAsync(string name, Guid? excludeId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken = default);
}

public sealed class ProductRepository : EfRepository<Product>, IProductRepository
{
    public ProductRepository(CatalogDbContext context) : base(context)
    {
    }

    public Task<bool> NameExistsAsync(string name, Guid? excludeId, CancellationToken cancellationToken = default)
    {
        var query = Context.Products.Where(p => p.Name.ToLower() == name.ToLower());

        if (excludeId is not null)
        {
            query = query.Where(p => p.Id != excludeId.Value);
        }

        return query.AnyAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
        await Context.Products
            .Select(p => p.Category)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(cancellationToken);
}
