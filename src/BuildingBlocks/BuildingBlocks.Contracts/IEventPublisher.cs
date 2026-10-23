namespace BuildingBlocks.Contracts;

/// <summary>
/// Servis dışına olay yayınlama soyutlaması.
///
/// Business katmanları yalnızca bunu görür; MassTransit/RabbitMQ bilgisi
/// API katmanındaki implementasyonda kalır. <c>IAuctionNotifier</c> (SignalR)
/// ile aynı mantık: iş kurallarını broker ayağa kaldırmadan test edebilmek için.
///
/// Önemli: transactional outbox açık olduğunda bu çağrı mesajı BROKER'A DEĞİL,
/// aynı veritabanı transaction'ındaki outbox tablosuna yazar. Yani
/// <c>SaveChangesAsync</c> geri alınırsa mesaj da yayınlanmamış olur.
/// Bu yüzden publish çağrısı SaveChanges'ten ÖNCE yapılmalıdır.
/// </summary>
public interface IEventPublisher
{
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : class;
}
