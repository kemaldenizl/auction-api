using Auction.Business.Auctions.Abstractions;
using Auction.Business.Auctions.Dtos;
using Auction.Business.Auctions.Events;
using Auction.Business.Auctions.Mapping;
using Auction.Business.Catalog;
using Auction.Core.Abstractions;
using Auction.Core.Exceptions;
using Auction.Core.Paging;
using Auction.Entity.Auctions;
using Auction.Repository.Repositories;
using BuildingBlocks.Contracts;
using BuildingBlocks.Contracts.Auctions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Auction.Business.Auctions;

public sealed class AuctionService : IAuctionService
{
    private const int RecentBidCount = 20;

    private readonly IAuctionItemRepository _auctions;
    private readonly IBidRepository _bids;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuctionNotifier _notifier;
    private readonly ICatalogClient _catalog;
    private readonly IEventPublisher _events;
    private readonly ILogger<AuctionService> _logger;

    public AuctionService(
        IAuctionItemRepository auctions,
        IBidRepository bids,
        IUnitOfWork unitOfWork,
        IAuctionNotifier notifier,
        ICatalogClient catalog,
        IEventPublisher events,
        ILogger<AuctionService> logger)
    {
        _auctions = auctions;
        _bids = bids;
        _unitOfWork = unitOfWork;
        _notifier = notifier;
        _catalog = catalog;
        _events = events;
        _logger = logger;
    }

    // ------------------------------------------------------------------ okuma

    public async Task<PagedResult<AuctionSummaryResponse>> GetListAsync(
        AuctionListQuery query,
        CancellationToken cancellationToken = default)
    {
        var page = query.ToPageRequest();
        var source = _auctions.Query();

        if (query.Status is not null)
        {
            source = source.Where(a => a.Status == query.Status.Value);
        }

        if (query.ProductId is not null)
        {
            source = source.Where(a => a.ProductId == query.ProductId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = $"%{query.Search.Trim()}%";
            source = source.Where(a => EF.Functions.ILike(a.Title, term));
        }

        var totalCount = await source.LongCountAsync(cancellationToken);
        if (totalCount == 0)
        {
            return PagedResult<AuctionSummaryResponse>.Empty(page);
        }

        var items = await source
            // Canlı olanlar üstte, sonra bitişi en yakın olan.
            .OrderBy(a => a.Status == AuctionStatus.Live ? 0 : 1)
            .ThenBy(a => a.EndsAt)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(AuctionMapping.ToSummary)
            .ToListAsync(cancellationToken);

        return new PagedResult<AuctionSummaryResponse>(items, page.Page, page.PageSize, totalCount);
    }

    public async Task<AuctionDetailResponse> GetDetailAsync(
        Guid auctionId,
        CancellationToken cancellationToken = default)
    {
        var auction = await _auctions.Query()
            .FirstOrDefaultAsync(a => a.Id == auctionId, cancellationToken)
            ?? throw new NotFoundException("Açık artırma", auctionId);

        var recentBids = await GetBidsAsync(auctionId, RecentBidCount, cancellationToken);

        return auction.MapToDetail(recentBids);
    }

    public async Task<IReadOnlyList<BidResponse>> GetBidsAsync(
        Guid auctionId,
        int take,
        CancellationToken cancellationToken = default)
    {
        var normalizedTake = Math.Clamp(take, 1, 200);

        var bids = await _bids.GetRecentAsync(auctionId, normalizedTake, cancellationToken);

        return bids.Select(bid => bid.MapToResponse()).ToList();
    }

    // ---------------------------------------------------------------- oluşturma

    public async Task<AuctionDetailResponse> CreateAsync(
        CreateAuctionRequest request,
        CancellationToken cancellationToken = default)
    {
        // Servisler arası doğrulama: ürün gerçekten var mı?
        var product = await _catalog.GetProductAsync(request.ProductId, cancellationToken)
            ?? throw new NotFoundException($"Ürün katalogda bulunamadı: {request.ProductId}");

        if (!product.IsActive)
        {
            throw new BusinessRuleException("Pasif ürün için açık artırma açılamaz.", "product_inactive");
        }

        var now = DateTimeOffset.UtcNow;
        var startsAt = request.StartsAt ?? now;

        // Küçük bir tolerans: istemcinin saati birkaç saniye geride olabilir.
        if (startsAt < now.AddMinutes(-1))
        {
            throw new BusinessRuleException("Başlama zamanı geçmişte olamaz.", "invalid_start_time");
        }

        var startingPrice = request.StartingPrice ?? product.StartingPrice;

        var auction = new AuctionItem
        {
            ProductId = product.Id,
            // Snapshot: Catalog'daki ürün sonra değişse bile burası sabit kalır.
            Title = product.Name,
            Description = product.Description,
            ImageUrl = product.ImageUrl,
            StartingPrice = startingPrice,
            CurrentPrice = startingPrice,
            MinimumIncrement = request.MinimumIncrement,
            StartsAt = startsAt,
            EndsAt = startsAt.AddMinutes(request.DurationMinutes),
            Status = startsAt <= now ? AuctionStatus.Live : AuctionStatus.Scheduled
        };

        await _auctions.AddAsync(auction, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Açık artırma oluşturuldu: {AuctionId} ({Title}), durum {Status}, bitiş {EndsAt:O}",
            auction.Id,
            auction.Title,
            auction.Status,
            auction.EndsAt);

        return auction.MapToDetail([]);
    }

    // ------------------------------------------------------------------- teklif

    public async Task<PlaceBidResult> PlaceBidAsync(
        Guid auctionId,
        decimal amount,
        BidderIdentity bidder,
        CancellationToken cancellationToken = default)
    {
        var bidderId = bidder.Id?.Trim();
        var bidderName = bidder.Name?.Trim();

        if (string.IsNullOrWhiteSpace(bidderId) || string.IsNullOrWhiteSpace(bidderName))
        {
            throw new BusinessRuleException("Teklif sahibi bilgisi eksik.", "missing_bidder");
        }

        if (amount <= 0)
        {
            throw new BusinessRuleException("Teklif tutarı 0'dan büyük olmalı.", "invalid_amount");
        }

        // Kuruş hassasiyetine yuvarla: istemci 1000.12345 gönderebilir.
        amount = Math.Round(amount, 2, MidpointRounding.ToZero);

        var outcome = await _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            // FOR UPDATE: aynı saniyede gelen iki teklif burada sıraya girer.
            var auction = await _auctions.GetForUpdateAsync(auctionId, token)
                ?? throw new NotFoundException("Açık artırma", auctionId);

            var now = DateTimeOffset.UtcNow;

            // Arka plan servisi henüz sırayı almadıysa burada canlıya alıyoruz;
            // saati gelmiş bir açık artırmanın teklifi "başlamadı" diye reddedilmesin.
            if (auction.Status == AuctionStatus.Scheduled && now >= auction.StartsAt)
            {
                auction.Status = AuctionStatus.Live;
            }

            EnsureBiddable(auction, now);

            if (string.Equals(auction.HighestBidderId, bidderId, StringComparison.Ordinal))
            {
                throw new BusinessRuleException("En yüksek teklif zaten sizde.", "already_highest_bidder");
            }

            var minimumRequired = auction.MinimumNextBid();
            if (amount < minimumRequired)
            {
                throw new BidTooLowException(minimumRequired);
            }

            var previousBidderId = auction.HighestBidderId;
            var previousAmount = auction.CurrentPrice;

            var bid = new Bid
            {
                AuctionItemId = auction.Id,
                Amount = amount,
                BidderId = bidderId,
                BidderName = bidderName
            };

            await _bids.AddAsync(bid, token);

            auction.CurrentPrice = amount;
            auction.BidCount += 1;
            auction.HighestBidderId = bidderId;
            auction.HighestBidderName = bidderName;

            await _unitOfWork.SaveChangesAsync(token);

            return new BidOutcome(
                bid.MapToResponse(),
                auction.MapToSummary(),
                auction.Title,
                auction.MinimumNextBid(),
                auction.EndsAt,
                auction.BidCount,
                previousBidderId,
                previousAmount);
        }, cancellationToken);

        // Bildirimler COMMIT SONRASI. Önce yayınlasaydık, transaction geri alınırsa
        // istemciler var olmayan bir teklifi görmüş olurdu.
        await PublishBidEventsAsync(outcome, bidderId, bidderName, cancellationToken);

        _logger.LogInformation(
            "Teklif kabul edildi: {AuctionId} <- {Amount} ({BidderName})",
            auctionId,
            amount,
            bidderName);

        return new PlaceBidResult(outcome.Bid, outcome.Auction);
    }

    private async Task PublishBidEventsAsync(
        BidOutcome outcome,
        string bidderId,
        string bidderName,
        CancellationToken cancellationToken)
    {
        await _notifier.BidPlacedAsync(
            new BidPlacedEvent(
                outcome.Auction.Id,
                outcome.Bid.Id,
                outcome.Bid.Amount,
                outcome.MinimumNextBid,
                bidderId,
                bidderName,
                outcome.BidCount,
                outcome.Bid.CreatedAt,
                outcome.EndsAt),
            cancellationToken);

        // Hedefli bildirim: teklifi geçilen önceki lider.
        if (!string.IsNullOrEmpty(outcome.PreviousBidderId) &&
            !string.Equals(outcome.PreviousBidderId, bidderId, StringComparison.Ordinal))
        {
            await _notifier.OutbidAsync(
                outcome.PreviousBidderId,
                new OutbidEvent(
                    outcome.Auction.Id,
                    outcome.Title,
                    outcome.PreviousAmount,
                    outcome.Bid.Amount,
                    bidderName,
                    outcome.MinimumNextBid),
                cancellationToken);
        }
    }

    private static void EnsureBiddable(AuctionItem auction, DateTimeOffset now)
    {
        switch (auction.Status)
        {
            case AuctionStatus.Scheduled:
                throw new BusinessRuleException(
                    $"Açık artırma henüz başlamadı. Başlangıç: {auction.StartsAt:dd.MM.yyyy HH:mm} UTC",
                    "auction_not_started");

            case AuctionStatus.Cancelled:
                throw new BusinessRuleException("Açık artırma iptal edildi.", "auction_cancelled");

            case AuctionStatus.Ended:
                throw new BusinessRuleException("Açık artırma sona erdi.", "auction_ended");
        }

        // Durum Live olsa bile saat geçmiş olabilir (worker henüz kapatmamış).
        if (now >= auction.EndsAt)
        {
            throw new BusinessRuleException("Açık artırmanın süresi doldu.", "auction_ended");
        }
    }

    // --------------------------------------------------------- durum geçişleri

    public async Task<AuctionDetailResponse> StartNowAsync(
        Guid auctionId,
        CancellationToken cancellationToken = default)
    {
        var auction = await _auctions.GetByIdAsync(auctionId, cancellationToken)
            ?? throw new NotFoundException("Açık artırma", auctionId);

        if (auction.Status == AuctionStatus.Live)
        {
            // Idempotent: iki kez basılırsa hata vermeye gerek yok.
            return auction.MapToDetail(await GetBidsAsync(auctionId, RecentBidCount, cancellationToken));
        }

        if (auction.Status is AuctionStatus.Ended or AuctionStatus.Cancelled)
        {
            throw new BusinessRuleException(
                $"Durumu '{auction.Status}' olan açık artırma başlatılamaz.",
                "invalid_status_transition");
        }

        var now = DateTimeOffset.UtcNow;
        auction.StartsAt = now;
        auction.Status = AuctionStatus.Live;

        if (auction.EndsAt <= now)
        {
            // Planlanan bitiş zaten geçmişse makul bir süre tanı.
            auction.EndsAt = now.AddMinutes(30);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _notifier.AuctionStartedAsync(
            new AuctionStartedEvent(
                auction.Id,
                auction.Title,
                auction.CurrentPrice,
                auction.MinimumNextBid(),
                auction.StartsAt,
                auction.EndsAt),
            cancellationToken);

        _logger.LogInformation("Açık artırma elle başlatıldı: {AuctionId}", auctionId);

        return auction.MapToDetail(await GetBidsAsync(auctionId, RecentBidCount, cancellationToken));
    }

    public async Task<AuctionDetailResponse> CancelAsync(
        Guid auctionId,
        CancellationToken cancellationToken = default)
    {
        var auction = await _auctions.GetByIdAsync(auctionId, cancellationToken)
            ?? throw new NotFoundException("Açık artırma", auctionId);

        if (auction.Status == AuctionStatus.Cancelled)
        {
            return auction.MapToDetail(await GetBidsAsync(auctionId, RecentBidCount, cancellationToken));
        }

        if (auction.Status == AuctionStatus.Ended)
        {
            throw new BusinessRuleException("Sona ermiş açık artırma iptal edilemez.", "invalid_status_transition");
        }

        auction.Status = AuctionStatus.Cancelled;
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _notifier.AuctionCancelledAsync(
            new AuctionCancelledEvent(auction.Id, auction.Title),
            cancellationToken);

        _logger.LogInformation("Açık artırma iptal edildi: {AuctionId}", auctionId);

        return auction.MapToDetail(await GetBidsAsync(auctionId, RecentBidCount, cancellationToken));
    }

    public async Task<bool> TryStartDueAsync(Guid auctionId, CancellationToken cancellationToken = default)
    {
        // Satır kilidi yarışı çözer: birden fazla replica aynı anda denerse
        // ikinci olan birincinin commit'ini bekler, sonra durumu Live görüp çekilir.
        // Yani geçişi ve olay yayınını yalnızca bir instance yapar.
        var started = await _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var auction = await _auctions.GetForUpdateAsync(auctionId, token);

            if (auction is null || auction.Status != AuctionStatus.Scheduled)
            {
                return null;
            }

            auction.Status = AuctionStatus.Live;
            await _unitOfWork.SaveChangesAsync(token);

            return new AuctionStartedEvent(
                auction.Id,
                auction.Title,
                auction.CurrentPrice,
                auction.MinimumNextBid(),
                auction.StartsAt,
                auction.EndsAt);
        }, cancellationToken);

        if (started is null)
        {
            return false;
        }

        await _notifier.AuctionStartedAsync(started, cancellationToken);

        _logger.LogInformation("Açık artırma canlıya alındı: {AuctionId} ({Title})", started.AuctionId, started.Title);

        return true;
    }

    public async Task<bool> TryEndDueAsync(Guid auctionId, CancellationToken cancellationToken = default)
    {
        var outcome = await _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var auction = await _auctions.GetForUpdateAsync(auctionId, token);

            if (auction is null || auction.Status != AuctionStatus.Live || DateTimeOffset.UtcNow < auction.EndsAt)
            {
                return null;
            }

            auction.Status = AuctionStatus.Ended;

            // Servis DIŞINA giden olay: Catalog bunu dinleyip ürünü pasife çeker.
            // Outbox sayesinde bu çağrı mesajı broker'a değil aynı transaction'daki
            // outbox tablosuna yazar — "bitti ama olay gitmedi" ya da tersi olamaz.
            await _events.PublishAsync(
                new AuctionEnded(
                    auction.Id,
                    auction.ProductId,
                    auction.Title,
                    auction.CurrentPrice,
                    auction.HighestBidderId,
                    auction.HighestBidderName,
                    auction.BidCount,
                    DateTimeOffset.UtcNow),
                token);

            await _unitOfWork.SaveChangesAsync(token);

            return new AuctionEndedEvent(
                auction.Id,
                auction.Title,
                auction.CurrentPrice,
                auction.HighestBidderId,
                auction.HighestBidderName,
                auction.BidCount,
                DateTimeOffset.UtcNow);
        }, cancellationToken);

        if (outcome is null)
        {
            return false;
        }

        // Servis İÇİNDEKİ canlı bildirim (WebSocket) commit sonrası.
        await _notifier.AuctionEndedAsync(outcome, cancellationToken);

        _logger.LogInformation(
            "Açık artırma sona erdi: {AuctionId}, kazanan {Winner}, fiyat {Price}",
            outcome.AuctionId,
            outcome.WinnerName ?? "(teklif yok)",
            outcome.FinalPrice);

        return true;
    }

    // ------------------------------------------------- Catalog olaylarına tepki

    public async Task<int> RefreshScheduledSnapshotsAsync(
        ProductSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var scheduled = await _auctions.GetScheduledByProductAsync(snapshot.ProductId, cancellationToken);

        if (scheduled.Count == 0)
        {
            return 0;
        }

        foreach (var auction in scheduled)
        {
            auction.Title = snapshot.Name;
            auction.Description = snapshot.Description;
            auction.ImageUrl = snapshot.ImageUrl;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Ürün güncellendi, {Count} planlanmış açık artırmanın kopyası tazelendi: {ProductId}",
            scheduled.Count,
            snapshot.ProductId);

        return scheduled.Count;
    }

    public async Task<int> CancelScheduledForProductAsync(
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        var scheduled = await _auctions.GetScheduledByProductAsync(productId, cancellationToken);

        if (scheduled.Count == 0)
        {
            return 0;
        }

        foreach (var auction in scheduled)
        {
            auction.Status = AuctionStatus.Cancelled;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Odadaki izleyiciler anında görsün. Zincir şöyle işliyor:
        // Catalog DELETE -> RabbitMQ -> bu tüketici -> veritabanı -> WebSocket.
        foreach (var auction in scheduled)
        {
            await _notifier.AuctionCancelledAsync(
                new AuctionCancelledEvent(auction.Id, auction.Title),
                cancellationToken);
        }

        _logger.LogInformation(
            "Ürün silindi, {Count} planlanmış açık artırma iptal edildi: {ProductId}",
            scheduled.Count,
            productId);

        return scheduled.Count;
    }

    /// <summary>Transaction içinde toplanıp dışarıda yayınlanacak bilgiler.</summary>
    private sealed record BidOutcome(
        BidResponse Bid,
        AuctionSummaryResponse Auction,
        string Title,
        decimal MinimumNextBid,
        DateTimeOffset EndsAt,
        int BidCount,
        string? PreviousBidderId,
        decimal PreviousAmount);
}
