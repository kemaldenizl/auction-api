using Auction.Core.Abstractions;
using StackExchange.Redis;

namespace Auction.API.Infrastructure.Presence;

/// <summary>
/// İzleyici sayacının Redis sürümü: tüm replica'lar aynı sayıyı görür.
/// Oda başına bir SET tutulur, üyeler connection id'lerdir.
///
/// Bilinen sınır: sunucu çökerse o sunucunun connection id'leri sette kalır
/// ve sayı şişer. TTL bunu en kötü senaryoda sınırlar. Üretimde connection
/// başına ayrı TTL'li anahtar + periyodik temizlik tercih edilir;
/// demo için bu yeterli ve davranışı görünür.
/// </summary>
public sealed class RedisPresenceTracker : IPresenceTracker
{
    private static readonly TimeSpan RoomTtl = TimeSpan.FromHours(12);

    private readonly IConnectionMultiplexer _redis;

    public RedisPresenceTracker(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<int> JoinAsync(Guid auctionId, string connectionId, CancellationToken cancellationToken = default)
    {
        var database = _redis.GetDatabase();
        var key = KeyFor(auctionId);

        await database.SetAddAsync(key, connectionId);
        await database.KeyExpireAsync(key, RoomTtl);

        return (int)await database.SetLengthAsync(key);
    }

    public async Task<int> LeaveAsync(Guid auctionId, string connectionId, CancellationToken cancellationToken = default)
    {
        var database = _redis.GetDatabase();
        var key = KeyFor(auctionId);

        await database.SetRemoveAsync(key, connectionId);

        return (int)await database.SetLengthAsync(key);
    }

    public async Task<int> CountAsync(Guid auctionId, CancellationToken cancellationToken = default)
    {
        var database = _redis.GetDatabase();
        return (int)await database.SetLengthAsync(KeyFor(auctionId));
    }

    private static RedisKey KeyFor(Guid auctionId) => $"auction:presence:{auctionId}";
}
