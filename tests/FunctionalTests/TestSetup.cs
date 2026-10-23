using Auction.Business.Catalog;
using Catalog.API.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using TestCommon.Containers;
using Xunit;

namespace FunctionalTests;

[CollectionDefinition(PostgresCollection.Name)]
public sealed class PostgresCollectionDefinition : ICollectionFixture<PostgresFixture>;

/// <summary>
/// Auction API'yi bellek içinde ayağa kaldırır.
///
/// <para><b>Neden <c>WebApplicationFactory&lt;AuctionHub&gt;</c>:</b> top-level
/// statement'lı iki API de global namespace'te <c>Program</c> sınıfı üretiyor;
/// ikisine de referans veren bu projede <c>Program</c> belirsiz kalıyor.
/// <c>WebApplicationFactory&lt;T&gt;</c> T'nin yalnızca assembly'sini kullandığı
/// için o assembly'den herhangi bir tip işi görür.</para>
///
/// <para><b>Neden ortam değişkeni:</b> yapılandırmayı ortam değişkeniyle vermek
/// WebApplicationFactory'nin yapılandırma sırasına bağlı olmayan tek yöntem —
/// <c>WebApplication.CreateBuilder</c> ortam değişkenlerini her durumda okur.
/// Bu yüzden testler paralel çalışmıyor (bkz. AssemblyInfo).</para>
/// </summary>
internal sealed class AuctionApiFactory : WebApplicationFactory<Auction.API.Hubs.AuctionHub>
{
    private readonly string _connectionString;

    public ICatalogClient Catalog { get; } = Substitute.For<ICatalogClient>();

    public AuctionApiFactory(string connectionString)
    {
        _connectionString = connectionString;
        ApplyEnvironment();
    }

    private void ApplyEnvironment()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__AuctionDb", _connectionString);

        // Boş bırakılınca: Redis yerine bellek içi presence, RabbitMQ yerine
        // in-memory transport. Üretim kodundaki bilinçli "broker yoksa in-memory"
        // davranışını kullanıyoruz; teste özel bir dal yok.
        Environment.SetEnvironmentVariable("ConnectionStrings__Redis", string.Empty);
        Environment.SetEnvironmentVariable("RabbitMq__Host", string.Empty);

        Environment.SetEnvironmentVariable("Services__CatalogApi", "http://catalog.invalid");

        // Arka plan servisi testlerin ortasına girmesin.
        // (Otomatik bitirme IntegrationTests'te TryEndDueAsync ile doğrulanıyor.)
        Environment.SetEnvironmentVariable("AuctionLifecycle__IntervalSeconds", "60");
    }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            // Catalog servisine gerçek HTTP çağrısı yapmayalım.
            services.RemoveAll<ICatalogClient>();
            services.AddSingleton(Catalog);
        });
    }
}

/// <summary>Catalog API'yi bellek içinde ayağa kaldırır.</summary>
internal sealed class CatalogApiFactory : WebApplicationFactory<ProductsController>
{
    private readonly string _connectionString;

    public CatalogApiFactory(string connectionString)
    {
        _connectionString = connectionString;
        Environment.SetEnvironmentVariable("ConnectionStrings__CatalogDb", _connectionString);
        Environment.SetEnvironmentVariable("RabbitMq__Host", string.Empty);
    }
}

/// <summary>
/// Sunucu enum'ları string olarak gönderiyor ("status": "Live"), bu yüzden
/// test istemcisinin de aynı converter'ı kullanması gerekiyor —
/// tıpkı frontend'in yapması gerektiği gibi.
/// </summary>
internal static class TestJson
{
    public static readonly System.Text.Json.JsonSerializerOptions Options =
        new(System.Text.Json.JsonSerializerDefaults.Web)
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

    public static Task<T?> ReadAsync<T>(this HttpContent content) =>
        System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<T>(content, Options);

    public static Task<T?> GetJsonAsync<T>(this HttpClient client, string url) =>
        System.Net.Http.Json.HttpClientJsonExtensions.GetFromJsonAsync<T>(client, url, Options);
}
