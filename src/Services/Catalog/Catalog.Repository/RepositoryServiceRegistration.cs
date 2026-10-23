using Catalog.Core.Abstractions;
using Catalog.Repository.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Catalog.Repository;

public static class RepositoryServiceRegistration
{
    /// <summary>
    /// Veri katmanını kaydeder. Bağlantı dizesi "ConnectionStrings:CatalogDb"
    /// altından okunur (docker-compose bunu ortam değişkeni olarak geçiyor).
    /// </summary>
    public static IServiceCollection AddCatalogRepository(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("CatalogDb")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:CatalogDb tanımlı değil. docker-compose.yml veya appsettings dosyasını kontrol et.");

        services.AddDbContext<CatalogDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                // Geçici ağ/DB hatalarında otomatik yeniden dene.
                npgsql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);
                npgsql.MigrationsAssembly(typeof(CatalogDbContext).Assembly.FullName);
            });
        });

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CatalogDbContext>());
        services.AddScoped<IProductRepository, ProductRepository>();

        return services;
    }
}
