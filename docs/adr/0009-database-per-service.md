# ADR-0009: Database-per-Service

- **Status:** Accepted
- **Tarih:** 2026-07-04
- **Karar verenler:** Süleyman

## Context

OrderHub altı iş servisinden oluşur (Order, Payment, Analytics, Inventory, OrderProcessing/Saga,
Notification). Her servis kendi durumunu (state) kalıcılaştırır. Temel soru: **bu servisler tek bir
paylaşılan veritabanını mı yoksa her biri kendi veritabanını mı kullansın?**

Bu karar Faz 1'den (OrderService) itibaren örtük olarak uygulandı ve her yeni serviste tekrarlandı
(`OrderHub_Order`, `OrderHub_Payment`, `OrderHub_Analytics`, `OrderHub_Inventory`, `OrderHub_Sagas`,
`OrderHub_Notifications` — hepsi **tek SQL Server instance**'ında ayrı database'ler). Karar birçok ADR'de
(0006 CQRS read-model, 0007 saga) atıfla geçiyordu ancak **kendi ADR'i yoktu**; bu kayıt onu açık hale
getirir (mülakat sorusu: *"neden database-per-service?"*).

Mikroservis mimarisinin merkezî ilkesi **loose coupling** ve **bağımsız evrim**tir. Veritabanı, bu
bağımsızlığın en sık sızdığı yerdir: iki servis aynı tabloyu paylaşırsa, birinin şema değişikliği
diğerini kırar ve "bağımsız servis" iddiası çöker.

## Decision

**Her servis kendi veritabanına sahiptir (database-per-service).** Bir servisin verisine yalnızca o
servis erişir; başka bir servis o veriye **doğrudan DB üzerinden değil, yalnızca o servisin API'si veya
yayımladığı event'ler üzerinden** ulaşır.

- **Fiziksel yerleşim (dev):** Tek SQL Server instance, servis başına ayrı **database** (maliyet/operasyon
  basitliği; dev'de altı ayrı sunucu gereksiz). İzolasyon **logical**'dır (ayrı database + ayrı
  connection string + ayrı DbContext + ayrı migration geçmişi). Prod'da bu database'ler ayrı instance'lara
  taşınabilir — uygulama kodu değişmez (yalnız connection string).
- **Cross-service veri paylaşımı = event-driven projection.** Bir servisin başka servisin verisine
  ihtiyacı olduğunda **JOIN yapılmaz**; veri event olarak akar ve tüketen servis kendi read-model'ini
  kurar. Kanonik örnek: **AnalyticsService** (ADR-0006) — Order verisini Kafka `order-hub.orders.events`
  stream'inden tüketip kendi `OrderHub_Analytics` projection'ında tutar. Cross-database JOIN yerine
  **eventual consistency**.
- **Saga da bu kurala uyar (ADR-0007).** OrderProcessingService, OrderService aggregate'ine DB üzerinden
  girmez; command/event ile konuşur, kendi saga state'ini `OrderHub_Sagas`'ta tutar.

## Consequences

- **Olumlu — bağımsızlık:** Her servis şemasını diğerlerini kırmadan evriltir; ayrı deploy/scale edilebilir;
  bir DB'nin sorunu diğerlerini şema düzeyinde etkilemez. "Servis sahibi verisinin de sahibidir" netleşir.
- **Olumlu — güvenlik/sınır:** Bir servis diğerinin tablosunu "gizlice" okuyamaz → kontrat (API/event) tek
  giriş; bağımlılıklar görünür kalır.
- **Olumsuz / maliyet — cross-service query yok:** "Tüm sipariş + ödeme + stok tek sorguda" mümkün değil.
  Bu bilinçli bir takas: raporlama/okuma ihtiyaçları **CQRS read-model** (AnalyticsService) ile karşılanır.
- **Olumsuz / maliyet — eventual consistency:** Projection'lar anlık değil (Kafka consume gecikmesi). Bu
  yüzden idempotent consumer + dedup (ADR-0005/0006) zorunlu; "hemen tutarlı" beklentisi yanlış olur.
- **Olumsuz / maliyet — dağıtık transaction yok:** Servisler arası atomiklik 2PC ile değil, **saga +
  compensating transaction** ile sağlanır (ADR-0007). Bu, database-per-service'in doğrudan sonucudur.

## Alternatives Considered

### Seçenek A: Shared database (tek DB, servisler ortak tablolar)

- **Artılar:** Cross-service JOIN kolay; anlık tutarlılık; başlangıçta daha az altyapı.
- **Eksiler:** Servisleri **şema düzeyinde birbirine bağlar** → bir tablo değişikliği birden çok servisi
  kırar; bağımsız deploy/scale imkânsız; "mikroservis" iddiası pratikte distributed monolith'e döner;
  sahiplik sınırı yok (herkes her tabloyu okur/yazar → gizli coupling).
- **Karar:** **Reddedildi** — projenin temel amacı (bağımsız, event-driven mikroservisler) ile çelişiyor.

### Seçenek B: Schema-per-service (tek DB, servis başına ayrı schema)

- **Artılar:** Tek instance içinde bir miktar izolasyon; ayrı database'den biraz daha hafif.
- **Eksiler:** Hâlâ **tek fiziksel DB** → tek arıza noktası, birlikte scale, prod'da ayrı instance'a taşıma
  zor; izolasyon database sınırından daha zayıf (yanlışlıkla cross-schema erişim kolay).
- **Karar:** **Reddedildi** — ayrı database, "prod'da ayrı instance'a taşınabilir" evrim yolunu bedelsiz
  açık tutuyor; schema-per-service bu esnekliği vermiyor.

## İlgili

- [ADR-0006](0006-kafka-event-streaming.md) — CQRS read-model (AnalyticsService), cross-service veri event ile akar
- [ADR-0007](0007-saga-orchestration.md) — dağıtık transaction yerine saga + compensation (database-per-service sonucu)
- [ADR-0005](0005-custom-inbox.md) — idempotent consumer (eventual consistency'nin gereği)
- [CLAUDE.md](../../CLAUDE.md) §4 (klasör yapısı — servis başına Infrastructure/DbContext)
