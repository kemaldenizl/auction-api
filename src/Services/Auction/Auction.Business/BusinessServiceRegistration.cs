using Auction.Business.Auctions;
using Auction.Business.Catalog;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Auction.Business;

public static class BusinessServiceRegistration
{
    public static IServiceCollection AddAuctionBusiness(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IAuctionService, AuctionService>();

        var catalogBaseUrl = configuration["Services:CatalogApi"]
            ?? throw new InvalidOperationException(
                "Services:CatalogApi tanımlı değil. docker-compose.yml içinde http://catalog.api:8080 olarak geçiyor.");

        services.AddHttpClient<ICatalogClient, CatalogClient>(client =>
        {
            // BaseAddress sonunda "/" olmak ZORUNDA; yoksa "api/products/.." yolu
            // son segmenti ezerek yanlış URL üretir.
            client.BaseAddress = new Uri(catalogBaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        return services;
    }
}
