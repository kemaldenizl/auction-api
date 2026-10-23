using Auction.Repository;
using Auction.Repository.Seed;
using Microsoft.EntityFrameworkCore;

namespace Auction.API.Infrastructure;

public static class DatabaseInitializer
{
    /// <summary>Migration'ları uygular ve demo verisini yükler.</summary>
    public static async Task MigrateAndSeedAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<AuctionDbContext>>();
        var context = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();

        const int maxAttempts = 10;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await context.Database.MigrateAsync();
                logger.LogInformation("Auction migration'ları uygulandı.");

                await AuctionDbSeeder.SeedAsync(context, logger);
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

        throw new InvalidOperationException("Auction veritabanı hazırlanamadı. Postgres ayakta mı?");
    }
}
