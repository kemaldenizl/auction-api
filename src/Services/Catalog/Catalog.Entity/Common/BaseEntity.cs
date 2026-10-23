using Catalog.Core.Abstractions;

namespace Catalog.Entity.Common;

/// <summary>Tüm entity'lerin paylaştığı kimlik ve denetim alanları.</summary>
public abstract class BaseEntity : IEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }
}
