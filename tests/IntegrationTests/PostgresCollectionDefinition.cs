using TestCommon.Containers;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// Postgres container'ını bu assembly'deki test sınıfları arasında paylaşır.
///
/// Tanımın burada olması zorunlu: xUnit <c>[CollectionDefinition]</c>
/// niteliklerini yalnızca test assembly'sinin içinde tarar, TestCommon gibi
/// paylaşılan bir kütüphanedekini bulamaz.
/// </summary>
[CollectionDefinition(PostgresCollection.Name)]
public sealed class PostgresCollectionDefinition : ICollectionFixture<PostgresFixture>;
