using System.Collections.Concurrent;
using Auction.Core.Abstractions;

namespace Auction.API.Infrastructure.Presence;

/// <summary>
/// Tek instance için izleyici sayacı.
///
/// Ölçeklenince (docker compose up --scale auction.api=2) her sunucu sadece
/// kendi bağlantılarını sayar, yani kullanıcı yanlış sayı görür.
/// O senaryo için <see cref="RedisPresenceTracker"/> devreye girer;
/// hangisinin kullanıldığına Program.cs karar verir.
/// </summary>
public sealed class InMemoryPresenceTracker : IPresenceTracker
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, byte>> _rooms = new();

    public Task<int> JoinAsync(Guid auctionId, string connectionId, CancellationToken cancellationToken = default)
    {
        var room = _rooms.GetOrAdd(auctionId, _ => new ConcurrentDictionary<string, byte>());
        room[connectionId] = 0;

        return Task.FromResult(room.Count);
    }

    public Task<int> LeaveAsync(Guid auctionId, string connectionId, CancellationToken cancellationToken = default)
    {
        if (!_rooms.TryGetValue(auctionId, out var room))
        {
            return Task.FromResult(0);
        }

        room.TryRemove(connectionId, out _);

        if (room.IsEmpty)
        {
            _rooms.TryRemove(auctionId, out _);
            return Task.FromResult(0);
        }

        return Task.FromResult(room.Count);
    }

    public Task<int> CountAsync(Guid auctionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_rooms.TryGetValue(auctionId, out var room) ? room.Count : 0);
}
