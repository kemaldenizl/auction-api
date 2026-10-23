using BuildingBlocks.Contracts;

namespace TestCommon.Fakes;

/// <summary>
/// RabbitMQ/MassTransit yerine yayınlanan mesajları toplayan fake.
/// Servis dışına hangi olayın çıktığını broker olmadan doğrulamayı sağlar.
/// </summary>
public sealed class RecordingEventPublisher : IEventPublisher
{
    public List<object> Published { get; } = [];

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : class
    {
        Published.Add(@event);
        return Task.CompletedTask;
    }

    public IEnumerable<TEvent> OfType<TEvent>() => Published.OfType<TEvent>();

    public TEvent Single<TEvent>() => Published.OfType<TEvent>().Single();

    public bool Any<TEvent>() => Published.OfType<TEvent>().Any();
}
