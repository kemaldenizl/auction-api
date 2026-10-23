using Auction.Entity.Auctions;

namespace TestCommon.Builders;

/// <summary>
/// Test verisi kurucu. Her testte 12 satır entity kurulumu yapmak yerine
/// sadece o test için anlamlı olan alanı değiştiriyoruz — testin niyeti
/// okununca anlaşılıyor.
/// </summary>
public sealed class AuctionItemBuilder
{
    private Guid _id = Guid.CreateVersion7();
    private Guid _productId = Guid.CreateVersion7();
    private string _title = "Test Ürünü";
    private decimal _startingPrice = 1_000m;
    private decimal _minimumIncrement = 100m;
    private decimal? _currentPrice;
    private int _bidCount;
    private string? _highestBidderId;
    private string? _highestBidderName;
    private AuctionStatus _status = AuctionStatus.Live;
    private DateTimeOffset _startsAt = DateTimeOffset.UtcNow.AddMinutes(-10);
    private DateTimeOffset _endsAt = DateTimeOffset.UtcNow.AddHours(1);

    public static AuctionItemBuilder Live() => new();

    public static AuctionItemBuilder Scheduled() => new AuctionItemBuilder()
        .WithStatus(AuctionStatus.Scheduled)
        .WithWindow(DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow.AddHours(2));

    public static AuctionItemBuilder Ended() => new AuctionItemBuilder()
        .WithStatus(AuctionStatus.Ended)
        .WithWindow(DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow.AddHours(-1));

    public static AuctionItemBuilder Cancelled() => new AuctionItemBuilder()
        .WithStatus(AuctionStatus.Cancelled);

    public AuctionItemBuilder WithId(Guid id) { _id = id; return this; }

    public AuctionItemBuilder WithProductId(Guid productId) { _productId = productId; return this; }

    public AuctionItemBuilder WithTitle(string title) { _title = title; return this; }

    public AuctionItemBuilder WithStatus(AuctionStatus status) { _status = status; return this; }

    public AuctionItemBuilder WithPricing(decimal startingPrice, decimal minimumIncrement)
    {
        _startingPrice = startingPrice;
        _minimumIncrement = minimumIncrement;
        return this;
    }

    public AuctionItemBuilder WithWindow(DateTimeOffset startsAt, DateTimeOffset endsAt)
    {
        _startsAt = startsAt;
        _endsAt = endsAt;
        return this;
    }

    /// <summary>Mevcut bir en yüksek teklif varmış gibi kurar.</summary>
    public AuctionItemBuilder WithLeadingBid(decimal amount, string bidderId, string bidderName = "Lider", int bidCount = 1)
    {
        _currentPrice = amount;
        _highestBidderId = bidderId;
        _highestBidderName = bidderName;
        _bidCount = bidCount;
        return this;
    }

    public AuctionItem Build() => new()
    {
        Id = _id,
        ProductId = _productId,
        Title = _title,
        StartingPrice = _startingPrice,
        MinimumIncrement = _minimumIncrement,
        CurrentPrice = _currentPrice ?? _startingPrice,
        BidCount = _bidCount,
        HighestBidderId = _highestBidderId,
        HighestBidderName = _highestBidderName,
        Status = _status,
        StartsAt = _startsAt,
        EndsAt = _endsAt
    };
}
