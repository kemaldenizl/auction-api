namespace BuildingBlocks.Contracts.Catalog;

// DİKKAT: MassTransit exchange adlarını mesaj tipinin tam adından (namespace + tip)
// üretir. Bu tipleri taşımak/yeniden adlandırmak çalışan sistemde kuyruk
// topolojisini değiştirir — kırıcı bir değişikliktir.
//
// Olaylar geçmiş zamanlıdır ("oldu"), emir değildir ("yap"): yayınlayan servis
// kimin dinlediğini bilmez ve bilmemelidir.

/// <summary>
/// Katalogdaki bir ürün güncellendi.
/// Auction servisi bunu dinleyip <b>henüz başlamamış</b> açık artırmaların
/// ürün kopyasını (snapshot) tazeler.
/// </summary>
public sealed record ProductUpdated(
    Guid ProductId,
    string Name,
    string? Description,
    string? ImageUrl,
    decimal StartingPrice,
    bool IsActive,
    DateTimeOffset OccurredAt);

/// <summary>
/// Katalogdan bir ürün silindi.
/// Auction servisi bunu dinleyip o ürüne ait <b>henüz başlamamış</b>
/// açık artırmaları iptal eder. Canlı/bitmiş olanlara dokunulmaz.
/// </summary>
public sealed record ProductDeleted(
    Guid ProductId,
    DateTimeOffset OccurredAt);
