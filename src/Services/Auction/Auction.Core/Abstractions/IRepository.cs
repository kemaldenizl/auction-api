namespace Auction.Core.Abstractions;

/// <summary>
/// Generic repository'nin üzerinde çalışabileceği minimum sözleşme.
/// Core katmanı entity'leri tanımaz; sadece "Id'si olan bir şey" bilir.
/// </summary>
public interface IEntity
{
    Guid Id { get; }
}

/// <summary>
/// Okuma/yazma için generic repository.
/// <see cref="Query"/> IQueryable döner: filtreleme/sayfalama veritabanında çalışır.
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
/// SaveChanges'i repository'den ayırır. Teklif verme akışında birden fazla
/// değişiklik (bid insert + auction update) tek transaction'da commit edilir.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Verilen işi tek bir transaction içinde çalıştırır ve başarılıysa commit eder;
    /// istisna çıkarsa rollback eder.
    ///
    /// Neden elle BeginTransaction değil: bağlantı için "retry on failure"
    /// açık olduğunda EF, elle başlatılan transaction'lara izin vermez —
    /// yeniden denemenin transaction'ın tamamını kapsaması gerekir.
    /// Bu metot işi execution strategy'nin içine alarak ikisini birlikte mümkün kılar.
    /// </summary>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);
}
