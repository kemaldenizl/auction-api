using System.Text.Json;
using Auction.Core.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Auction.API.Middleware;

/// <summary>
/// REST tarafındaki istisnaları RFC 7807 ProblemDetails'a çevirir.
/// (SignalR tarafında aynı istisnalar hub içinde <c>BidRejected</c> olayına çevrilir.)
/// </summary>
public sealed class ExceptionHandlingMiddleware : IMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ExceptionHandlingMiddleware(
        ILogger<ExceptionHandlingMiddleware> logger,
        IHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (AppException exception)
        {
            _logger.LogWarning(exception, "İş kuralı hatası: {ErrorCode}", exception.ErrorCode);

            var minimumRequired = exception is BidTooLowException tooLow
                ? tooLow.MinimumRequired
                : (decimal?)null;

            await WriteProblemAsync(context, exception.StatusCode, exception.Message, exception.ErrorCode, minimumRequired);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            _logger.LogDebug("İstek iptal edildi: {Path}", context.Request.Path);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Beklenmeyen hata: {Path}", context.Request.Path);

            var detail = _environment.IsDevelopment()
                ? exception.ToString()
                : "Beklenmeyen bir hata oluştu.";

            await WriteProblemAsync(context, StatusCodes.Status500InternalServerError, detail, "internal_error", null);
        }
    }

    private static async Task WriteProblemAsync(
        HttpContext context,
        int statusCode,
        string detail,
        string errorCode,
        decimal? minimumRequired)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = ReasonFor(statusCode),
            Detail = detail,
            Instance = context.Request.Path
        };

        problem.Extensions["errorCode"] = errorCode;
        problem.Extensions["traceId"] = context.TraceIdentifier;

        // Teklif çok düşükse istemci input'u düzeltebilsin.
        if (minimumRequired is not null)
        {
            problem.Extensions["minimumRequired"] = minimumRequired;
        }

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOptions));
    }

    private static string ReasonFor(int statusCode) => statusCode switch
    {
        400 => "Geçersiz istek",
        404 => "Bulunamadı",
        409 => "İş kuralı ihlali",
        502 => "Bağımlı servis hatası",
        _ => "Sunucu hatası"
    };
}
