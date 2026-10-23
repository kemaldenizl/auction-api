using System.Linq.Expressions;
using Auction.Business.Auctions.Dtos;
using Auction.Entity.Auctions;

namespace Auction.Business.Auctions.Mapping;

public static class AuctionMapping
{
    /// <summary>
    /// IQueryable projeksiyonu. MinimumNextBid'i entity metodu yerine burada
    /// ifade olarak yazmak zorundayız; metot çağrısı SQL'e çevrilemez.
    /// </summary>
    public static readonly Expression<Func<AuctionItem, AuctionSummaryResponse>> ToSummary =
        auction => new AuctionSummaryResponse(
            auction.Id,
            auction.ProductId,
            auction.Title,
            auction.ImageUrl,
            auction.StartingPrice,
            auction.CurrentPrice,
            auction.MinimumIncrement,
            auction.BidCount == 0 ? auction.StartingPrice : auction.CurrentPrice + auction.MinimumIncrement,
            auction.BidCount,
            auction.HighestBidderName,
            auction.Status,
            auction.StartsAt,
            auction.EndsAt);

    public static AuctionSummaryResponse MapToSummary(this AuctionItem auction) =>
        new(
            auction.Id,
            auction.ProductId,
            auction.Title,
            auction.ImageUrl,
            auction.StartingPrice,
            auction.CurrentPrice,
            auction.MinimumIncrement,
            auction.MinimumNextBid(),
            auction.BidCount,
            auction.HighestBidderName,
            auction.Status,
            auction.StartsAt,
            auction.EndsAt);

    public static AuctionDetailResponse MapToDetail(
        this AuctionItem auction,
        IReadOnlyList<BidResponse> recentBids) =>
        new(
            auction.Id,
            auction.ProductId,
            auction.Title,
            auction.Description,
            auction.ImageUrl,
            auction.StartingPrice,
            auction.CurrentPrice,
            auction.MinimumIncrement,
            auction.MinimumNextBid(),
            auction.BidCount,
            auction.HighestBidderId,
            auction.HighestBidderName,
            auction.Status,
            auction.StartsAt,
            auction.EndsAt,
            recentBids);

    public static BidResponse MapToResponse(this Bid bid) =>
        new(bid.Id, bid.AuctionItemId, bid.Amount, bid.BidderId, bid.BidderName, bid.CreatedAt);
}
