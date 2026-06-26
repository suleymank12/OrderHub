Phase 4 — Kafka event streaming + CQRS read model: AnalyticsService (#4)

## Summary

Phase 4 adds an **event-streaming backbone** alongside the Phase 3 command bus. `OrderService` now
streams its order-lifecycle domain events to **Apache Kafka** (Confluent, KRaft) through the **same
transactional Outbox** that already drives RabbitMQ — a **1:N fan-out** where one domain event maps to
both a RabbitMQ command (when applicable) and a Kafka notification event. A new **`AnalyticsService`**
consumes `order-hub.orders.events` into a **CQRS read model** (`OrderProjection` + `DailyRevenueProjection`)
and exposes read-only query endpoints.

The split is deliberate and is the interview argument (ADR-0006): **RabbitMQ stays command-only**
(point-to-point, "do this work", retry/DLQ), **Kafka owns the event log** (pub-sub, replayable, multiple
independent consumers by offset). The consumer is **at-least-once** (manual offset commit *after* the DB
write) and **idempotent** (event-id dedup reusing the Phase 3 `InboxMessage` entity), so re-delivery never
double-counts revenue. A startup race — analytics booting before the topic exists on a fresh stack — is
handled by a **self-healing consumer** (caught at fresh-volume compose smoke, fixed in 4e).

## What's in this phase

**New service**
- `AnalyticsService` — full Clean Architecture (Domain/Application/Infrastructure/Api), own database
  `OrderHub_Analytics` (database-per-service), **read-only** (no write endpoints; projections are mutated
  solely by the Kafka consumer). Read-side port (`IAnalyticsReadRepository`) keeps Application unaware of
  EF (compiler-enforced Clean Arch).

**Building blocks (EventBus)**
- `IKafkaEvent` / `IRabbitMqEvent` **marker interfaces** + `RoutingIntegrationEventPublisher` — the outbox
  processor routes each event to its transport(s) by marker, no per-event branching in the processor.
- `KafkaIntegrationEventPublisher` — direct Confluent.Kafka producer, **idempotent** (`Acks=All`,
  `EnableIdempotence=true`), keyed by `IKafkaEvent.PartitionKey` (= `OrderId` → per-order ordering).
- **Composite Outbox PK** `(Id, Ordinal)` — lets one domain event fan out to N transport rows while keeping
  the `Id` invariant (dedup unique index) intact; consistent with the inbox composite-PK precedent.

**Producer (OrderService)**
- Order lifecycle events (`OrderCreated`/`OrderConfirmed`/`OrderPaid`/`OrderCancelled`) stream to
  `order-hub.orders.events` via the outbox 1:N fan-out + Kafka producer wiring.

**Consumer + read model (AnalyticsService)**
- `OrderEventsConsumer` (HostedService) — type-header dispatch, lifecycle apply to `OrderProjection`,
  `DailyRevenueProjection` aggregation on `OrderPaid`, **DB-commit → offset-commit** ordering.
- **Idempotency:** event-id dedup via reused `InboxMessage` entity (`MessageId = IIntegrationEvent.Id`,
  `MessageType = CLR FullName`); projection change + dedup stamp in a **single atomic SaveChanges**.
- **Self-heal (4e):** `Consume()` now catches `ConsumeException`, classifies transient startup codes
  (`UnknownTopicOrPart` / `Local_UnknownTopic` / `LeaderNotAvailable` → warn + backoff + continue) vs fatal
  (rethrow → orchestrator restart). The live subscription picks up the topic once it appears.

**Read API**
- `GET /api/analytics/orders/{id}` (order projection), `GET /api/analytics/revenue/daily?from=&to=`
  (daily revenue) — both JWT-protected, read-only.

**Infrastructure / ops**
- docker-compose: `kafka` (Confluent cp-kafka:7.6.1, **KRaft** — Zookeeper-less; same major.minor as the
  Testcontainers fixtures), `kafka-ui` (debug), `analyticsservice` (depends_on sqlserver+kafka healthy).
- No schema registry (JSON; ADR-0006 Karar 5).

**ADR**
- `docs/adr/0006-kafka-event-streaming.md` — 6 decisions: RabbitMQ vs Kafka split, idempotent producer,
  1:N composite outbox PK, routing publisher, topology/JSON/no-schema-registry, consumer dedup + offset-after.

## Test breakdown

**306 tests green** (Phase 3: 274 → **+32**), build 0 warnings / 0 errors.

| Category | Count | Phase-4 delta |
|---|---:|---|
| Unit | 199 | Common 42, OrderService 125, PaymentService 14, Inbox 2, **EventBus 6** (Kafka publisher + routing), **Analytics 10** (read-side queries + validators) |
| Integration (Testcontainers) | 107 | OrderService 90 (**+3** Kafka 1:N fan-out, real Kafka), PaymentService 4, **Analytics 13** |
| ↳ Analytics integration (subset) | 13 | consumer e2e 4 (lifecycle→Paid, dedup→single revenue, multi-order daily aggregate, **topic-missing self-heal**), read-API 7, persistence 2 |

**Honest test-shape note.** Consumer *projection-update* and *idempotency/dedup* logic are proven by **real
Kafka integration** (`DuplicateOrderPaid_DedupPreventsDoubleRevenue`), not mocked unit tests — the consumer
is I/O-bound, so a real-broker test is more meaningful (no artificial unit tests, K2). The ROADMAP §4.7
"100-event consumer-lag" volume test is **not written** (open follow-up); ordering + at-least-once are
already covered by the single-partition lifecycle-order test, the multi-order aggregate test, and the
offset-commit-after assertion.

## Coverage

**80.2% line / 67.1% branch**, full suite with Docker integration included (apples-to-apples with Phase 3's
**79.9% line / 69.6% branch**) — line **up +0.3pp** despite a whole new service and the Kafka layer.

The Phase-4 core is well covered: **EventBus 100%** (Kafka routing + idempotent publisher), **Contracts
100%**, **Outbox 90.7%** (up from 88.9% — composite-PK 1:N fan-out), **Analytics.Application 100%** (read-side
handlers), **Analytics.Api 89.6%**, **OrderService.Application 95.7% / Domain 100%**.

The one low assembly is **Analytics.Infrastructure 62.6%**, and it is **not** a real gap — it is dominated by
code that is boilerplate or infeasible to unit-test:
- `OrderEventsLog` **8.3%** — `[LoggerMessage]` source-generated logging templates (only the methods on
  exercised paths register as covered; the rest are compile-time partials with no logic).
- `AnalyticsDbContextModelSnapshot` **0%** / `…DesignTimeFactory` **0%** — EF auto-generated / design-time-only
  (never runs at runtime; excluded from K1 too).
- `OrderEventsConsumer` **64.2%** — happy path + dedup + self-heal are integration-covered, but the many
  **defensive failure branches** (poison `JsonException`, transient generic-catch + `Seek` retry, fatal
  rethrow, `ProjectionMissing` / `UnknownType` / `MissingTypeHeader` anomalies) are not all triggered.

The actual persistence logic is **100%** (`AnalyticsDbContext`, `AnalyticsReadRepository`, both EF
configurations, both converters, `MigrationExtensions`). **PaymentService.Api remains 0%** — its HTTP
read-side (`GetPaymentById`), untouched in Phase 4, is the same coverage-parity follow-up flagged in Phase 3.

## Engineering discipline highlights

- **1:N fan-out without breaking the dedup invariant.** One domain event must produce multiple transport
  rows, but the outbox dedup unique index keys on `Id`. Solved with a **composite PK `(Id, Ordinal)`** —
  fan-out is expressible, the `Id` invariant and the inbox-symmetry both hold — rather than minting synthetic
  ids (which would have desynced producer/consumer dedup). Documented in ADR-0006 Karar 3.
- **One EventId across three transports.** Dedup keys on `IIntegrationEvent.Id` end-to-end: producer outbox
  unique index → RabbitMQ inbox filter → **Kafka inbox-entity reuse**. The Phase 3 `InboxMessage` entity is
  reused on the consumer side (manual `(eventId, type)` check — the MassTransit `InboxConsumeFilter` is
  RabbitMQ-bound and deliberately *not* used), so the same EventId is the dedup key on all three hops.
- **Idempotent revenue tied to dedup, not status-guard.** A duplicate `OrderPaid` must not double `DailyRevenue`.
  Revenue increment is gated on **first-time dedup** (not on `MarkPaid`'s forward-only status guard, which
  exists for out-of-order consistency) — so offset (no loss) and dedup (no double) are complementary, proven
  by `DuplicateOrderPaid_DedupPreventsDoubleRevenue_AndStampsInboxOnce`.
- **At-least-once = DB-commit then offset-commit.** The consumer commits the offset only *after* the projection
  SaveChanges; a crash in between re-delivers and dedup skips it. Proven behaviorally
  (`…ThenCommitsOffsetAfterProcessing`: a rejoining consumer in the same group sees no unread message).
- **Consumer self-heal — startup race caught by fresh-volume smoke (4e).** On a truly fresh stack the topic
  doesn't exist when analytics boots; the consumer hit `UnknownTopicOrPart`, the unhandled `ConsumeException`
  faulted the `BackgroundService` → host `StopHost` → container `Exited(0)`. Fixed by catching at the
  `Consume()` level and classifying transient-startup vs fatal; the live subscription self-heals when the
  topic appears. Verified end-to-end on a `down -v` stack (analytics stays healthy, `Restarts=0`, order
  create → projection appears). RED/GREEN confirmed: reverting the fix faults `ExecuteTask` (test fails for
  the right reason).
- **Clean Architecture compiler-enforced on the read side.** Application depends only on
  `IAnalyticsReadRepository`; the EF implementation lives in Infrastructure. Application coverage is 100% and
  it has no EF reference.
- **Phase 3 lessons applied proactively.** The new Dockerfile `COPY` lists were built against the service's
  *actual* references up front (no stale-restore failure, the Phase 3 bug), and the fresh-volume compose
  smoke was run *as a gate* — which is exactly what surfaced the 4e self-heal bug before merge.

## What's next

**Phase 5 — InventoryService + Saga.** Add `InventoryService` and orchestrate the order flow with a
MassTransit **Saga state machine** (reserve stock → process payment → confirm/ship, with compensation on
stock/payment failure). A `NotificationService` joins as a second independent Kafka consumer of
`order-hub.orders.events` — exactly the multi-consumer pay-off Kafka was chosen for. Open Phase-4 follow-ups
to fold in: the §4.7 100-event consumer-lag volume test, and PaymentService API read-side tests for coverage
parity.

## Verification commands

```bash
# Build + full test suite (306 tests, needs Docker for Testcontainers)
dotnet build OrderHub.sln
dotnet test OrderHub.sln

# Coverage report (coverlet + ReportGenerator) and phase acceptance gate
pwsh scripts/coverage.ps1            # 80.2% line / 67.1% branch
pwsh scripts/check-acceptance.ps1    # K1/K3/LOCK/BUILD/TEST/DOCKER

# Full stack (fresh volume): 6 services healthy on a clean volume + e2e self-heal
cp docker/.env.example docker/.env            # set real secrets (K3)
cp docker/docker-compose.override.yml.example docker/docker-compose.override.yml
cd docker && docker compose down -v && docker compose up -d --build
docker compose ps   # sqlserver/rabbitmq/kafka/orderservice/paymentservice/analyticsservice + seq + kafka-ui

# §4.8.1 "Kafka down → outbox never loses" demo (manual, compose-level)
# 1) POST /api/dev/token (8080) → JWT;  POST /api/orders → orderId → ~1s → OrderProjection (Status=Created)
docker compose stop kafka     # POST /api/orders still 200/201; outbox Kafka rows ProcessedOnUtc=NULL (kept)
docker compose start kafka    # OutboxProcessor reconnects → drains → analytics consumer catches up (lag→0)
# Kafka UI: http://localhost:8088   Seq: http://localhost:8081   Analytics Swagger: http://localhost:8083/swagger
docker compose down
```
