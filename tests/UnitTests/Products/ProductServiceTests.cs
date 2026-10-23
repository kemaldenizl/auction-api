using BuildingBlocks.Contracts.Catalog;
using Catalog.Business.Products;
using Catalog.Business.Products.Dtos;
using Catalog.Core.Exceptions;
using Catalog.Entity.Products;
using Catalog.Repository.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TestCommon.Fakes;
using Xunit;

namespace UnitTests.Products;

public sealed class ProductServiceTests
{
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly FakeCatalogUnitOfWork _unitOfWork = new();
    private readonly RecordingEventPublisher _events = new();
    private readonly ProductService _service;

    public ProductServiceTests()
    {
        _service = new ProductService(
            _products,
            _unitOfWork,
            _events,
            NullLogger<ProductService>.Instance);
    }

    private static Product Sample(bool isActive = true) => new()
    {
        Id = Guid.CreateVersion7(),
        Name = "Omega Seamaster",
        Category = "Saat",
        StartingPrice = 42_000m,
        IsActive = isActive
    };

    [Fact]
    public async Task Create_ayni_isimde_urun_varsa_reddedilir()
    {
        _products.NameExistsAsync("Omega Seamaster", null, Arg.Any<CancellationToken>()).Returns(true);

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(
            () => _service.CreateAsync(new CreateProductRequest
            {
                Name = "Omega Seamaster",
                Category = "Saat",
                StartingPrice = 1_000m
            }));

        Assert.Equal("duplicate_product_name", exception.ErrorCode);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Create_bosluklari_temizler()
    {
        _products.NameExistsAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(false);

        var response = await _service.CreateAsync(new CreateProductRequest
        {
            Name = "  Omega Seamaster  ",
            Category = "  Saat  ",
            StartingPrice = 1_000m
        });

        Assert.Equal("Omega Seamaster", response.Name);
        Assert.Equal("Saat", response.Category);
    }

    [Fact]
    public async Task Create_olay_yayinlamaz()
    {
        _products.NameExistsAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(false);

        await _service.CreateAsync(new CreateProductRequest
        {
            Name = "Yeni Ürün",
            Category = "Test",
            StartingPrice = 100m
        });

        // Yeni ürünün Auction tarafında karşılığı yok; tüketicisi olmayan
        // olay yayınlamıyoruz. Gereksiz mesaj gereksiz kuyruk demek.
        Assert.Empty(_events.Published);
    }

    [Fact]
    public async Task Update_ProductUpdated_olayini_yayinlar()
    {
        var product = Sample();
        _products.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        _products.NameExistsAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(false);

        await _service.UpdateAsync(product.Id, new UpdateProductRequest
        {
            Name = "Omega Seamaster 300",
            Description = "güncellendi",
            Category = "Saat",
            StartingPrice = 45_000m,
            ImageUrl = "https://example.com/yeni.png",
            IsActive = true
        });

        var message = _events.Single<ProductUpdated>();
        Assert.Equal(product.Id, message.ProductId);
        Assert.Equal("Omega Seamaster 300", message.Name);
        Assert.Equal("güncellendi", message.Description);
        Assert.Equal("https://example.com/yeni.png", message.ImageUrl);
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Update_isim_cakisirsa_olay_yayinlamaz()
    {
        var product = Sample();
        _products.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        _products.NameExistsAsync("Baska Urun", product.Id, Arg.Any<CancellationToken>()).Returns(true);

        await Assert.ThrowsAsync<BusinessRuleException>(
            () => _service.UpdateAsync(product.Id, new UpdateProductRequest
            {
                Name = "Baska Urun",
                Category = "Saat",
                StartingPrice = 1_000m
            }));

        Assert.Empty(_events.Published);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Delete_ProductDeleted_olayini_yayinlar()
    {
        var product = Sample();
        _products.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);

        await _service.DeleteAsync(product.Id);

        var message = _events.Single<ProductDeleted>();
        Assert.Equal(product.Id, message.ProductId);
        _products.Received(1).Remove(product);
    }

    [Fact]
    public async Task Delete_urun_yoksa_NotFound_firlatir_ve_olay_yayinlamaz()
    {
        var missingId = Guid.CreateVersion7();
        _products.GetByIdAsync(missingId, Arg.Any<CancellationToken>()).Returns((Product?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _service.DeleteAsync(missingId));

        Assert.Empty(_events.Published);
    }

    // --- MarkAsSoldAsync: mesaj tüketicisinden çağrılıyor, ASLA fırlatmamalı ---

    [Fact]
    public async Task MarkAsSold_aktif_urunu_pasife_ceker()
    {
        var product = Sample();
        _products.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);

        var changed = await _service.MarkAsSoldAsync(product.Id);

        Assert.True(changed);
        Assert.False(product.IsActive);
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task MarkAsSold_urun_yoksa_firlatmaz()
    {
        var missingId = Guid.CreateVersion7();
        _products.GetByIdAsync(missingId, Arg.Any<CancellationToken>()).Returns((Product?)null);

        // Fırlatsaydı mesaj sonsuz yeniden denenir, sonunda error kuyruğuna düşerdi.
        var changed = await _service.MarkAsSoldAsync(missingId);

        Assert.False(changed);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task MarkAsSold_zaten_pasifse_idempotenttir()
    {
        var product = Sample(isActive: false);
        _products.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);

        // Broker "en az bir kez" teslim eder: aynı mesaj iki kez gelebilir.
        var changed = await _service.MarkAsSoldAsync(product.Id);

        Assert.False(changed);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }
}
