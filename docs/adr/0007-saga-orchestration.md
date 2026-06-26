# ADR-0007: Saga Orchestration for Distributed Order Processing

- **Status:** Accepted
- **Tarih:** 2026-06-26
- **Karar verenler:** Süleyman

## Context

Faz 5 (ROADMAP §5), sipariş akışını uçtan uca koordine etmeyi ister: **stok rezervasyonu → ödeme →
onay/sevkiyat**, ve başarısızlıkta **compensation** (telafi). Distributed transaction (2PC) yok — yerine
saga ile **compensating transaction**. Bu akış üç servisi kapsar: yeni **InventoryService** (§5.1), mevcut
**PaymentService** (Faz 3) ve mevcut **OrderService**.

Mevcut zemin (Faz 3) Order↔Payment'ı **choreography** ile kurmuştu: `Order.Confirm()` → `OrderConfirmed`
domain event → outbox → `ProcessPayment` (RabbitMQ) → PaymentService → `PaymentSucceeded`/`PaymentFailed`
→ OrderService consumer'ları → `MarkOrderPaid`/`FailOrderPayment` command'leri.

★ **Kritik bulgu (durum tespiti, 2026-06-26):** Bu choreography **canlı yolda DORMANT**. `OrdersController`
yalnız `POST /api/orders` (Create) içerir — **Confirm/Pay için HTTP endpoint'i YOKTUR**. `Order.Confirm()`
sadece domain seam'idir (integration testlerinde tetiklenir). Yani üretimde `OrderConfirmed` HTTP ile hiç
raise edilmez → tüm payment choreography sadece 3c integration testlerinde çalışır. HTTP'den tetiklenen tek
lifecycle = `OrderCreated` (Kafka → Analytics). **Sonuç:** Saga, *canlı bir akışı değiştirmiyor* — **var
olmayan orchestration'ı dolduruyor**.

## Decision

### Karar 1 — Orchestration (Saga, tek otorite) vs Choreography → **Saga**

Sipariş akışını **merkezi bir saga** orchestrate eder (MassTransit state machine). Saga **tek otoritedir**:
süreç durumunu (`OrderProcessingSaga`) o tutar, adımları o tetikler.

- **Neden orchestration:** Üç servisli, telafili, sıralı bir akışta choreography "kim neyi biliyor?"u
  dağıtır → süreç durumu hiçbir yerde bütün olarak görünmez, compensation dalları her serviste yeniden
  kurulur. Saga süreci **tek yerde, görünür** (saga state DB'sinde) tutar; compensation dalları **tek
  state machine'de** tanımlıdır. ROADMAP §5.5 "saga state DB'de görünebiliyor" kabul kriterini doğrudan
  karşılar.
- **Choreography (B) reddi — split-brain:** Mevcut choreography'yi koruyup saga'yı "saran" bir katman
  eklemek **çift-yönetim** doğurur: hem saga hem OrderService `PaymentSucceededConsumer` `PaymentSucceeded`'ı
  tüketirse ikisi de order'ı paid'e geçirmeye çalışır. Aggregate status-guard ikinci'yi no-op yapar (çökmez)
  **ama** "order paid mi?" otoritesi ikiye bölünür (saga state vs order aggregate) → tutarsızlık kaynağı.
  Reddedildi.
- **Düşük yıkım (dormant choreography sayesinde):** Choreography canlı yolda çalışmadığından, A'ya geçiş
  *canlı bir akışı silmez*. Kaldırılacak tek şey iki **thin adapter** consumer'dır
  (`PaymentSucceeded/FailedIntegrationEventConsumer`, 5d'de kaldırılır → saga onları tüketir).
- **Domain handler'lar command-consumer ile REUSE (C):** Saga ayrı host'tur; OrderService aggregate'ine
  `ISender` ile doğrudan giremez. Bu yüzden saga, OrderService'e **command** gönderir (`MarkOrderPaid`,
  `FailOrderPayment`, `ConfirmOrder`, `ShipOrder`); OrderService bu command'leri **command-consumer**
  (5d'de eklenir) ile alıp **mevcut MediatR handler + aggregate mantığını AYNEN** çalıştırır. Mevcut
  `MarkOrderPaidCommand`/`FailOrderPaymentCommand` + handler + validator + aggregate davranışı **değişmez**;
  yalnız tetikleyici *event* yerine *saga-command* olur. **PaymentService sıfır değişim** (saga da aynı
  `ProcessPayment` command'ini gönderir). `OrderConfirmed → ProcessPayment` outbox map'i kalkar; ödemeyi
  artık saga başlatır.
- **Test etkisi:** 3c choreography consumer testleri → saga testlerine evrilir (silinmez, taşınır); domain
  handler testleri **olduğu gibi** kalır. Coverage düşmez.

### Karar 2 — Saga timeout scheduler = mevcut **Hangfire** (delayed-exchange reddi)

Saga reservation timeout'u (15 dk, §5.1) için **yeni broker altyapısı eklenmez**:

- **Reservation expiry = InventoryService Hangfire-job → event:** Stok rezervasyonu 15 dk içinde
  confirm/release edilmezse, **InventoryService'in kendi Hangfire job'u** (SQL storage, zaten stack'te)
  rezervasyonu serbest bırakır ve `StockReservationExpired` event'i publish eder; **saga bunu normal event
  olarak tüketir** ve compensation'a girer. Bu, OrderService'in `CancelUnpaidOrderJob` precedent'iyle birebir
  aynı pattern (Faz 2, ADR-0003 hibrit) — kanıtlı, durable.
- **RabbitMQ delayed-message-exchange reddi:** MassTransit-native `UseDelayedMessageScheduler` daha
  idiomatik olurdu **ama** broker'a `rabbitmq-delayed-message-exchange` **plugin'i** gerektirir → rabbitmq
  image'ı değişir (custom image / plugin enable) = **compose-only infra riski**. Faz 3 (Hangfire cold-start
  exit 139) ve Faz 4 (consumer topic-yok exit 0) **compose-only bug precedent'leri** bu riski somut kıldı;
  fresh-volume smoke'ta "plugin eksik" sınıfı bir bug istemiyoruz. Reddedildi.
- **Saga-native timeout (MassTransit `Schedule`) — ŞİMDİLİK YOK (YAGNI):** Reservation expiry yukarıdaki
  job→event ile çözüldüğünden, saga-içi scheduler (`MassTransit.Hangfire`) **bu adımda eklenmez** ve CPM'e
  pin'lenmez. 5d'de saga-native bir timeout *gerçekten* gerekirse (ör. payment-yanıt-yok süresi), o zaman
  **`MassTransit.Hangfire`** eklenir (mevcut Hangfire+SQL'i reuse — yine delayed-exchange değil).

### Karar 3 — Saga persistence = **MassTransit.EntityFrameworkCore** + optimistic concurrency

- **EF Core saga repository:** Saga state, **`MassTransit.EntityFrameworkCore`** ile kendi DB'sine
  (`OrderHub_Sagas`, **database-per-service** — ADR Faz 3 deseni) persist edilir. CPM'e `8.5.9` pinlendi
  (MassTransit alt-paketleri lockstep → core ile aynı sürüm).
- **Optimistic concurrency + RowVersion:** Saga instance'ına eşzamanlı mesajlar vurabilir (ör. `StockReserved`
  ile bir timeout neredeyse aynı anda). MassTransit EF repository iki mod sunar: *pessimistic* (row lock) ve
  *optimistic* (SQL Server `[Timestamp] byte[] RowVersion`). **Optimistic + RowVersion + retry** seçilir —
  yarışta ikinci yazma reddedilir → MassTransit retry → güncel state görülür. Bu, **ADR-0005 inbox
  composite-PK "retry-on-contention"** felsefesiyle aynı (concurrency backstop = DB constraint + retry,
  pessimistic lock değil). Kesin migration + mapping 5d'de.

### Karar 4 — Saga ↔ Inventory **command-style** (IRabbitMqEvent, RabbitMQ)

Saga↔Inventory ve saga↔Order/Payment iletişimi **command/point-to-point** → mevcut **`IRabbitMqEvent`**
marker'ı (ADR-0006 Karar 1: command = RabbitMQ). Yeni Contracts event seti (5b/5c'de eklenir):

| Yön | Mesaj | Tür |
|---|---|---|
| Saga → Inventory | `ReserveStockIntegrationEvent` | command |
| Saga → Inventory | `ConfirmStockReservationIntegrationEvent` | command |
| Saga → Inventory | `ReleaseStockIntegrationEvent` | command (compensation) |
| Inventory → Saga | `StockReservedIntegrationEvent` | event |
| Inventory → Saga | `StockReservationFailedIntegrationEvent` | event |
| Inventory → Saga | `StockReservationExpiredIntegrationEvent` | event (Hangfire-job, Karar 2) |
| Inventory → Saga | `StockReleasedIntegrationEvent` | event (compensation ack) |

Hepsi `OrderHub.Contracts/Inventory/` altında (mevcut `Orders/`+`Payments/` düzeni). **Kafka order-stream
(`order-hub.orders.events`) bu sagaların işi DEĞİL** — o, NotificationService'in (§5.3) bağımsız ikinci
consumer'ının işidir (Kafka'nın çok-tüketici gerekçesinin canlı kanıtı).

### Karar 5 — Saga state machine (TASARIM taslağı — kod değil; 5d implement eder)

`OrderProcessingSaga` (MassTransit `MassTransitStateMachine`). **Correlation:** saga instance `OrderId` ile
correlate edilir (`CorrelationId = OrderId` — order başına tek saga; tüm akış event'leri OrderId taşır).
**Idempotency:** her transition state-guard'lıdır — beklenmeyen state'te gelen event yok sayılır/loglanır
(event re-delivery'de çift ilerleme olmaz).

**Happy path:**

```
                OrderCreated
   [Initial] ───────────────▶ (ReserveStock command gönder)
        │
        ▼
[AwaitingStockReservation] ──StockReserved──▶ (ProcessPayment command gönder)
        │
        ▼
   [AwaitingPayment] ──PaymentSucceeded──▶ (ConfirmStockReservation + ConfirmOrder + ShipOrder command)
        │
        ▼
[AwaitingStockConfirmation] ──StockReservationConfirmed──▶ [Completed] (Finalize)
```

**Compensation dalları:**

```
[AwaitingStockReservation] ──StockReservationFailed──▶ (FailOrderPayment/CancelOrder command) ──▶ [Cancelled]

[AwaitingStockReservation] ──StockReservationExpired (timeout 15dk)──▶ (CancelOrder) ──▶ [Cancelled]

[AwaitingPayment] ──PaymentFailed──▶ (ReleaseStock command) ──▶ [Compensating]
        │
        ▼
   [Compensating] ──StockReleased──▶ (CancelOrder command) ──▶ [Cancelled]
```

**State'ler (5):** `Initial` (örtük) · `AwaitingStockReservation` · `AwaitingPayment` ·
`AwaitingStockConfirmation` · `Compensating` · final: `Completed` / `Cancelled`.

**Event'ler (`Event<T>`):** `OrderCreated` (Initial; correlate-by OrderId) · `StockReserved` ·
`StockReservationFailed` · `StockReservationExpired` · `PaymentSucceeded` · `PaymentFailed` ·
`StockReleased` · `StockReservationConfirmed`.

**Gönderilen command'ler (saga `Send`):** `ReserveStock`, `ProcessPayment`, `ConfirmStockReservation`,
`ReleaseStock` (Inventory/Payment) · `ConfirmOrder`, `ShipOrder`, `MarkOrderPaid`, `FailOrderPayment`/
`CancelOrder` (OrderService command-consumer'ları, Karar 1-C).

**Timeout:** `AwaitingStockReservation`/`AwaitingPayment`'ta reservation 15dk — Karar 2 gereği
InventoryService Hangfire-job'undan gelen `StockReservationExpired` event'iyle modellenir (saga-native
`Schedule` değil).

> Bu yalnız **tasarım**tır: `MassTransitStateMachine`, `State`, `Event<T>`, `Initially/During/When`,
> `.TransitionTo`, EF saga map + migration → **5d**'de yazılır. 5a yalnız ADR + CPM pin'i sağlar.

## Consequences

- **(+)** Süreç durumu tek yerde görünür ve test edilebilir (§5.5 kabul kriteri). Compensation dalları tek
  state machine'de — dağılmaz.
- **(+)** Mevcut domain handler'lar + PaymentService korunur (Karar 1-C reuse). Yeni broker altyapısı yok
  (Karar 2). Concurrency, kanıtlı "constraint + retry" felsefesiyle (Karar 3, ADR-0005 ile tutarlı).
- **(−)** OrderService'e command-consumer eklenir + iki event-consumer kaldırılır (5d transport rewire'ı,
  orta iş). Saga için yeni servis/host + yeni DB (`OrderHub_Sagas`) + EF migration.
- **(−)** Optimistic concurrency, yarışta retry maliyeti getirir (kabul: pessimistic lock'tan daha ölçeklenir,
  ADR-0005 precedent'i).
- **Sonraki adımlar:** 5b InventoryService skeleton · 5c Inventory command'leri + Hangfire expiry · 5d saga
  state machine + EF persistence + OrderService transport rewire · 5e compensation + timeout testleri ·
  5f NotificationService · 5g e2e + fresh-volume compose smoke.
