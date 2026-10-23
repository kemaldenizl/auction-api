namespace Catalog.Core.Abstractions;

/// <summary>
/// Okuma/yazma için generic repository.
/// <see cref="Query"/> bilinçli olarak IQueryable döner: filtreleme/sayfalama
/// veritabanında çalışsın, belleğe çekilmesin.
/// </summary>
public interface IRepository<TEntity> where TEntity : class, IEntity
{
    IQueryable<TEntity> Query();

    Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);

    void Update(TEntity entity);

    void Remove(TEntity entity);
}

/// <summary>
/// SaveChanges'i repository'den ayırır: tek bir iş akışında birden fazla
/// repository değişikliği tek transaction'da commit edilebilsin.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
