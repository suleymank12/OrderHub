# OrderHub — Detaylı Yol Haritası

> Bu dosya CLAUDE.md'nin altında çalışır. CLAUDE.md'deki **mutlak kurallar** her fazda geçerlidir.
> Her faz başlamadan önce ilgili bölümü baştan oku, kabul kriterlerini içselleştir, sonra başla.

---

## Genel Akış

```
Faz 1: Foundation          → OrderService MVP + Docker + ilk testler
Faz 2: Hangfire            → Scheduled jobs + recurring jobs
Faz 3: RabbitMQ + Outbox   → PaymentService + reliable messaging
Faz 4: Kafka + Analytics   → Event stream + read model
Faz 5: Inventory + Saga    → Distributed transaction + compensation
Faz 6: Gateway + Observ.   → YARP + OTel + Polly + healthchecks
Faz 7: Docs + CI           → README + ADR + diagrams + GitHub Actions
```

**Bağımlılık:** Fazlar sırayla yapılır. Faz N, Faz N-1'in tüm kabul kriterleri yeşil olmadan başlamaz.

---

# Faz 1 — Foundation

**Hedef:** OrderService'i Clean Architecture ile kur, Docker'da ayağa kaldır, ilk unit + integration testlerle birlikte teslim et.

**Agent öncelik sırası:** `architect` → `backend-developer` → `devops-engineer` → `test-engineer`

## 1.1 Solution skeleton

- [ ] `OrderHub.sln` oluştur
- [ ] `Directory.Build.props` — ortak `<LangVersion>`, `<Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<ImplicitUsings>enable</ImplicitUsings>`
- [ ] `Directory.Packages.props` — Central Package Management (CPM) ile tüm versiyon yönetimi
- [ ] `.editorconfig` — stil kuralları, naming, line ending (LF)
- [ ] `.gitignore` — VS, Rider, bin/obj, .env, user-secrets
- [ ] `BuildingBlocks/OrderHub.Common` projesi — `Result<T>`, `DomainException`, `IDomainEvent`, `Entity<TId>`, `AggregateRoot<TId>` primitive'leri

## 1.2 OrderService — Domain

- [ ] `Order` aggregate root: `Id (Guid)`, `CustomerId`, `Items`, `Status`, `Total`, `CreatedAt`, `ConfirmedAt`
- [ ] `OrderItem` entity
- [ ] `OrderStatus` enum: `Pending`, `Confirmed`, `Paid`, `Shipped`, `Cancelled`
- [ ] `Money` value object (decimal + currency)
- [ ] `Address` value object
- [ ] Domain events: `OrderCreated`, `OrderConfirmed`, `OrderCancelled`
- [ ] Domain exceptions: `OrderAlreadyConfirmedException`, `EmptyOrderException`, `InvalidOrderStatusTransitionException`
- [ ] Behavior method'lar: `Order.Create()`, `order.Confirm()`, `order.Cancel(reason)`
- [ ] **Hiçbir setter public değil.**

## 1.3 OrderService — Application

- [ ] MediatR registration
- [ ] FluentValidation registration
- [ ] Pipeline behaviors: `ValidationBehavior`, `LoggingBehavior`, `TransactionBehavior`
- [ ] `CreateOrderCommand` + handler + validator
- [ ] `GetOrderByIdQuery` + handler
- [ ] `ListOrdersQuery` + handler (paged)
- [ ] DTO'lar — `OrderDto`, `OrderItemDto`, `CreateOrderRequest`
- [ ] Mapster konfigürasyonu

## 1.4 OrderService — Infrastructure

- [ ] `OrderDbContext` — `Orders`, `OrderItems` DbSet'leri
- [ ] EF Core configuration (IEntityTypeConfiguration) — value object owned types, index'ler
- [ ] `OrderRepository : IOrderRepository` (Application'da interface)
- [ ] İlk migration: `InitialCreate`
- [ ] Connection string `appsettings.json`'da placeholder, gerçek değer `.env` üzerinden compose'a geçer

## 1.5 OrderService — Api

- [ ] Minimal API mı Controller mı? → **Controller** (büyük proje için daha yapısal)
- [ ] `OrdersController`: `POST /api/orders`, `GET /api/orders/{id}`, `GET /api/orders`
- [ ] Global exception handler middleware → `DomainException` → 400/409, `ValidationException` → 400, generic → 500
- [ ] JWT bearer authentication (configurable, prod-ready setup ama Faz 1'de basit local key)
- [ ] Swagger / OpenAPI (Swashbuckle) — JWT auth desteği ile
- [ ] Serilog bootstrap (console + file)
- [ ] Health check: `/health/live`, `/health/ready` (DB)

## 1.6 Docker

- [ ] Multi-stage Dockerfile (`sdk` → `aspnet`), non-root user, healthcheck
- [ ] `docker-compose.yml`:
  - `sqlserver` (mssql/server:2022-latest), persistent volume, healthcheck
  - `seq` (datalust/seq), persistent volume
  - `orderservice` (build from src), depends_on healthy DB
- [ ] `.env.example` dosyası — gerçek `.env` gitignored
- [ ] `docker-compose.override.yml` — local dev port mapping (gitignored)

## 1.7 Testler

- [ ] `OrderService.UnitTests` — `Order.Cancel_AlreadyCancelled_Throws`, `Order.AddItem_ZeroQuantity_Throws`, vs.
- [ ] `OrderService.UnitTests` — `CreateOrderCommandHandler_ValidRequest_ReturnsOrderId`, `_InvalidCustomer_ReturnsValidationError`
- [ ] `OrderService.IntegrationTests` — Testcontainers ile gerçek SQL Server container
  - `WebApplicationFactory` ile API host'lanır
  - `POST /api/orders` → 201, DB'de kayıt var
  - `GET /api/orders/{id}` → 200
- [ ] `dotnet test` → tüm yeşil
- [ ] Coverage report (coverlet + ReportGenerator)

## 1.8 Kabul Kriteri

- ✅ `docker-compose up -d` ile tüm stack ayağa kalkıyor
- ✅ Swagger UI'dan order create edebiliyorsun, JWT ile auth çalışıyor
- ✅ Tüm testler yeşil (`dotnet test`)
- ✅ Coverage rapor üretilebiliyor
- ✅ Hiçbir kod dosyası 400 satırı geçmiyor (`find src tests -name "*.cs" | xargs wc -l | awk '$1>400'` boş çıkmalı)
- ✅ Hiçbir secret repo'da yok (`git grep -i "password\|secret\|connectionstring=Server"` sadece placeholder'ları bulmalı)

**Tahmini süre:** 6-8 saat part-time

---

# Faz 2 — Hangfire

**Hedef:** OrderService'e Hangfire entegre et, ödenmeyen sipariş otomatik cancel'lansın, recurring günlük rapor job'u çalışsın.

**Agent öncelik sırası:** `backend-developer` → `test-engineer`

## 2.1 Hangfire setup

- [ ] `Hangfire.AspNetCore` + `Hangfire.SqlServer` paketleri
- [ ] Hangfire için **ayrı bir DB** (`OrderHub_Hangfire`) — domain DB'sini kirletme
- [ ] Hangfire dashboard `/hangfire` endpoint'i, **JWT authorization filter** ile korumalı (anonim erişim YASAK)
- [ ] DI registration — `IBackgroundJobClient`, `IRecurringJobManager`

## 2.2 Scheduled job — Order timeout

- [ ] `OrderCreatedDomainEventHandler` içinde Hangfire delayed job schedule et: 15 dakika sonra `CancelUnpaidOrderJob`
- [ ] `CancelUnpaidOrderJob.ExecuteAsync(Guid orderId)`:
  - Order'ı yükle, status'u `Pending` ise `Cancel("payment_timeout")` çağır
  - Idempotent: zaten Paid/Cancelled ise no-op
- [ ] Job'un retry policy'si: 3 deneme, exponential backoff

## 2.3 Recurring job — Günlük satış raporu

- [ ] `DailySalesReportJob` — her gece 02:00'da çalışır
- [ ] Önceki gün siparişlerini aggregate eder, Seq'e structured log basar (Faz 4'te Kafka'ya event olarak yayımlanacak — şimdilik log yeterli)
- [ ] CRON: `0 2 * * *`

## 2.4 Recurring job — Stok eşik uyarısı

- [ ] `LowStockAlertJob` — saatlik
- [ ] Faz 5'te InventoryService geleceği için **şimdilik placeholder** — sadece "InventoryService entegre edilmedi" log'u atar
- [ ] **Not:** Bu placeholder K2'ye karşı değil mi? — Hayır, çünkü InventoryService henüz **yok**. Var olmayan servise call atamayız. Faz 5'te bu job'un implementation'ı tamamlanır. ROADMAP'te explicit olarak belirtildiği için K2 ihlali değil.

## 2.5 Testler

- [ ] Unit: `CancelUnpaidOrderJob_OrderAlreadyPaid_DoesNothing`
- [ ] Unit: `CancelUnpaidOrderJob_OrderPending_CancelsOrder`
- [ ] Integration (Testcontainers): job schedule edildi → 15 sn'lik kısa timeout ile test → cancel oldu

## 2.6 Kabul Kriteri

- ✅ Sipariş oluştur → 15 dk ödenmezse otomatik cancel olur (test'te 15 sn ile doğrulanır)
- ✅ Hangfire dashboard çalışır, JWT olmadan erişilemez
- ✅ Günlük rapor job'u Hangfire'da görünür (next run zamanı var)
- ✅ Tüm yeni kod test'li, 400 satır kuralı korunmuş

**Tahmini süre:** 4-5 saat

---

# Faz 3 — RabbitMQ + Outbox + PaymentService

**Hedef:** PaymentService'i ayağa kaldır, OrderService ile RabbitMQ üzerinden command-based iletişim kur. **Outbox pattern** ile DB transaction + message publish atomikliğini garanti et.

**Agent öncelik sırası:** `architect` → `messaging-engineer` → `backend-developer` → `test-engineer`

## 3.1 BuildingBlocks/OrderHub.EventBus

- [x] `IIntegrationEvent` interface (`Guid Id`, `DateTime OccurredOn`)
- [x] `IIntegrationEventPublisher` interface (Application'dan kullanılır)
- [x] MassTransit ile RabbitMQ wrapper

## 3.2 BuildingBlocks/OrderHub.Outbox

- [x] `OutboxMessage` entity (id, type, payload, occurred_on, processed_on, retry_count, error)
- [x] `OutboxInterceptor` (EF Core `SaveChangesInterceptor`) — domain event'leri tespit edip outbox'a yazar, **aynı transaction içinde**
- [x] `OutboxProcessorService` (HostedService) — outbox'ı polling ile okur, MassTransit ile publish eder, başarılıysa `processed_on` set eder
- [x] Polling interval konfigürasyon (default 2 sn)
- [x] Retry policy + DLQ (max 5 retry → manual intervention)
- [x] **Idempotency:** outbox'a aynı `Id` iki kez yazılamaz (unique index)

> ⚠️ **Senior note:** Outbox **ZORUNLU**. RabbitMQ'ya direkt publish + DB commit ardışık yapılırsa, ikinci adım fail olursa event kaybolur veya duplicate olur. Outbox bunu çözer. Bu pattern atlanamaz.

## 3.3 PaymentService — yeni servis

- [x] Aynı Clean Architecture yapısı (4 katman)
- [x] `Payment` aggregate: `Id`, `OrderId`, `Amount`, `Status`, `ExternalTransactionId`
- [x] Mock payment provider (random success/failure, configurable %)
- [x] Kendi DB'si: `OrderHub_Payment` (database-per-service)
- [x] Outbox setup (aynı pattern)

## 3.4 Command flow — Order → Payment

- [x] OrderService: `Order.Confirm()` çağrıldığında → `ProcessPaymentIntegrationEvent` outbox'a yazılır → RabbitMQ'ya publish edilir
- [x] MassTransit topology:
  - Exchange: `order-hub.payment` (type: direct veya topic, karar ADR'de)
  - Queue: `payment-service.process-payment`
  - Routing key: `payment.process`
- [x] PaymentService: `ProcessPaymentIntegrationEventConsumer` — payment'ı işler, sonucu kendi event'i ile yayımlar (`PaymentSucceededIntegrationEvent` veya `PaymentFailedIntegrationEvent`)
- [x] OrderService: `PaymentSucceededIntegrationEventConsumer` — order'ı `Paid`'e geçirir; `PaymentFailedIntegrationEventConsumer` — order'ı `Cancelled`'a geçirir

## 3.5 Retry + DLQ

- [x] MassTransit retry policy: exponential backoff, max 5 deneme
- [x] DLQ: `_error` suffix queue'ları (MassTransit default)
- [x] **Idempotent consumer:** her consumer mesaj id'sini bir tabloda tutar, aynı id ikinci kez işlenmez (Inbox pattern). Bu **ZORUNLU**.

## 3.6 docker-compose güncelleme

- [x] `rabbitmq:3.13-management` servisi (management UI 15672'de)
- [x] `paymentservice` build
- [x] Healthcheck'ler

## 3.7 Testler

- [x] Unit: Outbox interceptor → domain event detection, payload serialization
- [x] Unit: Inbox consumer → duplicate message handling
- [x] Integration (Testcontainers + RabbitMQ container):
  - Order confirm → Payment processed → Order paid (happy path)
  - Order confirm → Payment fails → Order cancelled (compensation)
  - Duplicate message → idempotent skip

## 3.8 Kabul Kriteri

- ✅ Order confirm edilince Payment otomatik işlenir, OrderService event'i alır, status `Paid` olur
- ✅ Payment fail olursa OrderService order'ı `Cancelled`'a geçirir
- ✅ RabbitMQ down olursa outbox'ta mesajlar birikir, ayağa kalkınca otomatik publish olur (test edilir)
- ✅ Aynı mesaj iki kez gelirse ikinci işlenmez (inbox test)
- ✅ DLQ'da mesaj varsa Seq'te alert log'u
- ✅ Tüm testler yeşil, 400 satır kuralı korunmuş

**Tahmini süre:** 10-14 saat (MassTransit + Outbox ilk kez ise öğrenme dahil)

---

# Faz 4 — Kafka + AnalyticsService

**Hedef:** OrderService'in domain event'lerini Kafka'ya stream et. AnalyticsService bu stream'i tüketip read-model üretsin (CQRS read-side).

**Agent öncelik sırası:** `architect` → `messaging-engineer` → `backend-developer` → `test-engineer`

## 4.1 Mimari karar — neden hem RabbitMQ hem Kafka?

ADR yaz: `docs/adr/0006-kafka-event-streaming.md` (0001 zaten migration-in-docker'a ait → çakışma giderildi). Özet:
- **RabbitMQ:** Command-style, point-to-point, "bu iş yapılmalı" semantiği. Retry/DLQ kolay. Düşük throughput'lu critical messaging.
- **Kafka:** Event log, pub-sub, replay edilebilir, yüksek throughput. Multiple consumer'lar bağımsız offset'le aynı stream'i okur. Analytics, audit, future consumer için.

Mülakatta savunacağın asıl argüman bu — ADR'yi ciddi yaz.

## 4.2 Kafka topology

- [x] Topic: `order-hub.orders.events`, replication factor 1 (single-broker dev) — compose auto-create.
- [ ] Topic: `order-hub.payments.events` → **bilinçli ertelendi (ADR-0006 Karar 5, YAGNI)**: topolojide tanımlı ama üretici/consumer yok (PaymentService Kafka'ya produce etmez); audit consumer gelince doldurulur.
- [x] Schema: JSON (Avro/Protobuf overkill bu scope için — ADR-0006 Karar 5)
- [x] Key: `OrderId` (aynı order eventleri aynı partition'a → ordering garantisi) — `IKafkaEvent.PartitionKey`.

> **Not (4-final, dürüstlük):** `order-hub.orders.events` dev'de `KAFKA_AUTO_CREATE_TOPICS_ENABLE` ile
> oluşur → **tek partition** (§4.2 "partition count 3" prod-provisioning hedefiydi). Per-order ordering
> partition sayısından **bağımsız** korunur (key=OrderId → aynı order hep aynı partition). Single-broker /
> single-consumer dev'de 3-partition parallelism gereksiz; prod'da `KAFKA_NUM_PARTITIONS=3` veya pre-provision
> ile sağlanır. Gerçek deviation: 3 değil 1 partition — gizlenmedi, fonksiyonel etki yok.

## 4.3 Producer side — OrderService

- [x] Outbox processor şimdi iki target'a publish ediyor (`RoutingIntegrationEventPublisher`):
  - Command event'leri → RabbitMQ (`IRabbitMqEvent`)
  - Domain event'leri (notification event'leri) → Kafka (`IKafkaEvent`)
- [x] Event sınıflandırma: `IRabbitMqEvent`, `IKafkaEvent` marker interface'leri (tipine göre routing)
- [x] Confluent.Kafka producer config: idempotent producer, `Acks=All`, `EnableIdempotence=true` (ADR-0006 Karar 2)

## 4.4 AnalyticsService

- [x] Yeni servis, Clean Architecture (Domain/Application/Infrastructure/Api)
- [x] Kendi DB'si: `OrderHub_Analytics` (read-optimized schema, database-per-service)
- [x] Kafka consumer (HostedService): `OrderEventsConsumer` (+ topic-yok startup self-heal, 4e)
- [x] Tablo: `OrderProjection` (id, customer_id, status, total, created_at, paid_at, last_updated)
- [x] Tablo: `DailyRevenueProjection` (date, total_orders, total_revenue, avg_order_value)
- [x] Consumer offset commit: manual commit, **işlendikten sonra** (DB-commit → offset-commit, at-least-once)
- [x] Idempotent projection update (event-id dedup, `InboxMessage` entity reuse — ADR-0006 Karar 6)

## 4.5 Analytics API

- [x] `GET /api/analytics/orders/{id}` — order projection (JWT korumalı)
- [x] `GET /api/analytics/revenue/daily?from=&to=` — günlük revenue (JWT korumalı)
- [x] **Sadece okuma**, hiçbir write endpoint yok (CQRS read-side; projection yalnız consumer ile güncellenir)

## 4.6 docker-compose güncelleme

- [x] Kafka (Confluent cp-kafka:7.6.1 KRaft mode — Zookeeper-less; Testcontainers fixture'larıyla aynı major.minor)
- [x] Schema registry **eklemiyoruz** (JSON kullanıyoruz, ADR-0006'da gerekçe var)
- [x] Kafka UI (provectuslabs/kafka-ui:v0.7.2) — debug için
- [x] `analyticsservice` build (OrderHub_Analytics, Kafka consumer + read-API; depends_on sqlserver+kafka healthy)

## 4.7 Testler

- [x] Unit: read-side query logic (`GetOrderProjectionById` 4, `GetDailyRevenue` 3 + validator 3 = 10 unit).
  > **Dürüstlük:** Projection *update logic* + *idempotency/dedup* için **pure unit test YOK** — bilinçli olarak
  > **gerçek Kafka integration** ile kanıtlandı (`DuplicateOrderPaid_DedupPreventsDoubleRevenue`): consumer I/O-bound,
  > mocklu unit'ten daha anlamlı (K2: suni unit eklemedik). Projection domain davranışı (MarkConfirmed/MarkPaid/
  > AddPaidOrder) consumer e2e ile uçtan uca koşuyor.
- [x] Integration (Testcontainers + gerçek Kafka container):
  - [x] Order created → AnalyticsService'te projection oluştu (`Consumer_AppliesCreatedConfirmedPaidInOrder…`)
  - [x] Aynı event iki kez geldi → projection/revenue bir kez (`DuplicateOrderPaid_DedupPreventsDoubleRevenue…`)
  - [x] (4e) Topic-yok startup yarışı → consumer self-heal (`Consumer_StartedBeforeTopicExists_SurvivesAndConsumes…`)
  - [ ] **Consumer lag: 100 event publish → hepsi sırayla** — YAZILMADI (dürüst boşluk). Ordering + at-least-once
        zaten kanıtlı: lifecycle sıra testi (tek partition Created→Confirmed→Paid) + `MultipleOrdersPaidSameDay`
        (revenue aggregate) + offset-commit-after kanıtı. Açık throughput/volume testi Faz 5 follow-up — gizlenmiyor.

## 4.8 Kabul Kriteri

- ✅ Order create/confirm/pay → AnalyticsService'te projection güncelleniyor
  → **Kanıt:** `Consumer_AppliesCreatedConfirmedPaidInOrder_ThenCommitsOffsetAfterProcessing` (gerçek Kafka,
  Created→Confirmed→Paid → projection status=Paid). HTTP'de yalnız OrderCreated tetiklenebilir (4-final fresh-volume
  smoke: order create → projection Created); Confirm/Pay domain seam'i integration testlerinde (§4.8.1 notu).
- ✅ `GET /api/analytics/revenue/daily` doğru aggregate dönüyor
  → **Kanıt:** `MultipleOrdersPaidSameDay_DailyRevenueAggregatesCorrectly` (100+200+300 → total 600, avg 200) +
  `AnalyticsEndpointsTests` (API read-side, JWT).
- ✅ Kafka down olsa bile outbox kaybetmiyor
  → **Kanıt (dürüst):** **Otomatik Kafka-down testi YOK.** Outbox **transport-agnostik** aynı mekanizma — RabbitMQ
  broker-down otomatik testi (Faz 3 §3.8 / 3d-4b) outbox-birikir-recover'ı zaten kanıtlar; Kafka publish **aynı**
  `OutboxProcessor` + `RoutingPublisher` + transient/poison ayrımı + retry yolundan geçer (Kafka'ya özel kayıp yolu
  yok). Ek kanıt: §4.8.1 **manuel compose broker-outage demo'su**. Kafka-özel otomatik test bilinçli yazılmadı
  (mekanizma ortak; suni dallanma testi K2'ye girerdi) — gizlenmiyor.
- ✅ ADR yazılmış ve mantıklı → `docs/adr/0006-kafka-event-streaming.md` (6 karar: RabbitMQ+Kafka ayrımı, idempotent
  producer, 1:N composite outbox PK, routing publisher, topology/JSON, consumer dedup+offset-after).
- ✅ 400 satır + test coverage hedefleri tutmuş → `check-acceptance.ps1` K1 yeşil; coverage `scripts/coverage.ps1`
  (rakam PR metninde). 306 test, build 0/0.

### 4.8.1 Manuel demo — "Kafka down → outbox kaybetmez" (compose, broker-outage)

> Faz 3 §3.8 RabbitMQ broker-outage demo'sunun Kafka karşılığı; **compose seviyesinde manuel acceptance**.
> Tüm container'lar `cd docker && docker compose up -d --build` ile ayakta. Portlar override'dan
> (order 8080, analytics 8083, kafka-ui 8088, seq 8081).
>
> **Not (mevcut build):** `Order.Confirm()`/`MarkPaid()` HTTP ile tetiklenmez (yalnız domain seam'i —
> integration testlerinde). Bu yüzden HTTP ile üretilebilen tek lifecycle olayı **OrderCreated**'tır →
> Analytics `OrderProjection` **Created** durumunda oluşur. Confirm→Pay→Paid + DailyRevenue akışı 4c-3
> integration testleriyle (gerçek Kafka container) kanıtlıdır. Demo bu yüzden OrderCreated→Kafka→projection
> dayanıklılığını gösterir; tam Paid akışı testlerde.

1. **Baseline (Kafka up):**
   - `POST http://localhost:8080/api/dev/token` (body `{"userId":"<guid>"}`) → JWT al.
   - `POST http://localhost:8080/api/orders` (Bearer) → `orderId`. → OrderService `OrderCreated`'ı Kafka
     outbox'una yazar; OutboxProcessor `order-hub.orders.events`'e publish eder.
   - ~1 sn sonra: OrderHub_Analytics'te projection oluştu mu? (kafka-ui'de topic'te mesaj; veya
     `SELECT * FROM OrderProjections` OrderHub_Analytics DB'sinde; Status = Created).

2. **Kafka down → outbox birikir, RabbitMQ/order akışı ETKİLENMEZ:**
   - `docker compose stop kafka`.
   - `POST /api/orders` (birkaç kez) → istekler **200/201 döner** (OrderService sağlıklı kalır; Kafka
     publish'i ayrı transport, HTTP yolunu bloklamaz).
   - OrderService DB'sinde outbox: yeni Kafka satırları **ProcessedOnUtc = NULL** (publish edilemedi →
     **kaybolmadı**, birikiyor). RabbitMQ transport'u (varsa command event'leri) ayrı → etkilenmez.
   - ★ Acceptance: Kafka down RabbitMQ'yu/order HTTP akışını bozmaz **ve** event'ler outbox'ta korunur.

3. **Kafka up → reconnect → drain → analytics yakalar:**
   - `docker compose start kafka` (healthy olana dek bekle).
   - OutboxProcessor reconnect olur → birikmiş Kafka satırlarını publish eder (ProcessedOnUtc dolar).
   - AnalyticsService consumer tüketir → adım 2'de oluşturulan siparişlerin projection'ları belirir
     (`SELECT COUNT(*) FROM OrderProjections` artar; kafka-ui'de consumer-group lag → 0).

**Tahmini süre:** 10-14 saat

---

# Faz 5 — InventoryService + Saga

**Hedef:** InventoryService ekle, sipariş akışını **Saga pattern** ile orchestrate et. Distributed transaction yok; compensating transaction var.

**Agent öncelik sırası:** `architect` → `messaging-engineer` → `backend-developer` → `test-engineer`

## 5.1 InventoryService

- [ ] Clean Architecture
- [ ] `Product`, `StockItem`, `Reservation` aggregate'leri
- [ ] `ReserveStockCommand`, `ReleaseStockCommand`, `ConfirmStockReservationCommand`
- [ ] Reservation expiry (15 dk, Hangfire ile)

## 5.2 Saga — OrderProcessingSaga

MassTransit state machine (`MassTransit.SagaStateMachine`):

```
OrderCreated → ReserveStockRequested
  ↓ (stock reserved)
StockReserved → ProcessPaymentRequested
  ↓ (payment ok)
PaymentSucceeded → ConfirmStockReservation + ShipOrder
  ↓
Completed

# Compensation paths:
StockReservationFailed → CancelOrder
PaymentFailed → ReleaseStock → CancelOrder
```

- [ ] Saga state'i persisted (EF Core saga repository, kendi DB'si: `OrderHub_Sagas`)
- [ ] Each saga step idempotent, timeout'lar tanımlı
- [ ] Saga compensation: payment fail → stock release event publish

## 5.3 NotificationService

- [ ] Mock servis: console'a "Email sent: ..." log atar
- [ ] Kafka consumer: `order-hub.orders.events` → `OrderConfirmed` → notification log
- [ ] Hangfire ile delayed notification ("cart abandonment" 1 saat sonra)

## 5.4 Testler

- [ ] Saga happy path
- [ ] Saga compensation: stock fail
- [ ] Saga compensation: payment fail after stock reserved → stock release verify
- [ ] Saga timeout: reservation 15 dk dolarsa otomatik cancel

## 5.5 Kabul Kriteri

- ✅ Full happy path: Order create → Stock reserved → Payment ok → Order confirmed → Notification sent
- ✅ Compensation paths çalışıyor (her senaryo test edilmiş)
- ✅ Saga state DB'de görünebiliyor (debug için)
- ✅ Tüm orchestration **idempotent**

**Tahmini süre:** 10-12 saat

---

# Faz 6 — Gateway + Observability + Resilience

**Hedef:** Tek giriş noktası (YARP), end-to-end tracing, circuit breaker, retry, healthcheck dashboard.

**Agent öncelik sırası:** `devops-engineer` → `backend-developer` → `test-engineer`

## 6.1 YARP Gateway

- [ ] `OrderHub.Gateway` projesi
- [ ] Route config: `/api/orders/*` → OrderService, `/api/payments/*` → PaymentService, `/api/analytics/*` → AnalyticsService, vs.
- [ ] JWT validation gateway'de **tek yerden** (downstream'lere claims forward)
- [ ] Rate limiting (ASP.NET Core 8 native)
- [ ] CORS

## 6.2 Polly

- [ ] HttpClient'lara `Microsoft.Extensions.Http.Resilience` pipeline
- [ ] Retry (exponential), circuit breaker, timeout
- [ ] Gateway → downstream çağrılarında uygulanır

## 6.3 OpenTelemetry

- [ ] Tüm servislerde OTel SDK + ASP.NET Core instrumentation + EF Core instrumentation + MassTransit instrumentation + HttpClient instrumentation
- [ ] OTLP exporter → Seq (Seq OTLP destekliyor) veya Jaeger
- [ ] Trace ID Serilog log'larına enrich edilir

## 6.4 Healthcheck dashboard

- [ ] `AspNetCore.HealthChecks.UI` Gateway'de host'lanır
- [ ] Tüm servislerin `/health/ready` endpoint'leri burada görünür

## 6.5 Testler

- [ ] Integration: Gateway → OrderService route doğru
- [ ] Integration: invalid JWT → 401
- [ ] Integration: downstream timeout → Polly circuit breaker açılıyor

## 6.6 Kabul Kriteri

- ✅ Tüm trafik Gateway üzerinden geçiyor
- ✅ Bir trace ID order create'ten payment'a kadar takip edilebiliyor (Seq/Jaeger'da görünür)
- ✅ Downstream çökerse Gateway 503 dönüyor, circuit açılıyor
- ✅ Healthcheck dashboard tüm servisleri yeşil/kırmızı gösteriyor

**Tahmini süre:** 6-8 saat

---

# Faz 7 — Dokümantasyon + CI

**Hedef:** Proje CV'ye konabilir hale gelsin. README profesyonel, mimari diyagram net, CI pipeline çalışıyor.

**Agent öncelik sırası:** `architect` → `devops-engineer`

## 7.1 README.md

İçerik:
- Proje özeti (1 paragraf, mülakat dilinde)
- Architecture diagram (C4 Level 2 — Container)
- Tech stack table
- Quick start: `docker-compose up` → 5 dk içinde çalışır
- Event flow diagram (sequence diagram, Mermaid)
- Saga state diagram (Mermaid)
- Project structure
- Testing
- Decisions (ADR linkleri)

## 7.2 ADR'ler

- [ ] 0001 — RabbitMQ vs Kafka — neden ikisi de
- [ ] 0002 — Outbox pattern adoption
- [ ] 0003 — Database-per-service
- [ ] 0004 — Saga orchestration (orchestration vs choreography seçimi)
- [ ] 0005 — JSON over Avro/Protobuf
- [ ] 0006 — MassTransit seçimi

## 7.3 Postman / Bruno collection

- [ ] Tüm endpoint'ler için collection
- [ ] Auth flow (login → token → kullan)
- [ ] Environment variables

## 7.4 GitHub Actions

- [ ] `.github/workflows/ci.yml`:
  - Trigger: push, pull_request
  - Steps: restore, build, test, coverage report upload
  - Container build (no push — sadece build doğrulama)
- [ ] Build status badge README'de

## 7.5 Kabul Kriteri

- ✅ README'yi okuyan biri 5 dakikada projeyi anlıyor
- ✅ Mimari diyagramlar GitHub'da düzgün render oluyor
- ✅ CI pipeline yeşil
- ✅ Postman collection import edip 5 dakikada full flow test edilebiliyor
- ✅ Tüm ADR'ler yazılı, mantıklı, savunulabilir

**Tahmini süre:** 5-7 saat

---

## Faz Geçiş Protokolü

Bir faz bitince, **sonraki fazı başlatmadan önce**:

1. Tüm kabul kriterleri checklist'i yeşil mi? (manuel doğrula)
2. `dotnet test` yeşil mi?
3. `git status` temiz mi? (commit'lenmemiş değişiklik yok)
4. 400 satır script'i çalıştır: `find src tests -name "*.cs" -not -path "*/bin/*" -not -path "*/obj/*" -not -name "*.Designer.cs" -not -name "Migrations*" | xargs wc -l | awk '$1>400 {print}'` — boş çıkmalı
5. Kullanıcıya faz özeti sun, onayını al
6. Yeni branch aç (`feature/phase<N>-...`)

## Toplam Tahmin

| Faz | Süre |
|-----|------|
| 1 | 6-8 saat |
| 2 | 4-5 saat |
| 3 | 10-14 saat |
| 4 | 10-14 saat |
| 5 | 10-12 saat |
| 6 | 6-8 saat |
| 7 | 5-7 saat |
| **Toplam** | **51-68 saat** |

Part-time (haftada ~6 saat) → ~10 hafta. Full-time (haftada 35-40 saat) → ~2 hafta.
