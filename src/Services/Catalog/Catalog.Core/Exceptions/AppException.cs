namespace Catalog.Core.Exceptions;

/// <summary>
/// Beklenen (iş kuralı kaynaklı) hataların ortak atası.
/// API katmanındaki middleware bunları HTTP durum koduna çevirir;
/// bunun dışındaki her istisna 500 sayılır.
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

/// <summary>İş kuralı ihlali -> 409.</summary>
public sealed class BusinessRuleException : AppException
{
    public BusinessRuleException(string message, string errorCode = "business_rule")
        : base(message, errorCode)
    {
    }

    public override int StatusCode => 409;
}
