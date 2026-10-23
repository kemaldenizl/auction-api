using Auction.Core.Abstractions;

namespace Auction.Entity.Common;

/// <summary>Tüm entity'lerin paylaştığı kimlik ve denetim alanları.</summary>
public abstract class BaseEntity : IEntity
{
    // v7 GUID: zaman sıralı, bu yüzden index'te v4 gibi parçalanma yapmaz.
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }
}
