using Catalog.Repository;
using Catalog.Repository.Seed;
using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Infrastructure;

public static class DatabaseInitializer
{
    /// <summary>
    /// Migration'ları uygular ve demo verisini yükler.
    /// docker-compose postgres'i healthy olana kadar beklediği için normalde
    /// ilk denemede geçer; yerel <c>dotnet run</c> senaryosu için retry bıraktım.
    /// </summary>
    public static async Task MigrateAndSeedAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<CatalogDbContext>>();
        var context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        const int maxAttempts = 10;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await context.Database.MigrateAsync();
                logger.LogInformation("Catalog migration'ları uygulandı.");

                await CatalogDbSeeder.SeedAsync(context, logger);
                return;
            }
            catch (Exception exception) when (attempt < maxAttempts)
            {
                logger.LogWarning(
                    exception,
                    "Veritabanına bağlanılamadı ({Attempt}/{Max}), 3 sn sonra yeniden denenecek.",
                    attempt,
                    maxAttempts);

                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }

        throw new InvalidOperationException("Catalog veritabanı hazırlanamadı. Postgres ayakta mı?");
    }
}
