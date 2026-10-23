using Auction.Entity.Auctions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auction.Repository.Configurations;

internal sealed class AuctionItemConfiguration : IEntityTypeConfiguration<AuctionItem>
{
    public void Configure(EntityTypeBuilder<AuctionItem> builder)
    {
        // Tablo adı sabitleniyor: teklif akışındaki ham SQL kilidi
        // (SELECT ... FOR UPDATE) bu isme bağlı.
        builder.ToTable("AuctionItems");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Title)
            .IsRequired()
            .HasMaxLength(250);

        builder.Property(a => a.Description)
            .HasMaxLength(2000);

        builder.Property(a => a.ImageUrl)
            .HasMaxLength(500);

        builder.Property(a => a.StartingPrice).HasPrecision(18, 2);
        builder.Property(a => a.MinimumIncrement).HasPrecision(18, 2);
        builder.Property(a => a.CurrentPrice).HasPrecision(18, 2);

        builder.Property(a => a.HighestBidderId).HasMaxLength(100);
        builder.Property(a => a.HighestBidderName).HasMaxLength(150);

        // Enum veritabanında int olarak durur (JSON'da string'e çevriliyor).
        builder.Property(a => a.Status)
            .HasConversion<int>()
            .IsRequired();

        // Lifecycle worker'ın sorgusu: Status + zaman.
        builder.HasIndex(a => new { a.Status, a.StartsAt });
        builder.HasIndex(a => new { a.Status, a.EndsAt });
        builder.HasIndex(a => a.ProductId);

        builder.HasMany(a => a.Bids)
            .WithOne(b => b.AuctionItem!)
            .HasForeignKey(b => b.AuctionItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class BidConfiguration : IEntityTypeConfiguration<Bid>
{
    public void Configure(EntityTypeBuilder<Bid> builder)
    {
        builder.ToTable("Bids");

        builder.HasKey(b => b.Id);

        builder.Property(b => b.Amount).HasPrecision(18, 2);

        builder.Property(b => b.BidderId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(b => b.BidderName)
            .IsRequired()
            .HasMaxLength(150);

        // Teklif geçmişi: en yeni önce. Composite index tam bu sorgu için.
        builder.HasIndex(b => new { b.AuctionItemId, b.CreatedAt });
    }
}
