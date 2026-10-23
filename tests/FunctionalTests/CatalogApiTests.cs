using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Catalog.Business.Products.Dtos;
using Catalog.Core.Paging;
using TestCommon.Containers;
using Xunit;

namespace FunctionalTests;

/// <summary>
/// Catalog API'nin HTTP yüzeyi: durum kodları, doğrulama, hata gövdesi.
/// Birim testler iş kurallarını kapsıyor; burada test edilen şey
/// pipeline'ın kendisi — model binding, ProblemDetails middleware'i, rotalar.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CatalogApiTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private CatalogApiFactory _factory = null!;
    private HttpClient _client = null!;

    public CatalogApiTests(PostgresFixture postgres) => _postgres = postgres;

    public Task InitializeAsync()
    {
        _factory = new CatalogApiFactory(_postgres.CatalogConnectionString);
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private static CreateProductRequest NewProduct(string suffix) => new()
    {
        Name = $"Fonksiyonel Test {suffix}",
        Description = "açıklama",
        Category = "Test",
        StartingPrice = 1_500.50m,
        ImageUrl = "https://example.com/a.png"
    };

    [Fact]
    public async Task Urun_yasam_dongusu_bastan_sona_calisir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // CREATE -> 201 + Location başlığı
        var created = await _client.PostAsJsonAsync("/api/products", NewProduct(suffix));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.NotNull(created.Headers.Location);

        var product = await created.Content.ReadAsync<ProductResponse>();
        Assert.NotNull(product);
        Assert.Equal(1_500.50m, product.StartingPrice);

        // READ
        var fetched = await _client.GetJsonAsync<ProductResponse>($"/api/products/{product.Id}");
        Assert.NotNull(fetched);
        Assert.Equal(product.Name, fetched.Name);

        // UPDATE
        var updated = await _client.PutAsJsonAsync($"/api/products/{product.Id}", new UpdateProductRequest
        {
            Name = $"Fonksiyonel Test {suffix} güncel",
            Category = "Test",
            StartingPrice = 2_000m,
            IsActive = false
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var afterUpdate = await updated.Content.ReadAsync<ProductResponse>();
        Assert.NotNull(afterUpdate);
        Assert.False(afterUpdate.IsActive);
        Assert.NotNull(afterUpdate.UpdatedAt);

        // DELETE -> 204, sonra 404
        var deleted = await _client.DeleteAsync($"/api/products/{product.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var gone = await _client.GetAsync($"/api/products/{product.Id}");
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task Gecersiz_govde_400_ve_alan_bazli_hata_doner()
    {
        var response = await _client.PostAsJsonAsync("/api/products", new
        {
            name = "ab",          // en az 3 karakter
            category = "",        // zorunlu
            startingPrice = -5    // 0'dan büyük olmalı
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        var errors = problem.GetProperty("errors");

        // Her geçersiz alan ayrı ayrı bildirilmeli: istemci formu işaretleyebilsin.
        Assert.True(errors.TryGetProperty("Name", out _));
        Assert.True(errors.TryGetProperty("Category", out _));
        Assert.True(errors.TryGetProperty("StartingPrice", out _));
    }

    [Fact]
    public async Task Ayni_isimde_ikinci_urun_409_ve_errorCode_doner()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var first = await _client.PostAsJsonAsync("/api/products", NewProduct(suffix));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await _client.PostAsJsonAsync("/api/products", NewProduct(suffix));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        // ProblemDetails middleware'i makine-okunur kodu eklemeli.
        var problem = JsonSerializer.Deserialize<JsonElement>(await second.Content.ReadAsStringAsync());
        Assert.Equal("duplicate_product_name", problem.GetProperty("errorCode").GetString());
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Bilinmeyen_id_icin_404_ProblemDetails_doner()
    {
        var response = await _client.GetAsync($"/api/products/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        Assert.Equal("not_found", problem.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Gecersiz_guid_formatinda_rota_eslesmez_404_doner()
    {
        // Rota kısıtı {id:guid} sayesinde controller'a hiç girmiyor.
        var response = await _client.GetAsync("/api/products/not-a-guid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Sayfalama_ve_arama_calisir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        for (var i = 0; i < 3; i++)
        {
            var request = NewProduct($"{suffix}-{i}");
            var response = await _client.PostAsJsonAsync("/api/products", request);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        var page = await _client.GetJsonAsync<PagedResult<ProductResponse>>(
            $"/api/products?search={suffix}&pageSize=2&page=1");

        Assert.NotNull(page);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(2, page.TotalPages);
        Assert.True(page.HasNext);
        Assert.False(page.HasPrevious);
    }

    [Fact]
    public async Task PageSize_ust_sinira_kirpilir()
    {
        // İstemci 10_000 isterse sunucu 100'e indirmeli (DoS yüzeyi).
        var page = await _client.GetJsonAsync<PagedResult<ProductResponse>>(
            "/api/products?pageSize=10000");

        Assert.NotNull(page);
        Assert.Equal(PageRequest.MaxPageSize, page.PageSize);
    }

    [Fact]
    public async Task Health_ve_kok_adres_calisir()
    {
        var health = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("Healthy", await health.Content.ReadAsStringAsync());

        // Kök adres Swagger'a yönlendirir (otomatik takip kapalı).
        using var noRedirect = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var root = await noRedirect.GetAsync("/");
        Assert.Equal(HttpStatusCode.Found, root.StatusCode);
        Assert.Equal("/swagger", root.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Swagger_dokumani_uretilir()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        Assert.True(document.GetProperty("paths").TryGetProperty("/api/products", out _));
    }
}
