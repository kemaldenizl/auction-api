using Catalog.Entity.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Repository.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(p => p.Description)
            .HasMaxLength(2000);

        builder.Property(p => p.Category)
            .IsRequired()
            .HasMaxLength(100);

        // Para alanlarında double ASLA kullanılmaz; numeric(18,2).
        builder.Property(p => p.StartingPrice)
            .HasPrecision(18, 2);

        builder.Property(p => p.ImageUrl)
            .HasMaxLength(500);

        // Liste ekranı kategoriye göre filtreliyor.
        builder.HasIndex(p => p.Category);

        // Varsayılan sıralama CreatedAt desc; indeks olmadan tablo taraması olur.
        builder.HasIndex(p => p.CreatedAt);
    }
}
