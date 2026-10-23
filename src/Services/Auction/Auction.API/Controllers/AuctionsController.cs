using Auction.Business.Auctions;
using Auction.Business.Auctions.Dtos;
using Auction.Core.Paging;
using Microsoft.AspNetCore.Mvc;

namespace Auction.API.Controllers;

/// <summary>
/// Açık artırma uçları.
///
/// Teklif vermenin iki yolu var ve ikisi de aynı iş kurallarını kullanır:
/// bu REST ucu ve SignalR hub'ının <c>PlaceBid</c> metodu. Hangisi kullanılırsa
/// kullanılsın odadaki herkese <c>BidPlaced</c> olayı gider — Swagger'dan teklif
/// verip tarayıcıdaki React ekranının anında güncellendiğini görebilirsin.
/// </summary>
[ApiController]
[Route("api/auctions")]
[Produces("application/json")]
public sealed class AuctionsController : ControllerBase
{
    private readonly IAuctionService _auctions;

    public AuctionsController(IAuctionService auctions)
    {
        _auctions = auctions;
    }

    /// <summary>Açık artırmaları listeler. Canlı olanlar en üstte gelir.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<AuctionSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AuctionSummaryResponse>>> GetList(
        [FromQuery] AuctionListQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _auctions.GetListAsync(query, cancellationToken);
        return Ok(result);
    }

    /// <summary>Tek açık artırmanın detayı ve son 20 teklifi.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AuctionDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AuctionDetailResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var detail = await _auctions.GetDetailAsync(id, cancellationToken);
        return Ok(detail);
    }

    /// <summary>Teklif geçmişi (yeniden eskiye).</summary>
    [HttpGet("{id:guid}/bids")]
    [ProducesResponseType(typeof(IReadOnlyList<BidResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<BidResponse>>> GetBids(
        Guid id,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        var bids = await _auctions.GetBidsAsync(id, take, cancellationToken);
        return Ok(bids);
    }

    /// <summary>
    /// Yeni açık artırma açar. ProductId Catalog servisinden doğrulanır ve
    /// ürün bilgileri buraya kopyalanır (snapshot).
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(AuctionDetailResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<AuctionDetailResponse>> Create(
        [FromBody] CreateAuctionRequest request,
        CancellationToken cancellationToken)
    {
        var auction = await _auctions.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = auction.Id }, auction);
    }

    /// <summary>
    /// Teklif verir (REST yolu). Başarılıysa odadaki tüm WebSocket istemcilerine
    /// <c>BidPlaced</c>, geçilen kullanıcıya <c>Outbid</c> olayı gider.
    /// </summary>
    /// <response code="409">
    /// Teklif reddedildi. <c>errorCode</c> alanına bakın:
    /// <c>bid_too_low</c> (bu durumda <c>minimumRequired</c> da döner),
    /// <c>auction_not_started</c>, <c>auction_ended</c>, <c>already_highest_bidder</c>.
    /// </response>
    [HttpPost("{id:guid}/bids")]
    [ProducesResponseType(typeof(PlaceBidResult), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PlaceBidResult>> PlaceBid(
        Guid id,
        [FromBody] PlaceBidRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _auctions.PlaceBidAsync(id, request.Amount, request.ToIdentity(), cancellationToken);
        return CreatedAtAction(nameof(GetBids), new { id }, result);
    }

    /// <summary>
    /// Planlanmış açık artırmayı hemen canlıya alır. Demo kolaylığı:
    /// başlama saatini beklemeden teklif akışını test edebilmek için.
    /// </summary>
    [HttpPost("{id:guid}/start")]
    [ProducesResponseType(typeof(AuctionDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuctionDetailResponse>> StartNow(Guid id, CancellationToken cancellationToken)
    {
        var detail = await _auctions.StartNowAsync(id, cancellationToken);
        return Ok(detail);
    }

    /// <summary>Açık artırmayı iptal eder.</summary>
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(typeof(AuctionDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuctionDetailResponse>> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var detail = await _auctions.CancelAsync(id, cancellationToken);
        return Ok(detail);
    }
}
