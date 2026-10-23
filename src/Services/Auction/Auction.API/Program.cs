using System.Reflection;
using System.Text.Json.Serialization;
using Auction.API.Hubs;
using Auction.API.Infrastructure;
using Auction.API.Infrastructure.Presence;
using Auction.API.Messaging;
using Auction.API.Middleware;
using Auction.Business;
using BuildingBlocks.Messaging;
using Auction.Business.Auctions.Abstractions;
using Auction.Core.Abstractions;
using Auction.Repository;
using Microsoft.OpenApi.Models;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------- servisler
builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddAuctionRepository(builder.Configuration);
builder.Services.AddAuctionBusiness(builder.Configuration);

// Mesajlaşma: Catalog'daki ürün değişikliklerini dinler,
// açık artırma bitince AuctionEnded olayını yayınlar.
builder.Services.AddEventBus<AuctionDbContext>(
    builder.Configuration,
    bus =>
    {
        bus.AddConsumer<ProductUpdatedConsumer>();
        bus.AddConsumer<ProductDeletedConsumer>();
    });

// Business katmanı "bildirim gönder" der; SignalR'a çeviren implementasyon burada.
builder.Services.AddSingleton<IAuctionNotifier, SignalRAuctionNotifier>();

builder.Services.AddScoped<ExceptionHandlingMiddleware>();
builder.Services.AddHealthChecks();

// ---------------------------------------------------------------- SignalR
var signalR = builder.Services
    .AddSignalR(options =>
    {
        options.EnableDetailedErrors = builder.Environment.IsDevelopment();

        // İstemci bu süre boyunca hiç mesaj göndermezse bağlantı düşürülür.
        // KeepAlive bunun yarısından küçük olmalı.
        options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
        options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    })
    .AddJsonProtocol(options =>
    {
        // REST ile aynı davranış: enum'lar string olarak gitsin
        // (JoinAuction cevabındaki "status": "Live" gibi).
        options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

var redisConnectionString = builder.Configuration.GetConnectionString("Redis");

if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    // Backplane: birden fazla instance çalıştığında (--scale auction.api=2)
    // bir sunucudaki yayın diğer sunucudaki bağlantılara da ulaşır.
    signalR.AddStackExchangeRedis(redisConnectionString, options =>
    {
        // Redis geç açılırsa uygulama patlamasın, arka planda bağlanmayı sürdürsün.
        options.Configuration.AbortOnConnectFail = false;
        options.Configuration.ChannelPrefix = RedisChannel.Literal("auction-app");
    });

    builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    {
        var options = ConfigurationOptions.Parse(redisConnectionString);
        options.AbortOnConnectFail = false;
        return ConnectionMultiplexer.Connect(options);
    });

    // İzleyici sayacı da paylaşımlı olmalı, yoksa her replica kendi sayısını gösterir.
    builder.Services.AddSingleton<IPresenceTracker, RedisPresenceTracker>();
}
else
{
    builder.Services.AddSingleton<IPresenceTracker, InMemoryPresenceTracker>();
}

// Saate bağlı Scheduled -> Live -> Ended geçişleri.
builder.Services.AddHostedService<AuctionLifecycleWorker>();

// ---------------------------------------------------------------- CORS
// SignalR için kritik: AllowCredentials ile birlikte joker origin KULLANILAMAZ,
// origin'ler tek tek verilmek zorundadır.
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];

builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// ---------------------------------------------------------------- Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Auction API",
        Version = "v1",
        Description = """
            Canlı açık artırma servisi.

            ### SignalR hub'ı — Swagger bu kısmı göstermez
            Yol: `/hubs/auction` · Kimlik: `?bidderId=<id>&bidderName=<ad>`

            **İstemci -> sunucu (invoke):**
            - `JoinAuction(auctionId)` -> açık artırmanın güncel durumunu döner ve odaya katar
            - `LeaveAuction(auctionId)`
            - `PlaceBid(auctionId, amount)`
            - `WhoAmI()`

            **Sunucu -> istemci (on):**
            - `BidPlaced`, `AuctionStarted`, `AuctionEnded`, `AuctionCancelled`, `ViewerCountChanged` (odaya)
            - `Outbid` (sadece teklifi geçilen kullanıcıya), `BidRejected` (sadece teklifi gönderene)

            REST'teki `POST /api/auctions/{id}/bids` ile hub'ın `PlaceBid` metodu
            aynı iş kurallarını kullanır; ikisi de aynı canlı olayları yayınlar.
            """
    });

    var xmlPath = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }
});

var app = builder.Build();

// ---------------------------------------------------------------- pipeline
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Not: container içinde sadece HTTP dinliyoruz (TLS'i gateway üstlenir),
// bu yüzden UseHttpsRedirection bilinçli olarak yok. WebSocket upgrade'i
// düz HTTP üzerinden sorunsuz çalışır.

// CORS, endpoint eşlemelerinden ÖNCE gelmeli.
app.UseCors("frontend");

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Auction API v1");
    options.DocumentTitle = "Auction API";
});

app.MapControllers();
app.MapHub<AuctionHub>("/hubs/auction").RequireCors("frontend");
app.MapHealthChecks("/health");

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

await DatabaseInitializer.MigrateAndSeedAsync(app);

await app.RunAsync();
