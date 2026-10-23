namespace TestCommon.Fakes;

/// <summary>
/// Auction tarafı için UnitOfWork fake'i.
///
/// Önemli davranış: <c>ExecuteInTransactionAsync</c> verilen işi doğrudan
/// çalıştırır. Yani birim testlerde transaction/kilit YOK — orada sadece iş
/// kurallarını test ediyoruz. Gerçek transaction ve <c>FOR UPDATE</c>
/// davranışı IntegrationTests'te gerçek Postgres ile doğrulanıyor.
/// </summary>
public sealed class FakeAuctionUnitOfWork : Auction.Core.Abstractions.IUnitOfWork
{
    public int SaveChangesCallCount { get; private set; }

    public int TransactionCallCount { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCallCount++;
        return Task.FromResult(1);
    }

    public Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        TransactionCallCount++;
        return operation(cancellationToken);
    }
}

/// <summary>Catalog tarafı için UnitOfWork fake'i (transaction metodu yok).</summary>
public sealed class FakeCatalogUnitOfWork : Catalog.Core.Abstractions.IUnitOfWork
{
    public int SaveChangesCallCount { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCallCount++;
        return Task.FromResult(1);
    }
}
