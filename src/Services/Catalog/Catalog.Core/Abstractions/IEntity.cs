namespace Catalog.Core.Abstractions;

/// <summary>
/// Generic repository'nin üzerinde çalışabileceği minimum sözleşme.
/// Core katmanı entity'leri tanımaz; sadece "Id'si olan bir şey" bilir.
/// </summary>
public interface IEntity
{
    Guid Id { get; }
}
