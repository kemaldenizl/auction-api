using System.Reflection;
using System.Text.Json.Serialization;
using BuildingBlocks.Messaging;
using Catalog.API.Infrastructure;
using Catalog.API.Messaging;
using Catalog.API.Middleware;
using Catalog.Business;
using Catalog.Repository;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------- servisler
builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        // Enum'lar istemciye string gider ("Live" yerine 1 değil).
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddCatalogRepository(builder.Configuration);
builder.Services.AddCatalogBusiness();

// Mesajlaşma: Auction servisinden gelen AuctionEnded olayını dinler,
// ürün değişikliklerini (ProductUpdated/ProductDeleted) yayınlar.
builder.Services.AddEventBus<CatalogDbContext>(
    builder.Configuration,
    bus => bus.AddConsumer<AuctionEndedConsumer>());

builder.Services.AddScoped<ExceptionHandlingMiddleware>();

builder.Services.AddHealthChecks();

// React istemcisi başka porttan geldiği için CORS şart.
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

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Catalog API",
        Version = "v1",
        Description = """
            Açık artırmaya çıkacak ürünlerin kataloğu (CRUD).

            Canlı teklif akışı bu serviste değil, **Auction API**'de (SignalR hub'ı: `/hubs/auction`).
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
// Sıra önemli: hata yakalama en dışta olmalı.
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Not: container içinde sadece HTTP dinliyoruz, TLS'i gateway/ingress üstlenir.
// Bu yüzden UseHttpsRedirection bilinçli olarak yok.

app.UseCors("frontend");

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Catalog API v1");
    options.DocumentTitle = "Catalog API";
});

app.MapControllers();
app.MapHealthChecks("/health");

// Kök adresi Swagger'a yönlendir: konteyner açılınca ne yapacağını aramak zorunda kalmayalım.
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

await DatabaseInitializer.MigrateAndSeedAsync(app);

await app.RunAsync();
