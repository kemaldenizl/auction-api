namespace Auction.Core.Exceptions;

/// <summary>
/// Beklenen (iş kuralı kaynaklı) hataların ortak atası.
/// REST tarafında middleware bunları ProblemDetails'a,
/// SignalR tarafında hub bunları <c>BidRejected</c> olayına çevirir.
/// </summary>
public abstract class AppException : Exception
{
    protected AppException(string message, string errorCode) : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>İstemcinin dallanma yapabileceği makine-okunur kod.</summary>
    public string ErrorCode { get; }

    public abstract int StatusCode { get; }
}

/// <summary>Kayıt bulunamadı -> 404.</summary>
public sealed class NotFoundException : AppException
{
    public NotFoundException(string resource, Guid id)
        : base($"{resource} bulunamadı: {id}", "not_found")
    {
    }

    public NotFoundException(string message)
        : base(message, "not_found")
    {
    }

    public override int StatusCode => 404;
}

/// <summary>İş kuralı ihlali -> 409. Teklif reddetmelerinin çoğu bu.</summary>
public class BusinessRuleException : AppException
{
    public BusinessRuleException(string message, string errorCode = "business_rule")
        : base(message, errorCode)
    {
    }

    public override int StatusCode => 409;
}

/// <summary>
/// Teklif çok düşük. Ayrı tip olmasının sebebi: istemciye "en az ne kadar
/// vermelisin" bilgisini de taşımak istiyoruz (frontend input'u bununla düzeltiyor).
/// </summary>
public sealed class BidTooLowException : BusinessRuleException
{
    public BidTooLowException(decimal minimumRequired)
        : base($"Teklif çok düşük. En az {minimumRequired:N2} vermelisiniz.", "bid_too_low")
    {
        MinimumRequired = minimumRequired;
    }

    public decimal MinimumRequired { get; }
}

/// <summary>Bağımlı servis (Catalog) ulaşılamadı veya hata döndü -> 502.</summary>
public sealed class ExternalServiceException : AppException
{
    public ExternalServiceException(string serviceName, string message)
        : base($"{serviceName} servisine erişilemedi: {message}", "external_service_error")
    {
    }

    public override int StatusCode => 502;
}
