using BuildingBlocks.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Messaging;

public static class EventBusRegistration
{
    /// <summary>
    /// MassTransit'i transactional outbox ile kurar.
    ///
    /// <para><b>Transport seçimi:</b> <c>RabbitMq:Host</c> tanımlıysa RabbitMQ,
    /// değilse in-memory. In-memory seçeneği iki yerde işe yarıyor: broker
    /// olmadan <c>dotnet run</c> yapmak ve testlerde broker ayağa kaldırmamak.
    /// Dikkat: in-memory transport süreç dışına çıkmaz, yani servisler arası
    /// iletişim yalnızca RabbitMQ ile gerçekten çalışır.</para>
    ///
    /// <para><b>Outbox neden gerekli:</b> "veritabanına yaz + mesaj yayınla"
    /// iki ayrı sistemdir; arada çökersek biri olur diğeri olmaz. Outbox ile
    /// mesaj aynı transaction içinde <c>OutboxMessage</c> tablosuna yazılır,
    /// ayrı bir servis onu broker'a taşır. Sonuç: veritabanı değişikliği ile
    /// mesaj ya ikisi birlikte olur ya hiç olmaz. RabbitMQ kapalıysa mesajlar
    /// tabloda birikir, broker dönünce gönderilir — hiçbir olay kaybolmaz.</para>
    ///
    /// <para><b>Inbox:</b> aynı yapılandırma tüketici tarafında idempotency
    /// sağlar; broker "en az bir kez" teslim ettiği için aynı mesaj iki kez
    /// gelebilir, inbox tablosu ikinci işlemeyi engeller.</para>
    /// </summary>
    /// <typeparam name="TDbContext">Outbox/inbox tablolarını barındıran DbContext.</typeparam>
    public static IServiceCollection AddEventBus<TDbContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IBusRegistrationConfigurator>? configureConsumers = null)
        where TDbContext : DbContext
    {
        services.AddMassTransit(bus =>
        {
            // Kuyruk adları tip adından türetilir: ProductUpdatedConsumer -> product-updated
            bus.SetKebabCaseEndpointNameFormatter();

            bus.AddEntityFrameworkOutbox<TDbContext>(outbox =>
            {
                outbox.UsePostgres();

                // IPublishEndpoint çağrılarını outbox'a yönlendirir.
                // Bu satır olmadan Publish doğrudan broker'a gider ve atomiklik kaybolur.
                outbox.UseBusOutbox();

                // Outbox'taki mesajların ne sıklıkla taranacağı.
                outbox.QueryDelay = TimeSpan.FromSeconds(1);
            });

            configureConsumers?.Invoke(bus);

            var host = configuration["RabbitMq:Host"];

            if (string.IsNullOrWhiteSpace(host))
            {
                bus.UsingInMemory((context, cfg) => cfg.ConfigureEndpoints(context));
                return;
            }

            bus.UsingRabbitMq((context, cfg) =>
            {
                var port = ushort.TryParse(configuration["RabbitMq:Port"], out var parsed)
                    ? parsed
                    : (ushort)5672;

                cfg.Host(
                    host,
                    port,
                    configuration["RabbitMq:VirtualHost"] ?? "/",
                    rabbit =>
                    {
                        rabbit.Username(configuration["RabbitMq:Username"] ?? "guest");
                        rabbit.Password(configuration["RabbitMq:Password"] ?? "guest");
                    });

                // Geçici hatalarda tüketiciyi yeniden dene; kalıcı hatada
                // mesaj _error kuyruğuna düşer (orada incelenebilir).
                cfg.UseMessageRetry(retry => retry.Intervals(200, 500, 1_000, 5_000));

                cfg.ConfigureEndpoints(context);
            });
        });

        services.AddScoped<IEventPublisher, MassTransitEventPublisher>();

        return services;
    }
}
