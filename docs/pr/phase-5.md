Phase 5 — InventoryService + Saga orchestration + NotificationService (#5)

## Summary

Phase 5 turns the order flow into a **distributed transaction without 2PC**: a central **MassTransit saga**
(`OrderProcessingSaga`, own database `OrderHub_Sagas`) orchestrates **reserve stock → process payment → confirm
→ ship**, with **compensating transactions** on stock- or payment-failure. Two new services join the mesh — a
command-driven **`InventoryService`** (stock reservation with 15-min Hangfire expiry) and a read-side
**`NotificationService`** (mock email + cart-abandonment) — and the existing `OrderService` is rewired: the
Phase-3/4 event-consumers are replaced by **saga command-consumers** (`ConfirmOrder`/`MarkOrderPaid`/`ShipOrder`
/`CancelOrder`).

The saga is **the single authority** for process state (ADR-0007): the flow lives in one visible state machine
(inspectable in `OrderHub_Sagas`) instead of being scattered as choreography across services. Communication is
**command-style over RabbitMQ** (point-to-point, one consumer per command) while the Kafka order-stream keeps
serving independent event consumers — now **two** of them (Analytics + Notification), on **separate consumer
groups**, which is exactly the multi-consumer pay-off Kafka was chosen for. Concurrency is handled the proven
"constraint + retry" way: **optimistic RowVersion** on saga state, not pessimistic locks.

## What's in this phase

**New service — InventoryService** (5b/5c)
- Full Clean Architecture, own DB `OrderHub_Inventory`. `StockItem` aggregate (invariant: reserved ≤ available)
  with `Reserve`/`ConfirmReservation`/`ReleaseReservation`/`ExpireReservation` — all **idempotent** (status
  guards, no double release). Command-driven (no HTTP write API); saga sends `ReserveStock`/`ConfirmStockReservation`
  /`ReleaseStock`, Inventory replies with `StockReserved`/`StockReservationFailed`/`StockReservationConfirmed`/
  `StockReleased` via the transactional outbox.
- **15-min reservation expiry** via Hangfire (ADR-0007 Karar 2): a recurring job expires stale Pending
  reservations → `StockReservationExpired`, which the saga treats as a compensation trigger.

**New service — OrderProcessingService / the saga** (5d/5e)
- `OrderProcessingSaga` (MassTransit state machine) persisted with **`MassTransit.EntityFrameworkCore`** +
  **`ConcurrencyMode.Optimistic`** (RowVersion) + `UseInMemoryOutbox` + retry (ADR-0007 Karar 3). States:
  `AwaitingStockReservation → AwaitingPayment → AwaitingStockConfirmation → Completed`, plus compensation
  `Compensating → Cancelled`. Fan-out/fan-in counted by **ProductId sets** (Karar B) — redelivery-idempotent
  without an inbox table.
- **Compensation (5e):** stock-fail (or expiry) → release the **already-reserved subset** (frozen
  `ProductsToRelease` snapshot) → wait for `StockReleased` fan-in → `CancelOrder`; payment-fail → release **all**
  reservations → `CancelOrder`. New `CancelOrderIntegrationEvent` + OrderService `CancelOrderConsumer` +
  idempotent `Order.Cancel`.

**New service — NotificationService** (5f)
- Read-side over the Kafka order-stream on a **separate consumer group** (`notification-service.order-events`),
  own DB `OrderHub_Notifications`, own monotonic order-status projection (idempotent: inbox dedup + forward-only
  status guard). **Mock email** (`IEmailSender`/`MockEmailSender` — structured log + in-memory recorder, no real
  SMTP, K3). **Hangfire cart-abandonment**: `OrderCreated` → delayed reminder job; on fire it reads its own
  projection and **guards** (Paid/Cancelled → no-op; `ReminderSentUtc` idempotency). `OrderConfirmed` → immediate
  confirmation email. Side-effects run **post-commit** and only on first apply.

**Building blocks / contracts**
- Inventory + Orders + Payments integration events for the saga (`ReserveStock`/`StockReserved`/`…Failed`/
  `…Expired`/`Confirm…`/`Release…`/`StockReleased`, `ConfirmOrder`/`MarkOrderPaid`/`ShipOrder`/`CancelOrder`,
  `ProcessPayment`/`PaymentSucceeded`/`PaymentFailed`) — all `IRabbitMqEvent` (command-style). Kafka order-stream
  events unchanged (reused by the second consumer).

**ADR**
- `docs/adr/0007-saga-orchestration.md` — 5 decisions: saga vs choreography, Hangfire-expiry vs saga-native
  timeout, EF saga repository + optimistic concurrency, command-style over RabbitMQ, the state-machine design.
  MassTransit pinned to **8.4.0** (Karar 3 note: 8.5.9 pulled EF Core 9 which breaks the §3 EF Core 8 LTS lock).

## Test breakdown

**398 tests green** (Phase 4: 306 → **+92**), build 0 warnings / 0 errors.

| Category | Count | Phase-5 additions |
|---|---:|---|
| Unit | 264 | InventoryService 22, OrderProcessingService (saga) 13, NotificationService 8, OrderService +22 (saga command-consumer handlers + CancelOrder + idempotent Cancel) |
| Integration (Testcontainers) | 134 | InventoryService 10 (real RabbitMQ+SQL), OrderProcessingService 8 (RowVersion contention, saga happy e2e, compensation e2e), NotificationService 7 (Kafka consumer, cart-abandonment e2e), OrderService +2 (saga command consumers) |

**Honest test-shape note.** The saga is I/O-bound, so its behaviour is proven where it matters:
- **Saga logic** (fan-out counting, compensation branches, idempotency, late-event Ignore) — MassTransit **InMemory
  harness** unit tests (fast, deterministic).
- **RowVersion optimistic concurrency** — **isolated deterministic** integration test (two DbContexts on one saga
  row → `DbUpdateConcurrencyException`), which proves the exact token MassTransit's Optimistic repo relies on. It
  deliberately does **not** claim to prove MassTransit's retry loop (framework behaviour).
- **Real-infra e2e** (Testcontainers real RabbitMQ + SQL) — happy fan-out reaches `Completed` with persisted
  sets; compensation (payment-fail all-release, **stock-fail partial-release**, empty-release direct-cancel)
  reaches `Cancelled`. Test-doubles stand in for the other services so the *saga* is isolated; the **full real
  chain** (OrderService→Inventory→Payment→saga→Notification) is proven by the fresh-volume compose smoke below.

## Coverage

**78.5% line / 66.2% branch** (550 of 830 branches), full suite with Docker integration included. A small dip
from Phase 4's **80.2% line / 67.1% branch** — expected, and honest: Phase 5 adds **two I/O-heavy services**
(the saga host and the Notification Kafka consumer) whose coverage is dominated by code that is boilerplate or
only meaningful under a real broker:
- **Saga state machine** — the happy transitions and each compensation branch are exercised, but the many
  defensive `Ignore(...)` guards for late/duplicate events in terminal states are not all triggered.
- **`OrderEventsConsumer` (Notification)** — happy apply + dedup are integration-covered; the poison/transient/
  fatal defensive branches (mirrored from the proven Analytics consumer) are not all hit.
- **Hangfire wiring, EF `…ModelSnapshot` / `…DesignTimeFactory`, `[LoggerMessage]` partials** — schema/boilerplate
  or design-time-only (never runs at runtime; excluded from K1 too).

The **logic that matters is well covered**: every service's **Domain ~100%**, saga behaviour proven by InMemory
harness + real-infra e2e, compensation set-math + RowVersion token proven deterministically, and the
NotificationService cart-abandonment guard proven by both unit and a Hangfire-`Succeeded`-strengthened e2e. The
low assemblies are infrastructure defensive-branch coverage, the same shape flagged (and accepted) for
Analytics.Infrastructure in Phase 4.

## Engineering discipline highlights

- **Optimistic saga concurrency, deterministically proven.** Concurrent fan-in writes to one saga row are
  serialised by a SQL `rowversion` token (`ConcurrencyMode.Optimistic` + `UseMessageRetry`), consistent with the
  ADR-0005 "constraint + retry, not pessimistic lock" precedent. Proven by an isolated two-DbContext test, with
  the honest boundary documented (token vs framework retry).
- **Two ADR-design bugs caught before merge.** The ADR-0007 compensation sketch went `StockReservationFailed →
  CancelOrder` directly — which **leaks the already-reserved stock** on a partial reservation. Refined to release
  the reserved subset first (frozen `ProductsToRelease` snapshot) → fan-in → cancel. And an **empty release set**
  (failure before any reservation) would have left the saga **stuck in `Compensating`** waiting for a `StockReleased`
  that never comes — fixed with an `IfElse` that cancels directly. Both are covered by real-infra e2e.
- **Kafka multi-consumer pay-off, live.** Analytics and Notification consume the same `order-hub.orders.events`
  topic on **separate consumer groups** → independent offsets, both see every event. This is the concrete argument
  for Kafka-over-RabbitMQ on the event side (ADR-0006).
- **Cart-abandonment = guard-on-fire, not job cancellation.** No job-id bookkeeping: the delayed reminder fires
  and reads its own projection; Paid/Cancelled → no-op, `ReminderSentUtc` → idempotent. Simpler than tracking +
  cancelling jobs, and race-safe (fire reads current state). Mirrors OrderService's `CancelUnpaidOrderJob`.
- **Post-commit side-effects.** The Notification consumer schedules the reminder / sends the confirmation email
  **after** the projection SaveChanges and only on first apply — so a commit failure never double-sends, and the
  optional scheduler is resolved via `GetService` so the projection-only integration tests stay Hangfire-free.
- **Flaky-free async e2e discipline.** Bounded-wait on **positive signals**, never `Task.Delay`+assert. A
  redelivery e2e that flaked only under full-suite load (a missing `ProcessPayment` wait) was caught and fixed.
  The cart-abandonment **guard** e2e — a negative assertion ("no email") — is made sound by first confirming the
  reminder job reached Hangfire **Succeeded** (positive "it fired" signal), so absence of email proves the guard
  suppressed it, not that the job never ran.
- **EF Core 8 LTS lock defended.** `MassTransit.EntityFrameworkCore` 8.5.x pulls EF Core 9; the whole MassTransit
  line was pinned back to **8.4.0** (EF Core 8.0.x) to keep the §3 stack lock, documented in ADR-0007.
- **Phase 3/4 lessons applied.** Dockerfile `COPY` closures were kept in sync with real csproj references (the
  stale-Dockerfile trap resurfaced once in a sibling service and was fixed at the compose-confirm step), and the
  **fresh-volume compose smoke was run as a gate** — six services cold-start clean (saga migration + RabbitMQ
  bind, Notification Hangfire schema install) with `Restarts=0`.

## Honest limits / open follow-ups

- **Saga 15-min timeout has no full-duration e2e.** The mechanism is complete (Inventory Hangfire expiry →
  `StockReservationExpired` → saga compensation) and unit-proven, but a real 15-minute wait e2e is not written
  (would need time manipulation). ROADMAP §5.4 marked `[~]`.
- **No refund path.** Once an order is `Paid` the saga is forward-only (`AwaitingStockConfirmation`); cancelling a
  Paid order needs a refund, which is out of scope (`Order.Cancel` throws for Paid/Shipped by design).
- **Compensation stragglers rely on expiry.** A stock reservation that completes *after* compensation started is
  left to the 15-min Inventory expiry backstop rather than actively released (KN-2, documented in the saga).
- **Carried from Phase 4:** the §4.7 100-event consumer-lag volume test and PaymentService API read-side coverage
  remain open.

## What's next

**Phase 6 — Gateway + resilience + observability.** YARP gateway, Polly resilience (retry/circuit-breaker/
timeout), OpenTelemetry tracing across the saga hops, and Grafana metrics — turning the six-service mesh into an
operable system.

## Verification commands

```bash
# Build + full test suite (398 tests, needs Docker for Testcontainers)
dotnet build OrderHub.sln
dotnet test OrderHub.sln

# Coverage report + phase acceptance gate
pwsh scripts/coverage.ps1
pwsh scripts/check-acceptance.ps1    # K1/K3/LOCK/BUILD/TEST/DOCKER

# Full stack (fresh volume): 6 services healthy on a clean volume + live e2e
cp docker/.env.example docker/.env            # set real secrets (K3)
cp docker/docker-compose.override.yml.example docker/docker-compose.override.yml
cd docker && docker compose down -v && docker compose up -d --build
docker compose ps   # orderservice/paymentservice/analyticsservice/inventoryservice/orderprocessingservice/
                    # notificationservice + sqlserver/rabbitmq/kafka/seq (all healthy, Restarts=0)

# Live saga e2e (verified in 5g fresh-volume smoke):
#  - seed stock (SQL), POST /api/dev/token → JWT, POST /api/orders
#  - HAPPY (sufficient stock)      → saga → order Status = Shipped
#  - COMPENSATION (partial: one item insufficient) → saga release + CancelOrder → order Status = Cancelled
# Seq: http://localhost:8081   RabbitMQ UI: http://localhost:15672   Kafka UI: http://localhost:8088
docker compose down -v
```
