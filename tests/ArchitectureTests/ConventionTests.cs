using System.Reflection;
using Auction.Business.Auctions;
using Catalog.Business.Products;
using MassTransit;
using Xunit;

namespace ArchitectureTests;

/// <summary>
/// Kodlama kuralları. Hepsi gözden kaçması kolay, bozulduğunda
/// anlaşılması zor şeyler.
/// </summary>
public sealed class ConventionTests
{
    [Theory]
    [InlineData(typeof(IAuctionService))]
    [InlineData(typeof(IProductService))]
    public void Servis_arayuzlerindeki_tum_async_metotlar_CancellationToken_alir(Type serviceInterface)
    {
        // İstemci bağlantıyı kapattığında iş sürüyorsa boşa kaynak yakıyoruz.
        // Bir metot token almayı atlarsa iptal zinciri orada kopar.
        var missing = serviceInterface.GetMethods()
            .Where(method => typeof(Task).IsAssignableFrom(method.ReturnType))
            .Where(method => method.GetParameters().All(p => p.ParameterType != typeof(CancellationToken)))
            .Select(method => method.Name)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"{serviceInterface.Name} içindeki şu metotlar CancellationToken almıyor: {string.Join(", ", missing)}");
    }

    [Fact]
    public void Tum_mesaj_tuketicileri_sealed_ve_API_katmanindadir()
    {
        var assemblies = new[]
        {
            typeof(Auction.API.Messaging.ProductUpdatedConsumer).Assembly,
            typeof(Catalog.API.Messaging.AuctionEndedConsumer).Assembly
        };

        var consumers = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.IsClass && !type.IsAbstract)
            .Where(type => type.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConsumer<>)))
            .ToArray();

        Assert.NotEmpty(consumers);

        foreach (var consumer in consumers)
        {
            Assert.True(consumer.IsSealed, $"{consumer.Name} sealed olmalı.");

            // Tüketiciler controller/hub gibi birer adaptör: API katmanında dururlar,
            // iş kuralı taşımazlar. Business'a sızarsa Business MassTransit'e bağlanır.
            Assert.True(
                consumer.Namespace?.Contains(".API.") == true,
                $"{consumer.Name} API katmanında olmalı, şu an: {consumer.Namespace}");
        }
    }

    [Fact]
    public void EF_konfigurasyonlari_internal_sealed_tir()
    {
        var assemblies = new[]
        {
            typeof(Auction.Repository.AuctionDbContext).Assembly,
            typeof(Catalog.Repository.CatalogDbContext).Assembly
        };

        var configurations = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.IsClass && !type.IsAbstract)
            .Where(type => type.GetInterfaces().Any(i =>
                i.IsGenericType &&
                i.GetGenericTypeDefinition().FullName == "Microsoft.EntityFrameworkCore.IEntityTypeConfiguration`1"))
            .ToArray();

        Assert.NotEmpty(configurations);

        foreach (var configuration in configurations)
        {
            // Dışarıdan kullanılmaları gerekmiyor: DbContext bunları
            // ApplyConfigurationsFromAssembly ile reflection'la buluyor.
            Assert.False(configuration.IsPublic, $"{configuration.Name} internal olmalı.");
            Assert.True(configuration.IsSealed, $"{configuration.Name} sealed olmalı.");
        }
    }

    [Fact]
    public void DbContext_ler_IUnitOfWork_u_uygular()
    {
        // Business katmanı DbContext'i değil IUnitOfWork'ü görüyor.
        Assert.True(typeof(Auction.Core.Abstractions.IUnitOfWork)
            .IsAssignableFrom(typeof(Auction.Repository.AuctionDbContext)));

        Assert.True(typeof(Catalog.Core.Abstractions.IUnitOfWork)
            .IsAssignableFrom(typeof(Catalog.Repository.CatalogDbContext)));
    }

    [Fact]
    public void Entity_ler_API_disina_DTO_olarak_sizmaz()
    {
        // Controller imzalarında entity dönülürse veritabanı şeması
        // istemciye sözleşme olarak açılmış olur — sonra değiştirilemez.
        var controllerAssemblies = new[]
        {
            typeof(Auction.API.Controllers.AuctionsController).Assembly,
            typeof(Catalog.API.Controllers.ProductsController).Assembly
        };

        var entityNamespaces = new[] { "Auction.Entity", "Catalog.Entity" };

        var violations = new List<string>();

        foreach (var assembly in controllerAssemblies)
        {
            var controllers = assembly.GetTypes()
                .Where(type => type.Name.EndsWith("Controller", StringComparison.Ordinal));

            foreach (var controller in controllers)
            {
                foreach (var method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    var types = method.GetParameters()
                        .Select(parameter => parameter.ParameterType)
                        .Append(method.ReturnType)
                        .SelectMany(type => type.IsGenericType ? type.GetGenericArguments().Append(type) : [type]);

                    foreach (var type in types)
                    {
                        if (entityNamespaces.Any(ns => type.Namespace?.StartsWith(ns, StringComparison.Ordinal) == true)
                            && !type.IsEnum)
                        {
                            violations.Add($"{controller.Name}.{method.Name} -> {type.Name}");
                        }
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Controller imzalarında entity kullanılmış: {string.Join(", ", violations)}");
    }
}
