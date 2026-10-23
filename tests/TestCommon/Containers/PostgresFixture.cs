using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace TestCommon.Containers;

/// <summary>
/// Testler için tek kullanımlık Postgres container'ı (Testcontainers).
///
/// Neden in-memory veritabanı değil: bu projede test edilmesi gereken şeylerin
/// çoğu Postgres'e özgü — <c>SELECT … FOR UPDATE</c> satır kilidi, gerçek
/// transaction davranışı, <c>ILIKE</c>. EF'in InMemory sağlayıcısı bunların
/// hiçbirini taklit etmez; o sağlayıcıyla yazılan "yeşil" test yanlış güven verir.
///
/// Her servise ayrı database açıyoruz. Sebep teknik: iki servisin de MassTransit
/// outbox tabloları var (InboxState, OutboxMessage, OutboxState); aynı veritabanına
/// migrate edilirse ikinci migration çakışır.
///
/// Docker çalışır durumda olmalı. Container test oturumu sonunda silinir.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string CatalogDatabase = "catalog_test";
    private const string AuctionDatabase = "auction_test";

    // İmaj sürümü üretimdekiyle aynı: testin ortamı gerçeğe benzemezse değeri düşer.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16")
        .WithDatabase("postgres")
        .WithUsername("postgres")
        .WithPassword("testpass")
        .Build();

    public string CatalogConnectionString { get; private set; } = string.Empty;

    public string AuctionConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await _container.ExecScriptAsync($"CREATE DATABASE {CatalogDatabase};");
        await _container.ExecScriptAsync($"CREATE DATABASE {AuctionDatabase};");

        CatalogConnectionString = WithDatabase(CatalogDatabase);
        AuctionConnectionString = WithDatabase(AuctionDatabase);
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private string WithDatabase(string database) =>
        new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = database,
            // Eşzamanlılık testleri aynı anda çok bağlantı açıyor.
            MaxPoolSize = 50
        }.ConnectionString;
}

/// <summary>
/// Collection adı. Asıl <c>[CollectionDefinition]</c> her test projesinin
/// kendi içinde tanımlanır — xUnit collection tanımlarını yalnızca test
/// assembly'sinin içinde arar, paylaşılan kütüphanedekini görmez.
/// </summary>
public static class PostgresCollection
{
    public const string Name = "postgres";
}
