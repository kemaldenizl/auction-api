# AuctionApp — SignalR ile Canlı Açık Artırma

İki .NET 9 mikroservisi (**Catalog** — ürün CRUD, **Auction** — canlı teklif),
önlerinde bir **YARP gateway**, aralarında **RabbitMQ**. Projenin amacı SignalR
ile canlı veri akışını çalışmak; açık artırma bunun için seçilmiş gerçekçi bir
senaryo.

```
                 React istemcisi
                        │
                        ▼
              ┌───────────────────┐
              │  API Gateway :5080│  (YARP — tek giriş noktası, CORS, WebSocket)
              └─────────┬─────────┘
            ┌───────────┴───────────┐
            ▼                       ▼
    ┌───────────────┐       ┌───────────────┐
    │ Catalog :5005 │◄──────│ Auction :5112 │  senkron HTTP (ürün doğrulama)
    └───────┬───────┘       └───────┬───────┘
            │                       │  SignalR hub: /hubs/auction
            │                       │
            │    ┌─────────────┐    │
            └───►│  RabbitMQ   │◄───┘  asenkron olaylar (outbox ile)
                 └─────────────┘
                        
    postgres (catalog_db + auction_db)   ·   redis (SignalR backplane + presence)
```

## İçindekiler

- [Gereksinimler](#gereksinimler)
- [Çalıştırma](#çalıştırma)
- [Adresler](#adresler)
- [API Gateway](#api-gateway)
- [Servisler arası iletişim](#servisler-arası-iletişim)
- [Hazır demo verisi](#hazır-demo-verisi)
- [İlk 60 saniye: canlı akışı görmek](#ilk-60-saniye-canlı-akışı-görmek)
- [REST uçları](#rest-uçları)
- [SignalR sözleşmesi](#signalr-sözleşmesi)
- [Testler](#testler)
- [Ölçekleme testi](#ölçekleme-testi)
- [Günlük komutlar](#günlük-komutlar)
- [Docker olmadan çalıştırma](#docker-olmadan-çalıştırma)
- [Migration'lar](#migrationlar)
- [Proje yapısı](#proje-yapısı)
- [Tasarım kararları](#tasarım-kararları)
- [Sırada ne var](#sırada-ne-var)

---

## Gereksinimler

Docker ile çalıştıracaksan **sadece Docker Desktop** yeterli — .NET SDK gerekmez.

| Araç | Sürüm | Ne zaman gerekir |
|---|---|---|
| Docker Desktop | 28+ | Her zaman |
| .NET SDK | 9.0.x | Docker'sız geliştirme, migration üretme, testleri çalıştırma |
| Node 18+ | — | Sadece README'deki WebSocket dinleme örneği için |

> .NET 10 SDK kuruluysa da sorun yok: projeler `net9.0` hedefler ve paket
> sürümleri [Directory.Packages.props](Directory.Packages.props) içinde sabitlenmiştir.

## Çalıştırma

Depo kökünde:

```bash
docker compose up -d --build
```

Tek komut yeterli. Arka planda şunlar olur:

1. `postgres` ayağa kalkar, `catalog_db` ve `auction_db` yaratılır
   ([docker/postgres/init-multiple-dbs.sh](docker/postgres/init-multiple-dbs.sh))
2. `redis` (SignalR backplane + izleyici sayacı) ve `rabbitmq` (servisler arası
   olaylar) **healthy** olana kadar beklenir
3. Servisler EF migration'larını uygular (outbox tabloları dahil)
4. Demo verisi yüklenir (5 ürün, 2 açık artırma)
5. Gateway açılır ve istekleri servislere dağıtır

Hazır olduğunu doğrula:

```bash
docker compose ps && curl -s localhost:5080/health/catalog && curl -s localhost:5080/health/auction
```

İkisi de `Healthy` yazdırmalı.

## Adresler

| Ne | Adres |
|---|---|
| **Gateway (uygulamanın girişi)** | **http://localhost:5080** |
| Catalog Swagger | http://localhost:5005/swagger |
| Auction Swagger | http://localhost:5112/swagger |
| SignalR hub (gateway üzerinden) | `ws://localhost:5080/hubs/auction` |
| RabbitMQ yönetim arayüzü | http://localhost:15672 · `guest` / `guest` |
| Postgres | `localhost:5432` · `postgres` / `devpass` |
| Redis | `localhost:6379` |

> **Gateway neden 5080?** macOS'ta 5000 portunu AirPlay Receiver
> (`ControlCenter`) tutuyor. Kapatmak istemiyorsan 5080 kalsın; Linux/Windows'ta
> [docker-compose.yml](docker-compose.yml) içinde 5000'e çevirebilirsin.

Servis portları (5005/5112) **Swagger ve hata ayıklama için** açık bırakıldı.
Uygulamanın kendisi tek kapıdan — gateway'den — konuşur.

## API Gateway

[src/Gateway/ApiGateway](src/Gateway/ApiGateway) · YARP · rotalar
[appsettings.json](src/Gateway/ApiGateway/appsettings.json) içinde bildirimsel.

| Yol | Hedef |
|---|---|
| `/api/products/**` | Catalog API |
| `/api/auctions/**` | Auction API |
| `/hubs/**` | Auction API (WebSocket) |
| `/health/catalog`, `/health/auction` | ilgili servisin `/health`'i |
| `/health` | gateway'in kendisi |
| `/` | hangi yolun nereye gittiğini listeleyen JSON |

Önemli noktalar:

**Yol ön eki yok.** `/api/products` ve `/api/auctions` çakışmadığı için
yeniden yazma yapmıyoruz. Frontend için tek bir base URL yeter: `http://localhost:5080`.

**WebSocket için ek ayar gerekmiyor.** YARP `Connection: Upgrade` isteklerini
olduğu gibi aktarır; SignalR gateway üzerinden sorunsuz çalışır (doğrulandı).

**CORS artık gateway'in işi.** Tarayıcı yalnızca gateway'e konuşuyor. Servislerdeki
CORS ayarı doğrudan port erişimi/Swagger için duruyor.

**Aktif health check.** Gateway her 10 saniyede hedeflerin `/health`'ini yoklar;
üst üste 3 hata veren hedefe istek göndermez.

**Swagger bilinçli olarak proxy'lenmiyor.** Swagger UI varlıkları mutlak yol
kullandığı için yol yeniden yazıldığında bozulur; servis portlarından açılıyor.

## Servisler arası iletişim

Bu projenin iletişim mimarisi hakkındaki asıl soru: **ne zaman senkron HTTP, ne
zaman mesaj?** İkisi birlikte kullanılıyor ve seçim bilinçli.

### Senkron HTTP — "şimdi cevap lazım"

Tek yer: **açık artırma açılırken ürünü doğrulamak.**

```
Auction API ──GET /api/products/{id}──► Catalog API
```

[CatalogClient.cs](src/Services/Auction/Auction.Business/Catalog/CatalogClient.cs) ·
typed `HttpClient`, 10 sn timeout, hata → `502 external_service_error`.

Burada mesajlaşma **yanlış** olurdu: kullanıcı `POST /api/auctions` isteğinin
cevabını bekliyor ve ürün gerçekten var mı sorusunun cevabı *şimdi* gerekli.
Olayla yapsaydık ya doğrulamasız kabul edip sonra iptal etmek gerekirdi ya da
kullanıcıyı belirsiz bir "işleniyor" durumunda bırakırdık. Komut yolunda başka
servise soru sormak senkron HTTP'nin doğru yeri.

### Asenkron olaylar — "sonra olsun, kim dinlerse dinlesin"

Üç olay, iki yönde. Sözleşmeler:
[BuildingBlocks.Contracts](src/BuildingBlocks/BuildingBlocks.Contracts)

| Olay | Yayınlayan | Tüketen | Sonuç |
|---|---|---|---|
| `AuctionEnded` | Auction | Catalog | Satılan ürün pasife çekilir |
| `ProductUpdated` | Catalog | Auction | **Başlamamış** açık artırmaların ürün kopyası tazelenir |
| `ProductDeleted` | Catalog | Auction | **Başlamamış** açık artırmalar iptal edilir (+ odaya WebSocket bildirimi) |

Bunlar neden olay: yayınlayan servis sonucu beklemiyor, kimin dinlediğini
bilmiyor ve bilmemeli. Açık artırma bittiğinde Catalog'un ürünü pasife çekmesi
Auction'ın işi değil; yarın e-posta/fatura/ödeme tüketicileri eklenirse Auction
kodunda tek satır değişmez.

`ProductDeleted` zinciri bütün resmi gösteriyor:

```
Catalog'da DELETE → outbox → RabbitMQ → Auction tüketicisi → Postgres → WebSocket
```

Yani tarayıcıda açık duran bir açık artırma odası, **başka bir serviste** yapılan
silme işlemi yüzünden "iptal edildi" bildirimini anında alır.

### Snapshot kuralı

`AuctionItem` ürün bilgisinin kopyasını tutar (`Title`, `Description`, `ImageUrl`)
ve servisler arası foreign key **yoktur** — `ProductId` sadece soft reference.

Ürün güncellenince bu kopya ne olur? Duruma göre:

- **Scheduled** (henüz başlamamış, kimse teklif vermemiş) → tazelenir
- **Live / Ended** → **dokunulmaz**

Çünkü insanlar neye teklif verdiklerini bilmek zorunda. Satışa çıkmış bir ilanın
başlığı veya fotoğrafı altından değişmemeli; bitmiş bir ilanın kaydı ise tarihsel
veri.

### Transactional outbox

"Veritabanına yaz + mesaj yayınla" iki ayrı sistemdir; arada çökersen biri olur
diğeri olmaz. Outbox bunu çözer: mesaj aynı transaction içinde `OutboxMessage`
tablosuna yazılır, ayrı bir servis onu broker'a taşır.

[EventBusRegistration.cs](src/BuildingBlocks/BuildingBlocks.Messaging/EventBusRegistration.cs) ·
MassTransit'in EF Core outbox'ı

Sonuçları:

- Veritabanı değişikliği ile mesaj **ya ikisi birlikte olur ya hiç olmaz**
- **RabbitMQ kapalıysa mesajlar tabloda birikir**, broker dönünce gönderilir
- Tüketici tarafında inbox tablosu **idempotency** sağlar (broker "en az bir
  kez" teslim eder, aynı mesaj iki kez gelebilir)

Bu yüzden `IEventPublisher.PublishAsync` çağrısı **`SaveChangesAsync`'ten önce**
yapılmak zorunda — yorumlarda da belirtildi.

### Yayınlanmayan olaylar

`AuctionStarted` ve `BidPlaced` RabbitMQ'ya **gitmiyor**. Bunların servis dışında
tüketicisi yok; canlı bildirim SignalR'ın işi. Tüketicisi olmayan olay yayınlamak
gereksiz kuyruk, gereksiz şema ve yanlış bir "entegrasyon noktası" demek.

### Kullanılmayan: SignalR üzerinden servisler arası iletişim

Auction'ın SignalR hub'ı yalnızca **sunucu → tarayıcı** yönünde kullanılıyor.
Servisler arası iletişim için WebSocket kullanmak cazip görünür ama kalıcılık,
yeniden deneme ve tüketici bağımsızlığı yoktur; o iş broker'ın.

## Hazır demo verisi

Id'ler **sabit**, böylece `curl` komutlarını kopyalayıp doğrudan kullanabilirsin.

**Ürünler** (Catalog):

| Id | Ürün | Başlangıç |
|---|---|---|
| `11111111-…1111` | Omega Seamaster 300 (1964) | 42.000 |
| `22222222-…2222` | Fender Stratocaster (1963) | 85.000 |
| `33333333-…3333` | Michael Jordan Rookie Card | 120.000 |
| `44444444-…4444` | Art Deco Pırlanta Yüzük | 31.500 |
| `55555555-…5555` | Dune — İlk Basım (1965) | 18.000 |

**Açık artırmalar** (Auction):

| Id | Durum | Not |
|---|---|---|
| `aaaaaaaa-0000-0000-0000-000000000001` | **Live** | Hemen teklif verebilirsin |
| `aaaaaaaa-0000-0000-0000-000000000002` | Scheduled | 2 dakika sonra kendi kendine canlıya geçer |

İkincisi, arka plan servisinin `Scheduled → Live` geçişini ve `AuctionStarted`
olayını canlı izlemen için böyle bırakıldı. Beklemek istemezsen:
`POST /api/auctions/{id}/start`.

## İlk 60 saniye: canlı akışı görmek

Projenin bütün fikri şu: **teklif hangi yoldan gelirse gelsin, odadaki herkes
anında görür.** İki pencereyle kanıtla.

**1.** Tarayıcıda Auction Swagger'ı aç: http://localhost:5112/swagger

**2.** Terminalde bir WebSocket dinleyicisi aç — **gateway üzerinden**:

```bash
cd /tmp && npm i @microsoft/signalr >/dev/null 2>&1 && node --input-type=module -e "
import * as signalR from '@microsoft/signalr';
const c = new signalR.HubConnectionBuilder()
  .withUrl('http://localhost:5080/hubs/auction?bidderId=izleyici&bidderName=Izleyici')
  .build();
for (const e of ['BidPlaced','Outbid','AuctionStarted','AuctionEnded','AuctionCancelled','ViewerCountChanged','BidRejected'])
  c.on(e, p => console.log(e, JSON.stringify(p)));
await c.start();
const d = await c.invoke('JoinAuction','aaaaaaaa-0000-0000-0000-000000000001');
console.log('katıldım:', d.title, '| fiyat:', d.currentPrice, '| min sonraki:', d.minimumNextBid);
await new Promise(() => {});
"
```

**3.** Swagger'dan `POST /api/auctions/{id}/bids` çağır:

```json
{ "amount": 43000, "bidderId": "u1", "bidderName": "Kemal" }
```

Terminalde `BidPlaced` olayının anında düştüğünü göreceksin. REST isteği attın,
WebSocket'ten olay aldın — aradaki bağ `IAuctionNotifier`.

Komut satırından teklif vermek istersen (gateway üzerinden):

```bash
curl -s -X POST localhost:5080/api/auctions/aaaaaaaa-0000-0000-0000-000000000001/bids -H "Content-Type: application/json" -d '{"amount":44000,"bidderId":"u2","bidderName":"Ayse"}'
```

Mesajlaşma zincirini görmek için planlanmış açık artırmanın ürününü sil —
oda anında "iptal edildi" bildirimi alır:

```bash
curl -s -X DELETE localhost:5080/api/products/22222222-2222-2222-2222-222222222222
```

## REST uçları

Hepsi gateway üzerinden de aynı yolla çalışır: `http://localhost:5080/api/...`

### Catalog API — `:5005`

| Metot | Yol | Açıklama |
|---|---|---|
| GET | `/api/products` | Sayfalı liste. `page`, `pageSize`, `search`, `category`, `sortBy` (`name`/`price`/`createdAt`), `descending`, `isActive` |
| GET | `/api/products/categories` | Kullanımdaki kategoriler |
| GET | `/api/products/{id}` | Tek ürün |
| POST | `/api/products` | Oluştur |
| PUT | `/api/products/{id}` | Güncelle → `ProductUpdated` yayınlar |
| DELETE | `/api/products/{id}` | Sil → `ProductDeleted` yayınlar |

### Auction API — `:5112`

| Metot | Yol | Açıklama |
|---|---|---|
| GET | `/api/auctions` | Sayfalı liste. `status` (`Scheduled`/`Live`/`Ended`/`Cancelled`), `search`, `productId`. Canlı olanlar üstte |
| GET | `/api/auctions/{id}` | Detay + son 20 teklif |
| GET | `/api/auctions/{id}/bids?take=50` | Teklif geçmişi |
| POST | `/api/auctions` | Açık artırma aç (ürün Catalog'dan **senkron** doğrulanır) |
| POST | `/api/auctions/{id}/bids` | **Teklif ver** (REST yolu) |
| POST | `/api/auctions/{id}/start` | Hemen canlıya al (demo kolaylığı) |
| POST | `/api/auctions/{id}/cancel` | İptal et |

Hatalar RFC 7807 `ProblemDetails` döner ve makine-okunur bir `errorCode` taşır:

| `errorCode` | HTTP | Anlamı |
|---|---|---|
| `bid_too_low` | 409 | Teklif düşük. Cevapta `minimumRequired` da gelir |
| `already_highest_bidder` | 409 | En yüksek teklif zaten sende |
| `auction_not_started` | 409 | Henüz başlamadı |
| `auction_ended` | 409 | Süre doldu |
| `auction_cancelled` | 409 | İptal edilmiş |
| `duplicate_product_name` | 409 | Aynı isimde ürün var |
| `not_found` | 404 | Kayıt yok |
| `external_service_error` | 502 | Catalog servisine erişilemedi |

## SignalR sözleşmesi

**Yol:** `/hubs/auction` · **Kimlik:** `?bidderId=<id>&bidderName=<ad>`

İstemciden sunucuya (`invoke`):

| Metot | Döner | Açıklama |
|---|---|---|
| `JoinAuction(auctionId)` | `AuctionDetailResponse` | Odaya katar **ve** güncel durumu döner |
| `LeaveAuction(auctionId)` | — | Odadan çıkar |
| `PlaceBid(auctionId, amount)` | — | Sonuç olay olarak gelir (`BidPlaced` / `BidRejected`) |
| `WhoAmI()` | `{ id, name }` | Bağlantının kimliğini doğrula |

Sunucudan istemciye (`on`):

| Olay | Kime gider | İçerik |
|---|---|---|
| `BidPlaced` | Odanın tamamı | `amount`, `minimumNextBid`, `bidderName`, `bidCount`, `endsAt` |
| `Outbid` | **Sadece teklifi geçilen kullanıcı** | `yourBid`, `newPrice`, `newLeaderName` |
| `BidRejected` | **Sadece teklifi gönderen bağlantı** | `reason`, `errorCode`, `minimumRequired` |
| `AuctionStarted` | Odanın tamamı | `currentPrice`, `minimumNextBid`, `endsAt` |
| `AuctionEnded` | Odanın tamamı | `finalPrice`, `winnerId`, `winnerName`, `bidCount` |
| `AuctionCancelled` | Odanın tamamı | `title` |
| `ViewerCountChanged` | Odanın tamamı | `viewerCount` |

`JoinAuction`'ın değer döndürmesi bilinçli: "önce REST'ten çek, sonra socket'e
bağlan" sırasında araya düşen teklifleri kaçırma yarışı oluşmuyor.

> React tarafında bunların nasıl tüketileceği ayrı dosyada:
> **[frontend.md](frontend.md)**

## Testler

```bash
dotnet test
```

**109 test, 4 proje.** IntegrationTests ve FunctionalTests Docker gerektirir
(Testcontainers ile geçici Postgres başlatılır) — ama compose stack'inin ayakta
olması gerekmez, testler kendi veritabanını kurar.

| Proje | Sayı | Ne test eder | Altyapı |
|---|---|---|---|
| [UnitTests](tests/UnitTests) | 58 | İş kuralları: teklif doğrulama, durum geçişleri, olay yayınları, snapshot kuralı | Yok (hepsi fake) |
| [ArchitectureTests](tests/ArchitectureTests) | 14 | Katman bağımlılıkları ve kodlama kuralları | Yok (reflection) |
| [IntegrationTests](tests/IntegrationTests) | 8 | Eşzamanlılık (`FOR UPDATE`), mesaj tüketicileri | Postgres container |
| [FunctionalTests](tests/FunctionalTests) | 29 | HTTP yüzeyi + SignalR hub'ı uçtan uca | Postgres container |

Tek proje çalıştırmak için:

```bash
dotnet test tests/UnitTests/UnitTests.csproj
```

### Neyi neden orada test ettim

**58 birim testinin hiçbiri altyapı ayağa kaldırmıyor** — bu tasarımın bir
sonucu, tesadüf değil. Business katmanı SignalR'ı ve MassTransit'i tanımadığı
için teklif kurallarını `IAuctionNotifier` ve `IEventPublisher` fake'leriyle test
edebiliyoruz. ArchitectureTests bu özelliğin bozulmasını engelliyor: birisi
Business'a `using Microsoft.AspNetCore.SignalR;` eklerse test kırılır.

**Eşzamanlılık testi birim testle yapılamaz.** 12 kişi aynı anda aynı tutarı
teklif ettiğinde tam 1'inin kabul edilmesi, gerçek Postgres satır kilidinin
davranışı. EF'in InMemory sağlayıcısı `FOR UPDATE`'i taklit etmez — o sağlayıcıyla
yazılan yeşil test yanlış güven verir. Bu yüzden Testcontainers.

**Tüketiciler MassTransit test harness'i ile** test ediliyor: in-memory transport,
yani RabbitMQ gerekmiyor ama MassTransit'in kendi boru hattı (tüketici keşfi, DI
scope'u, serileştirme) gerçekten çalışıyor. "Consumer kaydı yapılmamış" gibi
hatalar yakalanır.

**Hub testleri long polling kullanıyor.** TestServer WebSocket'i destekliyor ama
bağlamak ek kurulum gerektiriyor; test edilen şey taşıma katmanı değil, hub
mantığı. Gerçek WebSocket yolu çalışan sistem üzerinde ayrıca doğrulandı.

**Outbox'ın kalıcılığı testlerde değil, çalışan sistemde doğrulandı** (bitmiş
açık artırma → RabbitMQ → Catalog ürünü pasife çekti). Outbox'ın tablo
davranışını test etmek, mesaj teslim edildikten sonra satırın silinmesi
yüzünden zamanlamaya bağlı/kırılgan olurdu.

## Ölçekleme testi

Amaç: iki Auction instance'ı çalıştırıp bir sunucudaki teklifin diğer sunucudaki
istemciye ulaştığını görmek (Redis backplane).

```bash
docker compose -f docker-compose.yml up -d --scale auction.api=2
```

`-f docker-compose.yml` önemli: [docker-compose.override.yml](docker-compose.override.yml)
devre dışı kalır. Override dosyası yalnızca geliştirme kolaylığı için Auction
API'ye sabit isim ve 5112 portu veriyor; sabit isim/port replica ile çalışmaz.
Bu modda Auction'a yalnızca gateway üzerinden erişilir.

Normale dönmek için:

```bash
docker compose up -d
```

### Doğrulanan davranış

İki replica ile yapılan testte: 10/10 bağlantı kuruldu, bir replica'daki teklif
diğerindeki istemciye ulaştı, hedefli `Outbid` doğru kullanıcıya gitti, her iki
replica da trafik aldı (17 ve 22 HTTP isteği), sıfır hata.

Kodda hiçbir değişiklik gerekmiyor: `Program.cs`, `ConnectionStrings:Redis` dolu
olduğu için `AddStackExchangeRedis` ile backplane'i ve izleyici sayacı için
`RedisPresenceTracker`'ı devreye alır.

### Sticky session hakkında dürüst not

Gateway'de cookie tabanlı session affinity yapılandırıldı, ama bu kurulumda
**garanti değil**: YARP tek mantıksal hedef (`http://auction.api:8080`) görüyor,
replica'lara dağıtımı Docker DNS yapıyor. Test 10/10 geçti çünkü HTTP bağlantı
havuzu pratikte aynı replica'ya gidiyor — bu bir yan etki, garanti değil.

SignalR'ın `negotiate` adımı ile WebSocket upgrade'i farklı replica'ya giderse
bağlantı kurulamaz. İki sağlam çözüm var:

1. **Her replica'yı YARP'a ayrı destination olarak tanıtmak** (sabit sayıda
   instance varsa; service discovery ile de yapılabilir)
2. **İstemcide `negotiate` adımını atlamak** — tercih edilen yol:
   ```js
   .withUrl(url, { skipNegotiation: true, transport: HttpTransportType.WebSockets })
   ```
   `negotiate` hiç olmadığı için hangi replica'ya gidildiği önemsizleşir.
   Bu da 10/10 doğrulandı. Detay: [frontend.md](frontend.md#tuzaklar-okumadan-geçme)

## Günlük komutlar

Kod değiştirdikten sonra tek servisi yeniden derle:

```bash
docker compose up -d --build auction.api
```

Logları takip et:

```bash
docker compose logs -f auction.api
```

Testleri çalıştır:

```bash
dotnet test
```

Durdur (veri kalır):

```bash
docker compose down
```

Her şeyi sıfırla — veritabanı silinir, seed yeniden çalışır:

```bash
docker compose down -v
```

Veritabanına bağlan:

```bash
docker compose exec postgres psql -U postgres -d auction_db -c 'SELECT "Title","CurrentPrice","BidCount","Status" FROM "AuctionItems";'
```

Outbox'ta bekleyen mesaj var mı (RabbitMQ kapalıysa burada birikir):

```bash
docker compose exec postgres psql -U postgres -d auction_db -c 'SELECT COUNT(*) FROM "OutboxMessage";'
```

Kuyrukları ve mesaj akışını izle: http://localhost:15672 (`guest`/`guest`).
Tüketici hatalarında mesajlar `*_error` kuyruğuna düşer — orada incelenebilir.

## Docker olmadan çalıştırma

Altyapıyı Docker'da, servisleri IDE'de çalıştırmak (debug için en rahatı):

```bash
docker compose up -d postgres redis rabbitmq
```

`appsettings.json` dosyaları bu senaryo için `localhost` adreslerine ayarlıdır.
Sonra üç ayrı terminalde:

```bash
dotnet run --project src/Services/Catalog/Catalog.API
```

```bash
dotnet run --project src/Services/Auction/Auction.API
```

```bash
dotnet run --project src/Gateway/ApiGateway
```

Portlar aynı kalır (5005 / 5112 / 5080).

> **Broker olmadan da çalışır:** `RabbitMq:Host` boş bırakılırsa MassTransit
> in-memory transport'a düşer. Uygulama açılır ama olaylar süreç dışına çıkmaz,
> yani servisler arası iletişim çalışmaz. Testler bu özelliği kullanıyor.

## Migration'lar

EF araçları projeye **yerel araç** olarak kurulu
([.config/dotnet-tools.json](.config/dotnet-tools.json)) — makinede global
`dotnet-ef` sürümü ne olursa olsun doğru sürüm kullanılır:

```bash
dotnet tool restore
```

Yeni migration (Catalog örneği):

```bash
dotnet ef migrations add AddSomething --project src/Services/Catalog/Catalog.Repository --startup-project src/Services/Catalog/Catalog.API --output-dir Migrations
```

Auction için:

```bash
dotnet ef migrations add AddSomething --project src/Services/Auction/Auction.Repository --startup-project src/Services/Auction/Auction.API --output-dir Migrations
```

Migration'lar uygulama açılışında otomatik uygulanır
(`DatabaseInitializer.MigrateAndSeedAsync`), elle `database update` gerekmez.

> Her iki servisin de `AddTransactionalOutbox` migration'ı var: MassTransit'in
> `InboxState`, `OutboxMessage`, `OutboxState` tablolarını oluşturuyor.

## Proje yapısı

```
src/
├── BuildingBlocks/
│   ├── BuildingBlocks.Contracts    mesaj sözleşmeleri + IEventPublisher (BAĞIMLILIKSIZ)
│   └── BuildingBlocks.Messaging    MassTransit + RabbitMQ + outbox kurulumu
├── Gateway/ApiGateway              YARP: tek giriş noktası
└── Services/<Servis>/
    ├── <Servis>.Core               soyutlamalar: IRepository, IUnitOfWork, PagedResult, exception'lar
    ├── <Servis>.Entity             domain entity'leri
    ├── <Servis>.Repository         EF Core DbContext, konfigürasyonlar, repository'ler, seed
    ├── <Servis>.Business           iş kuralları, DTO'lar, mapping (+ Auction: Catalog istemcisi)
    └── <Servis>.API                controller'lar, SignalR hub'ı, mesaj tüketicileri, DI, Swagger

tests/
├── TestCommon                      fake'ler, entity builder, Postgres container fixture
├── UnitTests                       iş kuralları
├── ArchitectureTests               katman kuralları
├── IntegrationTests                eşzamanlılık + tüketiciler (gerçek Postgres)
└── FunctionalTests                 HTTP + SignalR uçtan uca
```

Bağımlılık yönü tek yönlü: `API → Business → Repository → Entity → Core`.
Business yalnızca `Contracts`'ı görür; `Messaging`'e sadece API katmanı bağlıdır.

Dikkat çeken yerler:

| Dosya | Neden önemli |
|---|---|
| [AuctionService.cs](src/Services/Auction/Auction.Business/Auctions/AuctionService.cs) | Teklif akışı: kilit, kurallar, commit sonrası yayın |
| [AuctionHub.cs](src/Services/Auction/Auction.API/Hubs/AuctionHub.cs) | Hub; iş kuralı içermez, servise devreder |
| [SignalRAuctionNotifier.cs](src/Services/Auction/Auction.API/Infrastructure/SignalRAuctionNotifier.cs) | "Kim ne alır" kararının tek yeri |
| [AuctionEvents.cs](src/Services/Auction/Auction.Business/Auctions/Events/AuctionEvents.cs) | Frontend'in dinlediği sözleşme |
| [AuctionItemRepository.cs](src/Services/Auction/Auction.Repository/Repositories/AuctionItemRepository.cs) | `FOR UPDATE` satır kilidi |
| [EventBusRegistration.cs](src/BuildingBlocks/BuildingBlocks.Messaging/EventBusRegistration.cs) | Outbox/inbox kurulumu ve gerekçesi |
| [BidConcurrencyTests.cs](tests/IntegrationTests/BidConcurrencyTests.cs) | Lost update'in olmadığının kanıtı |

## Tasarım kararları

**Teklif iki yoldan verilebilir, kurallar tek yerde.** Hem REST ucu hem hub'ın
`PlaceBid` metodu aynı `IAuctionService`'i çağırır. İki yol asla ayrışmaz ve
ikisi de aynı canlı olayları yayınlar. Fonksiyonel testlerde doğrulandı.

**Business katmanı SignalR'ı ve MassTransit'i tanımaz.** Canlı bildirim
`IAuctionNotifier`, olay yayını `IEventPublisher` arkasında; implementasyonlar
API katmanında. Kazancı somut: 58 birim testinin hiçbiri altyapı ayağa
kaldırmıyor. ArchitectureTests bu sınırı koruyor.

**Mesaj tüketicileri API katmanında.** Controller ve hub gibi onlar da birer
adaptör: iş kuralı taşımazlar, servis metodunu çağırırlar. Business'a konsalardı
Business MassTransit'e bağlanırdı.

**Tüketiciler istisna fırlatmaz.** `MarkAsSoldAsync` ürün bulunamazsa ya da
zaten pasifse sessizce geçer. Fırlatsa mesaj sürekli yeniden denenir ve error
kuyruğuna düşerdi. Gerçek hatalarda (veritabanı erişilemez) istisna çıkar ve
retry politikası devreye girer — bu doğru davranış.

**Eşzamanlı teklifler satır kilidiyle sıralanır.** İki kişi aynı anda teklif
verdiğinde ikisi de aynı `CurrentPrice`'ı okuyup ikisi de geçerli sanabilir
(*lost update*). `SELECT … FOR UPDATE` ikinciyi birincinin commit'ine kadar
bekletir. Aynı mekanizma `Scheduled → Live → Ended` geçişlerinde de kullanılıyor:
çok instance'lı kurulumda geçişi ve olayı yalnızca biri yapar.

**Bildirimler commit'ten sonra, mesajlar commit ile birlikte.** SignalR yayını
transaction'ın dışında (önce yayınlasak, rollback olduğunda istemciler var
olmayan bir teklifi görürdü). RabbitMQ mesajı ise outbox sayesinde
transaction'ın *içinde* — ya ikisi birlikte ya hiç.

**Servisler arası foreign key yok, snapshot var.** Detay:
[Servisler arası iletişim](#servisler-arası-iletişim).

**Her servis kendi veritabanına sahip.** Aynı Postgres instance'ı içinde ama ayrı
database (`catalog_db`, `auction_db`) — aralarında join yapılamaz, ki amaç bu.

**`src/BuildingBlocks` artık dolu, ama sadece sözleşmelerle.**
Önceki turda "her servis tek başına derlenebilsin" diye boş bırakmıştım.
Mesajlaşma eklenince durum değişti: **mesaj sözleşmeleri paylaşılmak zorunda** —
iki servis aynı tipi aynı tam adla görmeli, çünkü MassTransit exchange adlarını
tip adından üretir. Bu yüzden `BuildingBlocks.Contracts` **bağımlılıksız**
tutuldu (ArchitectureTests bunu doğruluyor) ve `PagedResult`/`IRepository` gibi
katman içi soyutlamalar hâlâ her serviste ayrı — onları paylaşmanın bir faydası
yok, maliyeti ise servislerin birbirine kenetlenmesi.

**MassTransit 8.x'te kilitli.** 8.x Apache-2.0, **9.x ticari lisanslı**
(`massient.com/license`). Yükseltmeden önce lisansı kontrol et; sürüm
[Directory.Packages.props](Directory.Packages.props) içinde gerekçesiyle sabit.

**Container'da HTTPS yok.** Servisler düz HTTP (`:8080`) dinler, TLS'i gateway
üstlenir. Bu yüzden `UseHttpsRedirection` yok — WebSocket upgrade'i düz HTTP
üzerinden sorunsuz çalışır, `wss://` gateway'de sonlanır.

**Kimlik doğrulama yok (şimdilik).** Teklif sahibi istemciden geliyor: REST'te
gövdeden, SignalR'da query string'den. Bilinçli bir demo kararı — ayrı bir
Security servisi SignalR öğrenme hedefini yavaşlatırdı. JWT eklenince değişecek
tek yer `AuctionHub.ResolveBidder` ve `PlaceBidRequest`.

## Sırada ne var

1. **React istemcisi** — [frontend.md](frontend.md) hazır rehber. Base URL
   artık gateway: `http://localhost:5080`
2. **JWT** — `Auction.API` içinde, ayrı servis olmadan. SignalR'ın öğretici
   kısımları burada: WebSocket handshake'inde header gönderilemediği için token
   `access_token` query parametresiyle taşınır, `IUserIdProvider` ile
   `Clients.User(id)` kullanılır (şimdiki kullanıcı-başına-grup çözümünün yerine).
   Gateway'de token doğrulama da eklenebilir
3. **Gateway'de rate limiting** — YARP ASP.NET Core rate limiter'ı ile
   entegre; teklif uçları için doğal bir yer
4. **Saga / süreç yöneticisi** — MassTransit'in sonraki öğrenme adımı:
   "açık artırma bitti → ödeme al → kargo oluştur" gibi çok adımlı, telafi
   gerektiren akışlar
5. **Gözlemlenebilirlik** — OpenTelemetry. Bir teklifin gateway → Auction →
   RabbitMQ → Catalog yolculuğunu tek trace'te görmek, dağıtık sistemde
   hata ayıklamanın temeli
