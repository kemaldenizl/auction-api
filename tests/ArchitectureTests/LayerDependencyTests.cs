using System.Reflection;
using Xunit;

namespace ArchitectureTests;

/// <summary>
/// Katman kurallarını derlenmiş assembly'lerin referanslarından doğrular.
///
/// Neden değerli: bu kuralların hepsi insan hatasıyla sessizce bozulur.
/// Birisi Business katmanına <c>using Microsoft.AspNetCore.SignalR;</c> ekler,
/// kod derlenir, çalışır — ama katmanlama ölmüş olur ve bunu kimse fark etmez.
/// Bu testler o anı yakalar.
///
/// Not: <see cref="Assembly.GetReferencedAssemblies"/> derleyicinin gerçekten
/// KULLANILAN referanslarını verir. Yani "proje referansı var ama kullanılmıyor"
/// durumu testi kırmaz — zaten istediğimiz de gerçek bağımlılıkları ölçmek.
/// </summary>
public sealed class LayerDependencyTests
{
    // Her katmandan birer tip: assembly'leri bunlar üzerinden buluyoruz.
    private static readonly Assembly AuctionCore = typeof(Auction.Core.Abstractions.IRepository<>).Assembly;
    private static readonly Assembly AuctionEntity = typeof(Auction.Entity.Auctions.AuctionItem).Assembly;
    private static readonly Assembly AuctionRepository = typeof(Auction.Repository.AuctionDbContext).Assembly;
    private static readonly Assembly AuctionBusiness = typeof(Auction.Business.Auctions.AuctionService).Assembly;
    private static readonly Assembly AuctionApi = typeof(Auction.API.Hubs.AuctionHub).Assembly;

    private static readonly Assembly CatalogCore = typeof(Catalog.Core.Abstractions.IRepository<>).Assembly;
    private static readonly Assembly CatalogEntity = typeof(Catalog.Entity.Products.Product).Assembly;
    private static readonly Assembly CatalogRepository = typeof(Catalog.Repository.CatalogDbContext).Assembly;
    private static readonly Assembly CatalogBusiness = typeof(Catalog.Business.Products.ProductService).Assembly;
    private static readonly Assembly CatalogApi = typeof(Catalog.API.Messaging.AuctionEndedConsumer).Assembly;

    private static readonly Assembly Contracts = typeof(BuildingBlocks.Contracts.IEventPublisher).Assembly;

    /// <summary><c>init</c> accessor'ı mı, gerçek <c>set</c> mi?</summary>
    private static bool IsInitOnly(PropertyInfo property) =>
        property.SetMethod?.ReturnParameter
            .GetRequiredCustomModifiers()
            .Any(modifier => modifier.FullName == "System.Runtime.CompilerServices.IsExternalInit") == true;

    private static string[] ReferencesOf(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

    private static void AssertDoesNotReference(Assembly assembly, params string[] forbiddenPrefixes)
    {
        var references = ReferencesOf(assembly);

        var violations = references
            .Where(name => forbiddenPrefixes.Any(prefix =>
                name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .Distinct()
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"{assembly.GetName().Name} şunlara bağımlı olmamalı: {string.Join(", ", violations)}");
    }

    // ------------------------------------------------------- Core en içte olmalı

    [Fact]
    public void Core_katmani_dis_katmanlari_ve_EF_Core_u_tanimaz()
    {
        // Core saf soyutlama: entity bilmez, ORM bilmez.
        // Bozulursa "IRepository<Product>" gibi bir imza Core'a sızmış demektir.
        AssertDoesNotReference(AuctionCore,
            "Auction.Entity", "Auction.Repository", "Auction.Business", "Auction.API",
            "Microsoft.EntityFrameworkCore", "Npgsql", "MassTransit", "Microsoft.AspNetCore");

        AssertDoesNotReference(CatalogCore,
            "Catalog.Entity", "Catalog.Repository", "Catalog.Business", "Catalog.API",
            "Microsoft.EntityFrameworkCore", "Npgsql", "MassTransit", "Microsoft.AspNetCore");
    }

    [Fact]
    public void Entity_katmani_ORM_e_ve_dis_katmanlara_bagimli_degildir()
    {
        // Entity'ler POCO kalmalı: EF konfigürasyonu Repository'de attribute'larla değil
        // IEntityTypeConfiguration ile yapılıyor, böylece domain temiz kalıyor.
        AssertDoesNotReference(AuctionEntity,
            "Auction.Repository", "Auction.Business", "Auction.API",
            "Microsoft.EntityFrameworkCore", "Npgsql", "MassTransit");

        AssertDoesNotReference(CatalogEntity,
            "Catalog.Repository", "Catalog.Business", "Catalog.API",
            "Microsoft.EntityFrameworkCore", "Npgsql", "MassTransit");
    }

    // ----------------------------------------------- Business altyapı tanımamalı

    [Fact]
    public void Business_katmani_SignalR_ve_MassTransit_tanimaz()
    {
        // Projenin en önemli tasarım kararı bu:
        // canlı bildirim IAuctionNotifier, mesajlaşma IEventPublisher arkasında.
        // Bu sayede iş kuralları WebSocket/broker olmadan test edilebiliyor
        // (bkz. UnitTests — 58 test, hiçbiri altyapı ayağa kaldırmıyor).
        AssertDoesNotReference(AuctionBusiness,
            "Microsoft.AspNetCore.SignalR", "MassTransit", "Auction.API", "Yarp");

        AssertDoesNotReference(CatalogBusiness,
            "Microsoft.AspNetCore.SignalR", "MassTransit", "Catalog.API", "Yarp");
    }

    [Fact]
    public void Business_katmani_web_sunucusuna_bagimli_degildir()
    {
        // HTTP kavramları (ControllerBase, HttpContext, ProblemDetails) API katmanının işi.
        // İzin verilen tek istisna Microsoft.Extensions.* (DI, logging, HttpClient factory).
        var forbidden = new[] { "Microsoft.AspNetCore.Mvc", "Microsoft.AspNetCore.Http" };

        AssertDoesNotReference(AuctionBusiness, forbidden);
        AssertDoesNotReference(CatalogBusiness, forbidden);
    }

    [Fact]
    public void Repository_katmani_ust_katmanlari_tanimaz()
    {
        AssertDoesNotReference(AuctionRepository, "Auction.Business", "Auction.API");
        AssertDoesNotReference(CatalogRepository, "Catalog.Business", "Catalog.API");
    }

    // ------------------------------------------------- servisler birbirini tanımaz

    [Fact]
    public void Catalog_ve_Auction_birbirinin_koduna_bagimli_degildir()
    {
        // Mikroservis sınırı: iletişim sadece HTTP (senkron sorgu) ve
        // paylaşılan mesaj sözleşmeleri (asenkron olay) üzerinden.
        // Doğrudan proje referansı verilirse ayrı deploy edilebilirlik ölür.
        foreach (var assembly in new[] { AuctionCore, AuctionEntity, AuctionRepository, AuctionBusiness, AuctionApi })
        {
            AssertDoesNotReference(assembly, "Catalog.");
        }

        foreach (var assembly in new[] { CatalogCore, CatalogEntity, CatalogRepository, CatalogBusiness, CatalogApi })
        {
            AssertDoesNotReference(assembly, "Auction.");
        }
    }

    // ----------------------------------------------- paylaşılan sözleşmeler ince

    [Fact]
    public void Contracts_projesi_hicbir_altyapiya_bagimli_degildir()
    {
        // İki servisin de referans verdiği tek ortak kod burası.
        // MassTransit buraya sızarsa her servis broker'a derleme zamanında bağlanır
        // ve sözleşme projesi "paylaşılan altyapı"ya dönüşür — istemediğimiz şey.
        AssertDoesNotReference(Contracts,
            "MassTransit", "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore",
            "Npgsql", "Auction.", "Catalog.", "RabbitMQ");
    }

    [Fact]
    public void Contracts_icindeki_mesajlar_degismez_record_tipleridir()
    {
        var messageTypes = Contracts.GetTypes()
            .Where(type => type.IsClass && type.IsPublic && type.Namespace is not null)
            .Where(type => type.Namespace!.Contains(".Catalog") || type.Namespace!.Contains(".Auctions"))
            .ToArray();

        Assert.NotEmpty(messageTypes);

        foreach (var type in messageTypes)
        {
            // record'ların derleyici tarafından üretilen imzası
            Assert.True(
                type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) is not null,
                $"{type.Name} bir record olmalı (mesajlar değişmez olmalı).");

            Assert.True(type.IsSealed, $"{type.Name} sealed olmalı.");

            // Gerçek `set` accessor'ı mesajın tüketici içinde değiştirilmesine izin verir.
            // `init` kabul edilir: nesne kurulduktan sonra değişmez.
            // Reflection'da ikisi de "yazılabilir" görünür; ayrım setter'daki
            // IsExternalInit modifier'ında.
            var mutable = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.SetMethod?.IsPublic == true && !IsInitOnly(property))
                .Select(property => property.Name)
                .ToArray();

            Assert.True(
                mutable.Length == 0,
                $"{type.Name} salt-okunur olmalı; değiştirilebilir property'ler: {string.Join(", ", mutable)}");
        }
    }
}
