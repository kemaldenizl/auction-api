using BuildingBlocks.Contracts;
using MassTransit;

namespace BuildingBlocks.Messaging;

/// <summary>
/// <see cref="IEventPublisher"/>'ın MassTransit implementasyonu.
///
/// Outbox açık olduğu için bu <c>Publish</c> çağrısı mesajı broker'a değil
/// mevcut DbContext'in outbox tablosuna yazar; gerçek gönderim
/// <c>SaveChangesAsync</c> commit edildikten sonra arka plandaki
/// delivery servisi tarafından yapılır.
/// </summary>
internal sealed class MassTransitEventPublisher : IEventPublisher
{
    private readonly IPublishEndpoint _publishEndpoint;

    public MassTransitEventPublisher(IPublishEndpoint publishEndpoint)
    {
        _publishEndpoint = publishEndpoint;
    }

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : class =>
        _publishEndpoint.Publish(@event, cancellationToken);
}
