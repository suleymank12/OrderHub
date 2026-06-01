# ADR-0003: Hangfire Storage, Timeout Reliability ve Dashboard Security

- **Status:** Accepted
- **Tarih:** 2026-06-01
- **Karar verenler:** Süleyman

## Context

Faz 2 OrderService'e Hangfire ekliyor. ROADMAP §2.1 iki direktif veriyor: *"ayrı bir DB
(`OrderHub_Hangfire`)"* ve *"dashboard JWT authorization filter ile korumalı"*. §2.2 ise
ödenmeyen sipariş için *"15 dakika sonra delayed job"* istiyor. Bu üç direktifin her biri,
literal alındığında gerçek bir prod riski taşıyor ve birbirine bağlı:

- **Storage:** Ayrı DB, order-commit ile job-enqueue'yu iki ayrı veritabanına yazar → **dual-write**
  penceresi (commit oldu, enqueue olmadan crash → sipariş sonsuza dek `Pending`).
- **Timeout:** Tek bir per-order delayed job, Hangfire server crash / worker restart / schedule drift
  durumunda sessizce kaybolabilir → garanti yok.
- **Dashboard auth:** JWT bearer, Authorization **header**'da taşınır; tarayıcı `/hangfire`'a
  navigasyonda header gönderemez → JWT filter insan kullanıcı için kullanılamaz.

Bu üç soru birbirini şekillendirdiği için (storage seçimi → timeout mekanizmasını, timeout güvenilirliği →
storage gereksinimini belirliyor) tek bir birleşik ADR'de karara bağlanıyor.

## Decision

### 1. Storage — `OrderHub_Order` DB içinde ayrı `HangFire` şeması

Hangfire tabloları, mevcut `OrderHub_Order` veritabanında ayrı bir `HangFire` şemasında tutulur;
ayrı `OrderHub_Hangfire` DB'si **kurulmaz**. Compose'a değişiklik gerekmez (DB zaten mevcut), tek
connection/backup/migration noktası kalır.

> **Dürüst nüans:** Aynı DB **otomatik** atomik enqueue vermez — Hangfire `BackgroundJob.Schedule`
> kendi connection'ını açar, order'ın EF transaction'ına dahil olmaz. Ancak bu kabul edilebilir:
> commit↔enqueue arası kayıp, aşağıdaki **sweep backstop** ile zaten kapatılıyor. Yani aynı DB'yi
> seçmemizin gerekçesi atomiklik değil, **operasyonel basitlik** ve kırılgan transactional-enqueue
> plumbing'inden kaçınmaktır; güvenilirliği sweep sağlar.

**Schema provisioning:** `Hangfire.SqlServer`'ın `PrepareSchemaIfNecessary` ayarı **yalnızca
Development'ta açık** (`= IsDevelopment()`). Production'da otomatik schema oluşturma **kapalı** kalır
— ADR-0001'in EF migration prod-safe pattern'i birebir Hangfire şemasına da uygulanır (prod schema
out-of-band/bundle ile, Faz 7 evrim yolu). Faz 2'de yalnız Development koşulduğundan prod yolu tanımlı
ama henüz exercised değildir.

### 2. Timeout — Hibrit (delayed job + sweep backstop)

- **Ana yol:** `OrderCreatedDomainEventHandler` içinde `BackgroundJob.Schedule(15dk)` →
  `CancelUnpaidOrderJob` (tam zamanlama, Hangfire scheduling showcase).
- **Backstop:** Recurring sweep job (her 5 dk) → `Status == Pending && CreatedAtUtc < UtcNow - 15dk`
  olan siparişleri `Cancel("payment_timeout")`. Hangfire server crash, worker restart, schedule drift
  ve commit↔enqueue penceresini kapsar (defense in depth).
- Payment timeout business-critical olduğu için "belt and suspenders" çift güvenlik mantıklı.
  `Cancel` zaten `Pending` guard'lı → delayed job ve sweep aynı order'ı işlese de çift cancel olmaz
  (idempotent, bkz. Karar 5).

### 3. Dashboard auth — Development-only + localhost

- `MapHangfireDashboard` yalnızca `app.Environment.IsDevelopment()` içinde map edilir.
- `LocalhostDashboardAuthorization` filter → sadece localhost'tan erişim.
- Production'da `/hangfire` **kod yolundan silinmiş** → attack surface sıfır (Faz 1.5'in
  "dev-only token üretimi" pattern'inin tekrarı, tutarlı politika).
- Prod dashboard, Faz 6'da Gateway (YARP) arkasında reverse-proxy auth ile açılacak.

### 4. Timezone — Tüm scheduling explicit `TimeZoneInfo.Utc`

Hangfire cron'u default **server local time** yorumlar (kırılgan). Recurring tanımları explicit UTC
ile kaydedilir; mevcut `UtcDateTimeConverter` disiplini ile tutarlı. "Önceki gün" aggregate'inin gün
sınırı belirsizliğe düşmez.

### 5. Retry + Idempotency — Standing rule

- `AutomaticRetry`: 3 deneme, exponential backoff (Hangfire built-in).
- At-least-once teslimat → **her job re-execution-safe olmalı** (proje boyunca kural).
- `CancelUnpaidOrderJob`: `Pending` status guard → idempotent.
- `DailySalesReportJob`: log idempotent (yan etki yok).
- `LowStockAlertJob`: placeholder → idempotency trivial.

### 6. Serialization — §3 Newtonsoft.Json istisnası

Hangfire, iş argümanlarını **dahili olarak Newtonsoft.Json** ile serialize eder (`Hangfire.Core`'un
transitive bağımlılığı). CLAUDE.md §3 Newtonsoft'u "istisna yok" diye yasaklar; bu yasak **bizim**
serialization tercihlerimiz içindir (API/event payload'ları → System.Text.Json). Hangfire'ın internal
kullanımı kontrol etmediğimiz, doğrudan çağırmadığımız bir implementasyon detayıdır → **bilinçli
istisna**. Yüzey minimumda tutulur: job argümanları yalnızca primitive (`Guid orderId`) geçirilir,
karmaşık nesne serialize edilmez.

## Consequences

- **Olumlu:** Tek DB → tek backup/migration/connection; dual-write plumbing yok. Hibrit timeout,
  tekil mekanizmaların hiçbirinin tek başına kapatamadığı failure mode'ları kapsar. Dashboard prod'da
  yok → sıfır attack surface, K3 korunur. UTC + retry/idempotency disiplini deterministik davranış verir.
- **Olumsuz / dikkat:** Hangfire tabloları domain DB'sinde "yaşar" (şema ayrımı ile izole ama aynı
  instance). Sweep her 5 dk bir sorgu çalıştırır (indeksli `Status`+`CreatedAtUtc` ile ucuz, ihmal
  edilebilir). Aynı DB ölçeklenince I/O kontansiyonu olabilir → tek-servis ölçeğinde sorun değil.
- **Evrim yolu (yeni doğan görevler):** (a) Faz 4'te `DailySalesReportJob` Kafka'ya publish edince
  at-least-once → çift event riski doğar; o noktada outbox/dedup gerekecek (idempotent producer).
  (b) Faz 6'da Gateway arkasında prod dashboard auth. (c) Yük artarsa Hangfire ayrı instance'a taşınabilir
  (storage soyutlaması korunduğu için düşük maliyet).

## Alternatives Considered

### Storage — Ayrı `OrderHub_Hangfire` DB (ROADMAP literal)

- **Artılar:** Domain DB'si tamamen temiz; bağımsız ölçeklenir.
- **Eksiler:** Dual-write penceresi; compose'a 2. DB + init/migration; iki backup noktası.
- **Karar:** **Reddedildi** — ayrım faydası, dual-write maliyetini ve operasyonel yükü karşılamıyor.

### Timeout — Sadece per-order delayed job (ROADMAP literal)

- **Artılar:** En basit; Hangfire scheduling'i birebir gösterir.
- **Eksiler:** Server crash / worker restart / drift → garanti yok; asılı `Pending` order riski.
- **Karar:** **Reddedildi** — backstop'suz business-critical timeout kabul edilemez (hibrit ana yolu kapsıyor).

### Timeout — Sadece recurring sweep

- **Artılar:** Dual-write yok, self-healing.
- **Eksiler:** ~5 dk granülarite (tam değil); `BackgroundJob.Schedule` showcase'i kaybolur.
- **Karar:** **Reddedildi** — hibrit, tam zamanlama + crash-safety'i birlikte verir.

### Dashboard — JWT authorization filter (ROADMAP literal)

- **Artılar:** Tek kimlik sistemi (API ile aynı JWT).
- **Eksiler:** Tarayıcı navigasyonu bearer header göndermez → insan kullanıcı dashboard'u açamaz; pratikte kullanılamaz.
- **Karar:** **Reddedildi** — interaktif UI için bearer-header modeli yanlış araç.

### Dashboard — Basic auth filter

- **Artılar:** Her ortamda tarayıcı-uyumlu çalışır.
- **Eksiler:** Ek secret yönetimi (K3); JWT'den ayrı ikinci kimlik; prod'da hâlâ açık endpoint.
- **Karar:** **Reddedildi** — dev-only daha az yüzey, daha az secret, daha basit.

## İlgili

- [CLAUDE.md](../../CLAUDE.md) §2 (K3 güvenlik/secret), §9 (prod-safe), §7 (test policy)
- [ROADMAP.md](../../ROADMAP.md) §2.1 (storage + dashboard), §2.2 (timeout), §2.3 (recurring report)
- [ADR-0001](0001-migration-in-docker.md) (dev-only environment guard pattern — dashboard ile tutarlı)
- [ADR-0002](0002-in-process-domain-event-dispatch.md) (sweep backstop, dispatcher garanti boşluğunu kapatır)
- İleride: ADR (Outbox pattern adoption) — Faz 3; ADR (Kafka producer idempotency) — Faz 4
