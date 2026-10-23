# Frontend Rehberi — React ile Canlı Açık Artırma

Bu dosya **React geliştiricisi için** yazıldı: backend'i değiştirmene gerek yok,
burada anlatılanları uygularsan canlı teklif akışı çalışır.

Backend'in sözleşmesi [README.md](README.md#signalr-sözleşmesi) içinde; burada o
sözleşmenin React tarafındaki karşılığı ve pratikte takılacağın yerler var.

- [Zihinsel model](#zihinsel-model)
- [Kurulum](#kurulum)
- [Tipler](#1-tipler)
- [REST istemcisi](#2-rest-istemcisi)
- [SignalR bağlantısı](#3-signalr-bağlantısı)
- [useAuctionRoom hook'u](#4-useauctionroom-hooku)
- [Bileşen](#5-bileşen)
- [Tuzaklar](#tuzaklar-okumadan-geçme)
- [Teklif: hub mı REST mi?](#teklif-hub-mı-rest-mi)
- [JWT eklendiğinde](#jwt-eklendiğinde)

---

## Zihinsel model

Üç kuralı aklında tut, gerisi detay:

**1. Durumun kaynağı sunucu, istemci sadece yansıtır.**
Fiyatı kendin hesaplamaya çalışma. `BidPlaced` olayı `amount`, `minimumNextBid`
ve `bidCount` değerlerini zaten getiriyor — onları yaz, geç.

**2. İlk durumu `JoinAuction` veriyor, REST'e gerek yok.**
Hub metodu değer döndürüyor. "Önce `GET /api/auctions/{id}` çek, sonra socket'e
bağlan" yaparsan arada düşen teklifi kaçırırsın. `JoinAuction` hem odaya katar
hem anlık durumu döner — yarış yok.

**3. Teklifin sonucu cevap değil, olaydır.**
`PlaceBid` bir şey döndürmez. Başarılıysa `BidPlaced`, reddedilirse
`BidRejected` olayı gelir. UI'yi buna göre kur. (Senkron cevap istiyorsan REST
yolu var — [aşağıda](#teklif-hub-mı-rest-mi).)

## Kurulum

```bash
npm install @microsoft/signalr
```

`.env` (Vite):

```
VITE_API_URL=http://localhost:5080
```

**Tek adres yeter.** Önde bir API gateway var; `/api/products`, `/api/auctions`
ve `/hubs/auction` yollarını ilgili servise kendisi dağıtıyor. Servislerin
kendi portlarını (5005 / 5112) frontend'de kullanma — onlar Swagger ve hata
ayıklama için açık.

> **CORS gateway'in işi.** Varsayılan olarak `http://localhost:5173` (Vite) ve
> `http://localhost:3000` (CRA/Next) origin'lerine izin verilir. Başka portta
> çalışıyorsan gateway'in ayarına ekle —
> `src/Gateway/ApiGateway/appsettings.json` içindeki `Cors:AllowedOrigins`
> dizisine, ya da compose'dan:
> ```yaml
> gateway:
>   environment:
>     Cors__AllowedOrigins__0: "http://localhost:5173"
>     Cors__AllowedOrigins__1: "http://localhost:4200"
> ```
> Ortam değişkenleri diziyi **indeks indeks** ezer: yalnızca `__1` verirsen
> `appsettings.json`'daki 0. eleman yerinde kalır, 1. eleman değişir. Karışıklık
> olmaması için izin verdiğin origin'lerin tamamını sırayla yazman en temizi.
> Değişiklikten sonra `docker compose up -d auction.api` ile servisi yeniden başlat.

## 1. Tipler

`src/types/auction.ts` — sunucudaki C# kayıtlarının birebir karşılığı.
JSON camelCase geldiği için alan adları aynen böyle.

```ts
export type AuctionStatus = 'Scheduled' | 'Live' | 'Ended' | 'Cancelled';

export interface Bid {
  id: string;
  auctionId: string;
  amount: number;
  bidderId: string;
  bidderName: string;
  createdAt: string;
}

export interface AuctionSummary {
  id: string;
  productId: string;
  title: string;
  imageUrl: string | null;
  startingPrice: number;
  currentPrice: number;
  minimumIncrement: number;
  minimumNextBid: number;
  bidCount: number;
  highestBidderName: string | null;
  status: AuctionStatus;
  startsAt: string;
  endsAt: string;
}

export interface AuctionDetail extends AuctionSummary {
  description: string | null;
  highestBidderId: string | null;
  recentBids: Bid[];
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasPrevious: boolean;
  hasNext: boolean;
}

export interface Product {
  id: string;
  name: string;
  description: string | null;
  category: string;
  startingPrice: number;
  imageUrl: string | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string | null;
}

// ---- SignalR olayları (sunucu -> istemci) ----

export interface BidPlacedEvent {
  auctionId: string;
  bidId: string;
  amount: number;
  minimumNextBid: number;
  bidderId: string;
  bidderName: string;
  bidCount: number;
  createdAt: string;
  endsAt: string;
}

export interface OutbidEvent {
  auctionId: string;
  auctionTitle: string;
  yourBid: number;
  newPrice: number;
  newLeaderName: string;
  minimumNextBid: number;
}

export interface AuctionStartedEvent {
  auctionId: string;
  title: string;
  currentPrice: number;
  minimumNextBid: number;
  startsAt: string;
  endsAt: string;
}

export interface AuctionEndedEvent {
  auctionId: string;
  title: string;
  finalPrice: number;
  winnerId: string | null;
  winnerName: string | null;
  bidCount: number;
  endedAt: string;
}

export interface AuctionCancelledEvent {
  auctionId: string;
  title: string;
}

export interface ViewerCountChangedEvent {
  auctionId: string;
  viewerCount: number;
}

export interface BidRejectedEvent {
  auctionId: string;
  reason: string;
  errorCode: BidErrorCode;
  minimumRequired: number | null;
}

export type BidErrorCode =
  | 'bid_too_low'
  | 'already_highest_bidder'
  | 'auction_not_started'
  | 'auction_ended'
  | 'auction_cancelled'
  | 'invalid_amount'
  | 'missing_bidder'
  | 'not_found';
```

## 2. REST istemcisi

Backend hataları RFC 7807 `ProblemDetails` döner ve içinde **`errorCode`** ile
(teklif düşükse) **`minimumRequired`** gelir. Bunları kaybetmeyen bir sarmalayıcı:

```ts
// src/api/client.ts
export interface ApiProblem {
  title: string;
  status: number;
  detail: string;
  errorCode: string;
  traceId: string;
  minimumRequired?: number;
}

export class ApiError extends Error {
  constructor(readonly problem: ApiProblem) {
    super(problem.detail || problem.title);
    this.name = 'ApiError';
  }

  get errorCode() {
    return this.problem.errorCode;
  }

  /** Teklif düşükse sunucunun söylediği alt sınır. */
  get minimumRequired() {
    return this.problem.minimumRequired;
  }
}

async function request<T>(baseUrl: string, path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${baseUrl}${path}`, {
    ...init,
    headers: { 'Content-Type': 'application/json', ...init?.headers },
  });

  if (!response.ok) {
    // ProblemDetails bekliyoruz; gelmezse (502, proxy hatası) yine de anlamlı mesaj ver.
    const problem = await response.json().catch(() => null);
    throw new ApiError(
      problem ?? {
        title: 'İstek başarısız',
        status: response.status,
        detail: `HTTP ${response.status}`,
        errorCode: 'unknown',
        traceId: '',
      },
    );
  }

  if (response.status === 204) return undefined as T;
  return response.json() as Promise<T>;
}

// Tek giriş noktası: gateway yolları servislere dağıtıyor.
const API = import.meta.env.VITE_API_URL;

export const catalogApi = {
  listProducts: (params: Record<string, string | number | undefined> = {}) => {
    const query = new URLSearchParams(
      Object.entries(params)
        .filter(([, v]) => v !== undefined && v !== '')
        .map(([k, v]) => [k, String(v)]),
    );
    return request<PagedResult<Product>>(API, `/api/products?${query}`);
  },
  getProduct: (id: string) => request<Product>(API, `/api/products/${id}`),
  createProduct: (body: Partial<Product>) =>
    request<Product>(API, '/api/products', { method: 'POST', body: JSON.stringify(body) }),
  updateProduct: (id: string, body: Partial<Product>) =>
    request<Product>(API, `/api/products/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  deleteProduct: (id: string) =>
    request<void>(API, `/api/products/${id}`, { method: 'DELETE' }),
  categories: () => request<string[]>(API, '/api/products/categories'),
};

export const auctionApi = {
  list: (params: { status?: AuctionStatus; page?: number; pageSize?: number } = {}) => {
    const query = new URLSearchParams(
      Object.entries(params)
        .filter(([, v]) => v !== undefined)
        .map(([k, v]) => [k, String(v)]),
    );
    return request<PagedResult<AuctionSummary>>(API, `/api/auctions?${query}`);
  },
  get: (id: string) => request<AuctionDetail>(API, `/api/auctions/${id}`),
  bids: (id: string, take = 50) => request<Bid[]>(API, `/api/auctions/${id}/bids?take=${take}`),
  create: (body: { productId: string; minimumIncrement: number; durationMinutes: number; startingPrice?: number; startsAt?: string }) =>
    request<AuctionDetail>(API, '/api/auctions', { method: 'POST', body: JSON.stringify(body) }),
  /** REST yoluyla teklif — senkron sonuç döner (bkz. "hub mı REST mi"). */
  placeBid: (id: string, body: { amount: number; bidderId: string; bidderName: string }) =>
    request<{ bid: Bid; auction: AuctionSummary }>(API, `/api/auctions/${id}/bids`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  start: (id: string) => request<AuctionDetail>(API, `/api/auctions/${id}/start`, { method: 'POST' }),
  cancel: (id: string) => request<AuctionDetail>(API, `/api/auctions/${id}/cancel`, { method: 'POST' }),
};
```

## 3. SignalR bağlantısı

```ts
// src/realtime/connection.ts
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';

export interface Bidder {
  id: string;
  name: string;
}

export function createAuctionConnection(bidder: Bidder): HubConnection {
  const base = import.meta.env.VITE_API_URL;
  const query = new URLSearchParams({ bidderId: bidder.id, bidderName: bidder.name });

  return new HubConnectionBuilder()
    .withUrl(`${base}/hubs/auction?${query}`)
    // Kopmada yeniden bağlan. Dizi = her denemenin gecikmesi (ms).
    // Son elemandan sonra vazgeçer; sürekli denemesini istersen fonksiyon ver.
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .configureLogging(import.meta.env.DEV ? LogLevel.Information : LogLevel.Warning)
    .build();
}
```

Transport'u elle seçmen gerekmez: istemci WebSocket'i dener, olmazsa SSE, en son
long polling'e düşer. Backend üçünü de destekliyor.

## 4. useAuctionRoom hook'u

Rehberin özü burada. Yorumlardaki uyarılar üretimde başını ağrıtacak şeyler.

```ts
// src/hooks/useAuctionRoom.ts
import { useCallback, useEffect, useRef, useState } from 'react';
import { HubConnectionState } from '@microsoft/signalr';
import { createAuctionConnection, type Bidder } from '../realtime/connection';
import type {
  AuctionDetail, AuctionCancelledEvent, AuctionEndedEvent, AuctionStartedEvent,
  Bid, BidPlacedEvent, BidRejectedEvent, OutbidEvent, ViewerCountChangedEvent,
} from '../types/auction';

export type ConnectionStatus = 'connecting' | 'connected' | 'reconnecting' | 'disconnected' | 'error';

const MAX_FEED = 50;

export function useAuctionRoom(auctionId: string, bidder: Bidder) {
  const [auction, setAuction] = useState<AuctionDetail | null>(null);
  const [bids, setBids] = useState<Bid[]>([]);
  const [viewerCount, setViewerCount] = useState(0);
  const [status, setStatus] = useState<ConnectionStatus>('connecting');
  const [rejection, setRejection] = useState<BidRejectedEvent | null>(null);
  const [outbid, setOutbid] = useState<OutbidEvent | null>(null);
  const [ended, setEnded] = useState<AuctionEndedEvent | null>(null);

  const connectionRef = useRef<ReturnType<typeof createAuctionConnection> | null>(null);

  // bidder bir nesne; bağımlılığa nesneyi değil ALANLARINI koyuyoruz.
  // Aksi halde çağıran her render'da {id, name} literal'i verirse
  // effect sonsuz döngüye girer ve socket sürekli kopar.
  const { id: bidderId, name: bidderName } = bidder;

  useEffect(() => {
    let disposed = false;
    const connection = createAuctionConnection({ id: bidderId, name: bidderName });
    connectionRef.current = connection;

    // --- odaya katıl: ilk durumu da bu çağrı döndürür ---
    const join = async () => {
      const detail = await connection.invoke<AuctionDetail>('JoinAuction', auctionId);
      if (disposed) return;
      setAuction(detail);
      setBids(detail.recentBids);
    };

    // --- olay dinleyicileri ---
    // Hepsinde fonksiyonel setState kullanıyoruz: bu closure'lar effect ömrü
    // boyunca yaşıyor, state'i doğrudan okusalardı ilk render'daki değeri görürlerdi.

    connection.on('BidPlaced', (e: BidPlacedEvent) => {
      setAuction((prev) =>
        prev && prev.id === e.auctionId
          ? {
              ...prev,
              currentPrice: e.amount,
              minimumNextBid: e.minimumNextBid,
              bidCount: e.bidCount,
              highestBidderId: e.bidderId,
              highestBidderName: e.bidderName,
              endsAt: e.endsAt,
            }
          : prev,
      );

      setBids((prev) => {
        // bidId ile tekilleştir: yeniden bağlanma veya iyimser ekleme
        // sonucu aynı teklif iki kez listeye girmesin.
        if (prev.some((b) => b.id === e.bidId)) return prev;
        const next: Bid = {
          id: e.bidId,
          auctionId: e.auctionId,
          amount: e.amount,
          bidderId: e.bidderId,
          bidderName: e.bidderName,
          createdAt: e.createdAt,
        };
        return [next, ...prev].slice(0, MAX_FEED);
      });

      // Yeni teklif geldiyse eski ret mesajı artık geçersiz.
      setRejection(null);
    });

    // Sadece teklifi geçilen kullanıcıya gelir — "senin teklifin geçildi" uyarısı.
    connection.on('Outbid', (e: OutbidEvent) => setOutbid(e));

    // Sadece teklifi gönderen bağlantıya gelir.
    connection.on('BidRejected', (e: BidRejectedEvent) => setRejection(e));

    connection.on('AuctionStarted', (e: AuctionStartedEvent) => {
      setAuction((prev) =>
        prev ? { ...prev, status: 'Live', currentPrice: e.currentPrice, minimumNextBid: e.minimumNextBid, startsAt: e.startsAt, endsAt: e.endsAt } : prev,
      );
    });

    connection.on('AuctionEnded', (e: AuctionEndedEvent) => {
      setEnded(e);
      setAuction((prev) => (prev ? { ...prev, status: 'Ended', currentPrice: e.finalPrice } : prev));
    });

    connection.on('AuctionCancelled', (_e: AuctionCancelledEvent) => {
      setAuction((prev) => (prev ? { ...prev, status: 'Cancelled' } : prev));
    });

    connection.on('ViewerCountChanged', (e: ViewerCountChangedEvent) => {
      if (e.auctionId === auctionId) setViewerCount(e.viewerCount);
    });

    // --- bağlantı durumu ---
    connection.onreconnecting(() => !disposed && setStatus('reconnecting'));
    connection.onclose(() => !disposed && setStatus('disconnected'));

    connection.onreconnected(() => {
      if (disposed) return;
      setStatus('connected');
      // KRİTİK: yeniden bağlanmada grup üyelikleri SIFIRLANIR.
      // Tekrar JoinAuction çağırmazsan bağlantı açık görünür ama
      // hiçbir olay gelmez — teşhis edilmesi en zor hatalardan biri.
      // Ayrıca kopukken kaçırdığın teklifler de bu çağrının cevabıyla gelir.
      join().catch(() => setStatus('error'));
    });

    // --- başlat ---
    const startPromise = connection
      .start()
      .then(() => {
        if (disposed) return;
        setStatus('connected');
        return join();
      })
      .catch((error) => {
        if (!disposed) {
          console.error('Hub bağlantısı kurulamadı', error);
          setStatus('error');
        }
      });

    return () => {
      disposed = true;
      // React 18 StrictMode (dev) effect'i iki kez çalıştırır: mount -> cleanup -> mount.
      // start() devam ederken stop() çağırmak hata fırlatır, bu yüzden
      // start'ın bitmesini bekleyip sonra kapatıyoruz.
      startPromise.catch(() => {}).then(() => connection.stop());
    };
  }, [auctionId, bidderId, bidderName]);

  const placeBid = useCallback(
    async (amount: number) => {
      const connection = connectionRef.current;
      if (!connection || connection.state !== HubConnectionState.Connected) {
        throw new Error('Bağlantı hazır değil, lütfen tekrar deneyin.');
      }
      setRejection(null);
      // Dikkat: bu çağrı "kabul edildi" demek DEĞİL. Sonuç
      // BidPlaced / BidRejected olayı olarak gelir.
      await connection.invoke('PlaceBid', auctionId, amount);
    },
    [auctionId],
  );

  return {
    auction, bids, viewerCount, status,
    rejection, outbid, ended,
    placeBid,
    dismissOutbid: () => setOutbid(null),
    isLive: auction?.status === 'Live',
    isMine: auction?.highestBidderId === bidderId,
  };
}
```

## 5. Bileşen

```tsx
// src/components/AuctionRoom.tsx
import { useState } from 'react';
import { useAuctionRoom } from '../hooks/useAuctionRoom';
import type { Bidder } from '../realtime/connection';

const money = new Intl.NumberFormat('tr-TR', {
  style: 'currency', currency: 'TRY', maximumFractionDigits: 0,
});

const statusLabel = {
  Scheduled: 'Başlamadı', Live: 'Canlı', Ended: 'Sona erdi', Cancelled: 'İptal',
} as const;

export function AuctionRoom({ auctionId, bidder }: { auctionId: string; bidder: Bidder }) {
  const {
    auction, bids, viewerCount, status,
    rejection, outbid, ended, placeBid, dismissOutbid, isLive, isMine,
  } = useAuctionRoom(auctionId, bidder);

  const [amount, setAmount] = useState('');
  const [sending, setSending] = useState(false);

  if (!auction) {
    return <p>{status === 'error' ? 'Bağlantı kurulamadı.' : 'Yükleniyor…'}</p>;
  }

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setSending(true);
    try {
      await placeBid(Number(amount));
      setAmount('');
    } catch (error) {
      console.error(error);
    } finally {
      setSending(false);
    }
  };

  return (
    <section>
      <header>
        <h2>{auction.title}</h2>
        <span>{statusLabel[auction.status]}</span>
        <span>👁 {viewerCount}</span>
        {status !== 'connected' && <span>⚠ {status}</span>}
      </header>

      <p>
        Güncel fiyat: <strong>{money.format(auction.currentPrice)}</strong>
        {auction.highestBidderName && (
          <> — {isMine ? 'en yüksek teklif sizde' : auction.highestBidderName}</>
        )}
      </p>
      <p>{auction.bidCount} teklif · en az {money.format(auction.minimumNextBid)} verebilirsiniz</p>

      {ended && (
        <p role="status">
          Sona erdi. {ended.winnerName
            ? `Kazanan: ${ended.winnerName} — ${money.format(ended.finalPrice)}`
            : 'Hiç teklif gelmedi.'}
        </p>
      )}

      {outbid && (
        <p role="alert">
          Teklifiniz geçildi: {money.format(outbid.yourBid)} → {money.format(outbid.newPrice)} ({outbid.newLeaderName})
          <button onClick={dismissOutbid}>kapat</button>
        </p>
      )}

      <form onSubmit={submit}>
        <input
          type="number" step="0.01" value={amount}
          placeholder={String(auction.minimumNextBid)}
          onChange={(e) => setAmount(e.target.value)}
          disabled={!isLive || sending}
        />
        <button type="submit" disabled={!isLive || sending || status !== 'connected'}>
          Teklif ver
        </button>
        {/* Sunucu alt sınırı söylüyor; kullanıcıya tek tıkla düzeltme sun. */}
        {rejection && (
          <p role="alert">
            {rejection.reason}
            {rejection.minimumRequired && (
              <button type="button" onClick={() => setAmount(String(rejection.minimumRequired))}>
                {money.format(rejection.minimumRequired)} yap
              </button>
            )}
          </p>
        )}
      </form>

      <ul>
        {bids.map((bid) => (
          <li key={bid.id}>
            <strong>{money.format(bid.amount)}</strong> — {bid.bidderName}
            <time dateTime={bid.createdAt}>
              {new Date(bid.createdAt).toLocaleTimeString('tr-TR')}
            </time>
          </li>
        ))}
      </ul>
    </section>
  );
}
```

Kullanımı:

```tsx
<AuctionRoom
  auctionId="aaaaaaaa-0000-0000-0000-000000000001"
  bidder={{ id: 'u-kemal', name: 'Kemal' }}
/>
```

İki tarayıcı sekmesini **farklı `bidder.id`** ile açıp teklif ver: birinde
verdiğin teklif diğerinde anında görünür, geçilen tarafta "teklifiniz geçildi"
uyarısı çıkar.

## Tuzaklar (okumadan geçme)

**Yeniden bağlanınca gruba tekrar katıl.**
En sinsi hata. `withAutomaticReconnect` bağlantıyı geri getirir ama **grup
üyelikleri sunucu tarafında yeni `ConnectionId` ile sıfırlanır.**
`onreconnected` içinde `JoinAuction`'ı tekrar çağırmazsan bağlantı "connected"
görünür, hiçbir olay gelmez ve sebebini saatlerce ararsın. Yukarıdaki hook bunu
yapıyor; kendi kodunu yazarken atlamayın.

**StrictMode effect'i iki kez çalıştırır.**
Development'ta mount → cleanup → mount olur. `connection.start()` sürerken
`stop()` çağırmak hata fırlatır. Çözüm: start promise'ini tut, cleanup'ta onu
bekleyip sonra `stop()` çağır (hook'ta örneği var).

**Olay dinleyicilerinde fonksiyonel setState kullan.**
Dinleyiciler effect ömrü boyunca yaşar. `setBids([...bids, yeni])` yazarsan
`bids` ilk render'daki değerdir — liste sürekli tek elemana düşer.
Her zaman `setBids(prev => …)`.

**`bidder` nesnesini bağımlılığa koyma.**
`useAuctionRoom(id, { id: 'u1', name: 'Kemal' })` her render'da yeni nesne üretir.
Effect bağımlılığında nesne olursa socket her render'da kopar ve yeniden kurulur.
Hook içinde `bidder.id` / `bidder.name` alanlarını kullanıyoruz.

**Teklifleri `bidId` ile tekilleştir.**
Kendi teklifin de `BidPlaced` olarak geri gelir. İyimser güncelleme yapıyorsan
(ya da yeniden bağlanıp listeyi tazeliyorsan) aynı teklif iki kez görünür.

**Olay handler'ından değer döndürme.**
SignalR, sunucunun beklemediği bir dönüş değeri görürse hata loglar:
`Error: Result given for 'bidplaced' method but server is not expecting a result.`
Sebebi genelde kısa arrow fonksiyonudur — `push` bir sayı döndürür:
```ts
conn.on('BidPlaced', (e) => bids.push(e));        // ✗ push() sayı döndürür
conn.on('BidPlaced', (e) => { bids.push(e); });   // ✓ süslü parantez
```
`setState(...)` zaten `undefined` döndürdüğü için sorun çıkarmaz; tuzak
`push`, `set`, `add` gibi değer döndüren çağrılarda.

**Bağlanmadan `invoke` çağırma.**
`HubConnectionState.Connected` değilse `invoke` reddedilir. Butonu
`status !== 'connected'` iken kapat.

**Geri sayımı sunucu saatine göre kur.**
`endsAt` sunucu saatiyle UTC gelir, kullanıcının saati kayık olabilir. İlk olayda
aradaki farkı ölç ve geri sayımı ona göre düzelt:

```ts
const offset = Date.now() - new Date(serverNow).getTime();
const remaining = new Date(auction.endsAt).getTime() + offset - Date.now();
```

**CORS + credentials.** SignalR istemcisi varsayılan olarak credentials gönderir;
backend `AllowCredentials` + **açıkça sayılmış** origin'lerle yapılandırıldı.
Backend'de `AllowAnyOrigin()`'e geçerseniz tarayıcı WebSocket'i reddeder —
ikisi birlikte kullanılamaz.

**Para birimi hassasiyeti.** Sunucu `decimal`, JSON'da sayı olarak gelir, JS'te
`double` olur. Bu büyüklüklerde sorun çıkmaz ama kuruş hassasiyeti kritikse
hesap yapma — ekranda sunucunun verdiği değerleri göster, `minimumNextBid`'i
kendin hesaplamaya kalkma.

**Ölçeklenmiş backend'de `negotiate` sorunu.** Backend birden fazla instance
ile çalışıyorsa (`docker compose -f docker-compose.yml up -d --scale auction.api=2`)
SignalR'ın `negotiate` adımı bir instance'a, WebSocket upgrade'i diğerine
gidebilir — bu durumda bağlantı kurulamaz.

Gateway'de cookie tabanlı session affinity yapılandırıldı ama bu kurulumda
garanti değil (tek mantıksal hedef arkasında Docker DNS dağıtım yapıyor).
En sağlam çözüm istemcide: `negotiate` adımını tamamen atla.

```ts
import { HttpTransportType } from '@microsoft/signalr';

new HubConnectionBuilder()
  .withUrl(`${base}/hubs/auction?${query}`, {
    // negotiate adımı hiç olmadığı için hangi instance'a gidildiği önemsiz
    skipNegotiation: true,
    transport: HttpTransportType.WebSockets,
  })
  .withAutomaticReconnect()
  .build();
```

Bedeli: taşıma katmanı yedeklemesi (SSE / long polling) kalkar, yani WebSocket
kurulamazsa bağlantı hiç olmaz. Tek instance çalışırken buna gerek yok;
ölçeklendiğinde ekle. İki yol da iki replica ile doğrulandı (10/10 bağlantı).

## Teklif: hub mı REST mi?

İkisi de aynı iş kurallarından geçer ve aynı canlı olayları yayınlar. Fark
sadece sonucu nasıl öğrendiğinde:

| | Hub `PlaceBid` | REST `POST /bids` |
|---|---|---|
| Sonuç | `BidPlaced` / `BidRejected` olayı | HTTP 201 / 409 cevabı |
| Gecikme | Açık socket, ek el sıkışma yok | Yeni HTTP isteği |
| Form doğrulaması | Olay beklemek gerekir | `catch` bloğunda hazır |
| Bağlantı kopuksa | Çalışmaz | Çalışır |

**Öneri:** canlı odada hub'ı kullan (zaten bağlısın, en hızlısı).
REST'i yedek yol olarak tut — socket kopukken ya da "tek seferlik teklif" gibi
ekranlarda:

```ts
try {
  await auctionApi.placeBid(auctionId, { amount, bidderId: bidder.id, bidderName: bidder.name });
} catch (error) {
  if (error instanceof ApiError && error.errorCode === 'bid_too_low') {
    setAmount(String(error.minimumRequired));
  }
}
```

## JWT eklendiğinde

Backend'e kimlik doğrulama eklendiğinde frontend'de değişecek **tek yer**
bağlantı kurulumu:

```ts
new HubConnectionBuilder()
  .withUrl(`${base}/hubs/auction`, {
    // Token'ı query string'e elle ekleme — istemci bunu kendisi yapar.
    // (WebSocket handshake'inde Authorization header gönderilemediği için
    // SignalR token'ı ?access_token= olarak taşır.)
    accessTokenFactory: () => getAccessToken(),
  })
  .withAutomaticReconnect()
  .build();
```

`bidderId` / `bidderName` query parametreleri kalkar; sunucu kimliği token'dan
okur. Olay sözleşmesi aynı kalır, bu dosyadaki hook ve bileşen değişmez.
