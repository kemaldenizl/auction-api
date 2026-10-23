using Catalog.Core.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Repository.Repositories;

/// <summary>
/// <see cref="IRepository{TEntity}"/>'nin EF Core implementasyonu.
/// Entity'ye özel sorgular için bundan türeyip metot eklenir.
/// </summary>
public class EfRepository<TEntity> : IRepository<TEntity>
    where TEntity : class, IEntity
{
    protected readonly CatalogDbContext Context;

    public EfRepository(CatalogDbContext context)
    {
        Context = context;
    }

    public IQueryable<TEntity> Query() => Context.Set<TEntity>().AsNoTracking();

    public Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Context.Set<TEntity>().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) =>
        Context.Set<TEntity>().AnyAsync(e => e.Id == id, cancellationToken);

    public async Task AddAsync(TEntity entity, CancellationToken cancellationToken = default) =>
        await Context.Set<TEntity>().AddAsync(entity, cancellationToken);

    public void Update(TEntity entity) => Context.Set<TEntity>().Update(entity);

    public void Remove(TEntity entity) => Context.Set<TEntity>().Remove(entity);
}
