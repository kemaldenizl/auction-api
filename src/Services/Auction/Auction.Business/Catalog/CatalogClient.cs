using System.Net;
using System.Net.Http.Json;
using Auction.Core.Exceptions;
using Microsoft.Extensions.Logging;

namespace Auction.Business.Catalog;

/// <summary>Catalog servisinden okunan ürün (sadece ihtiyacımız olan alanlar).</summary>
public sealed record CatalogProduct(
    Guid Id,
    string Name,
    string? Description,
    string? ImageUrl,
    decimal StartingPrice,
    bool IsActive);

public interface ICatalogClient
{
    /// <summary>Ürünü getirir; yoksa <c>null</c> döner. Servise ulaşılamazsa fırlatır.</summary>
    Task<CatalogProduct?> GetProductAsync(Guid productId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Catalog servisine giden typed HttpClient.
/// BaseAddress ve timeout API katmanında (AddHttpClient) yapılandırılır;
/// docker-compose "Services__CatalogApi" ortam değişkeniyle geçiyor.
/// </summary>
public sealed class CatalogClient : ICatalogClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<CatalogClient> _logger;

    public CatalogClient(HttpClient httpClient, ILogger<CatalogClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<CatalogProduct?> GetProductAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"api/products/{productId}", cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new ExternalServiceException(
                    "Catalog",
                    $"beklenmeyen durum kodu {(int)response.StatusCode}");
            }

            return await response.Content.ReadFromJsonAsync<CatalogProduct>(cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "Catalog servisine ulaşılamadı. ProductId: {ProductId}", productId);
            throw new ExternalServiceException("Catalog", exception.Message);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            // İstemci iptal etmediyse bu timeout'tur.
            _logger.LogError(exception, "Catalog servisi zaman aşımına uğradı. ProductId: {ProductId}", productId);
            throw new ExternalServiceException("Catalog", "istek zaman aşımına uğradı");
        }
    }
}
