using Auction.Core.Abstractions;
using Auction.Repository.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Auction.Repository;

public static class RepositoryServiceRegistration
{
    /// <summary>
    /// Veri katmanını kaydeder. Bağlantı dizesi "ConnectionStrings:AuctionDb"
    /// altından okunur (docker-compose bunu ortam değişkeni olarak geçiyor).
    /// </summary>
    public static IServiceCollection AddAuctionRepository(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("AuctionDb")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:AuctionDb tanımlı değil. docker-compose.yml veya appsettings dosyasını kontrol et.");

        services.AddDbContext<AuctionDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);
                npgsql.MigrationsAssembly(typeof(AuctionDbContext).Assembly.FullName);
            });
        });

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AuctionDbContext>());
        services.AddScoped<IAuctionItemRepository, AuctionItemRepository>();
        services.AddScoped<IBidRepository, BidRepository>();

        return services;
    }
}
