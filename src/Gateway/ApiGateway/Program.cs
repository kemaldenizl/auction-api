var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------- servisler

// Rotalar ve cluster'lar appsettings.json -> "ReverseProxy" altında.
// Bildirimsel tutmanın sebebi: yeni servis eklerken kod değiştirmek gerekmiyor.
builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddHealthChecks();

// CORS artık GATEWAY'in işi: tarayıcı sadece buraya konuşuyor.
// (Servislerdeki CORS ayarı doğrudan port erişimi/Swagger için duruyor.)
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
            // SignalR için zorunlu. AllowCredentials ile joker origin kullanılamaz,
            // bu yüzden origin'ler tek tek sayılıyor.
            .AllowCredentials();
    });
});

var app = builder.Build();

// ---------------------------------------------------------------- pipeline
app.UseCors("frontend");

// WebSocket'ler için özel bir ayar gerekmiyor: YARP upgrade isteklerini
// (Connection: Upgrade) otomatik olarak hedefe aktarır. SignalR'ın
// /hubs/auction yolu bu sayede gateway üzerinden çalışır.
app.MapReverseProxy();

app.MapHealthChecks("/health");

// Gateway'in kendi dizini: hangi yol nereye gidiyor.
app.MapGet("/", () => Results.Ok(new
{
    service = "AuctionApp API Gateway",
    routes = new[]
    {
        new { path = "/api/products/**", target = "Catalog API" },
        new { path = "/api/auctions/**", target = "Auction API" },
        new { path = "/hubs/auction", target = "Auction API (SignalR WebSocket)" }
    },
    health = new[] { "/health", "/health/catalog", "/health/auction" },
    // Swagger bilinçli olarak proxy'lenmiyor: Swagger UI varlıkları mutlak
    // yol kullandığı için yol yeniden yazıldığında bozulur.
    swagger = new
    {
        catalog = "http://localhost:5005/swagger",
        auction = "http://localhost:5112/swagger"
    }
}));

await app.RunAsync();
