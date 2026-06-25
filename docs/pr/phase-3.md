Phase 3 — Event-driven payments: Outbox/Inbox over RabbitMQ (#3)

## Summary

Phase 3 turns OrderHub from a single service into an **event-driven, cross-service
payment flow**. `OrderService` and a new `PaymentService` communicate over **RabbitMQ
(MassTransit)** with **command semantics**, made reliable end-to-end by a **transactional
Outbox** (producer-side, atomic with the business write) and a custom **Inbox** (consumer-side
idempotency). The flow survives broker outages (messages accumulate and publish on recovery),
exhausts retries into `_error` dead-letter queues, and never double-processes a message.

Order confirm → `ProcessPayment` (outbox → RabbitMQ) → PaymentService processes → `PaymentSucceeded`/
`PaymentFailed` → OrderService transitions the order to `Paid`/`Cancelled`. Two-layer dedup
(EventId on producer outbox unique index + consumer inbox) makes the whole chain at-least-once safe.

## What's in this phase

**New service**
- `PaymentService` — full Clean Architecture (Domain/Application/Infrastructure/Api), `Payment`
  aggregate, configurable mock payment provider, own database `OrderHub_Payment` (database-per-service).

**Building blocks**
- `OrderHub.EventBus` — `IIntegrationEvent`, `IIntegrationEventPublisher`, MassTransit/RabbitMQ
  wrapper (`AddRabbitMqEventBus`), bus-level **exponential retry policy** + `_error` DLQ.
- `OrderHub.Outbox` — `OutboxMessage`, pre-commit read-only `OutboxInterceptor` (atomic with domain
  state), Type→factory translation registry, polling `OutboxProcessorService` (at-least-once;
  transient-publish vs poison-deserialize split; publish timeout).
- `OrderHub.Inbox` — message-level `InboxConsumeFilter` (transparent consume-pipe dedup, atomic single
  SaveChanges with the handler).
- `OrderHub.Contracts` — shared integration events (`ProcessPayment`, `PaymentSucceeded`, `PaymentFailed`).

**Command flow**
- OrderService: `OrderConfirmed` → `ProcessPaymentIntegrationEvent` (outbox map), `MarkOrderPaid`/
  `FailOrderPayment` commands, `PaymentSucceeded`/`PaymentFailed` consumers (idempotent + aggregate-guard).
- PaymentService: `ProcessPaymentIntegrationEventConsumer`, result events via its own outbox.

**Infrastructure / ops**
- docker-compose wiring: `rabbitmq:3.13-management` + `paymentservice` + `OrderHub_Payment`
  (single SQL instance, two databases), `rabbitmq-diagnostics` healthcheck + `depends_on:
  service_healthy` startup gating, env-injected credentials (K3).

**ADRs**
- ADR-0004 (MassTransit/RabbitMQ + direct-exchange intent + retry/DLQ implementation note).
- ADR-0005 (custom Inbox over MassTransit built-in; message-level dedup decision).
- ADR-0002 "Faz 3 Evrim" Karar 5 (processor transient/poison reliability split).

## Test breakdown

**274 tests green** (Phase 2: 226 → **+48**), build 0 warnings / 0 errors.

| Category | Count | Notes |
|---|---:|---|
| Unit | 183 | Common 42, OrderService 125, PaymentService 14, Inbox 2 |
| Integration (Testcontainers) | 91 | OrderService 87, PaymentService 4 — real SQL + real RabbitMQ |
| ↳ real-broker e2e (subset) | ~5 | transport smoke, payment command e2e, payment-result e2e, retry→`_error` DLQ, broker-outage→recovery |

Coverage (full suite, Docker integration included): **79.9% line / 69.6% branch** (Phase 2: 87.2% line).
The drop is **not** in the new messaging core — EventBus 100%, Contracts 100%, Outbox 88.9%
(`OutboxProcessorService` 97.2%, broker-down paths covered), Inbox 80.7% (`InboxConsumeFilter` 94.7%),
OrderService Application 95.7% / Domain 100%. It is concentrated in **PaymentService's HTTP read
surface** (`PaymentService.Api` 0%, `GetPaymentById` 0%) — a read endpoint outside the messaging flow
that Phase 3 targeted — plus EF auto-generated artifacts (ModelSnapshot / DesignTimeFactory 0%, no
business logic). No artificial tests were added to inflate the number (K2); a PaymentService API test
suite is a clean Phase 4 follow-up if parity is wanted.

## Engineering discipline highlights

- **Outbox broker-down vs poison data-loss bug (found in review, 3d-4a).** The processor handled all
  failures identically (`RetryCount++`), so a ~10s broker outage drove valid messages past the retry
  ceiling → silently dropped (§3.8 violation). Fixed by splitting **deserialize (permanent/poison →
  terminal counter → DLQ)** from **publish (transient/broker-down → no counter, retried next poll)**,
  plus a per-publish **timeout** so a blocking publish can't hang the loop. Transport-agnostic boundary
  kept (broad `catch`, no MassTransit/RabbitMQ types in the building block).
- **Empirically confirmed MassTransit blocks (not throws) when the broker is down** (3d-4b real-broker
  test: first deferral is `TaskCanceledException` = our timeout firing) — proving the publish-timeout
  was necessary, not defensive.
- **Retry-outermost pipe order.** Retry filter is added before the inbox filter so each retry re-runs
  the full consume (inbox included) against a fresh scope; a failed attempt rolls back (no inbox row)
  and retries cleanly, a successful one commits atomically. Proven behaviorally (6 attempts, no committed
  inbox row) and documented in ADR-0004.
- **Custom Inbox, message-level pivot.** MassTransit's open-generic `UseConsumeFilter` only supports
  message-level filters; dedup keys on `IIntegrationEvent.Id` (stable source EventId), not the envelope
  `MessageId` (which changes on republish) — so end-to-end dedup actually holds. Atomic via a single
  SaveChanges shared with the handler (no explicit transaction).
- **Hangfire cold-start race (found via fresh-volume compose, fixed).** On an empty database the Hangfire
  SQL-schema bootstrap ran before EF created the DB → `HangFire.Hash` missing → `RecurringJobRegistrar`
  crashed (exit 139). Fixed by moving the Development `ApplyMigrationsAsync` to immediately after
  `builder.Build()`, before any Hangfire/JobStorage resolution. ValidateOnBuild confirmed (empirically)
  not to materialize the factory-registered storage.
- **Stale Dockerfile restore layers caught.** Both API Dockerfiles' `COPY` lists predated Phase 3 and
  omitted the new building blocks → locked-mode restore failed; fixed so the stack builds.
- **Deterministic real-broker outage test.** Testcontainers re-maps the host port across stop/start
  (observed `:63449→:63456`), which would make MassTransit reconnect to a dead port; pinned a free host
  port so the connection string is stable across restart → flake-free demo.

## What's next

**Phase 4 — Kafka + AnalyticsService.** Stream OrderService domain events to Kafka; AnalyticsService
consumes the stream into a CQRS read model. RabbitMQ stays command-only; Kafka owns the event stream
(ADR-0001 to be written). Optional Phase 4 follow-up: PaymentService API/read-side test suite for
coverage parity.

## Verification commands

```bash
# Build + full test suite (274 tests, needs Docker for Testcontainers)
dotnet build OrderHub.sln
dotnet test OrderHub.sln

# Coverage report (coverlet + ReportGenerator) and phase acceptance gate
pwsh scripts/coverage.ps1
pwsh scripts/check-acceptance.ps1

# Full stack (fresh): all 5 services healthy on a clean volume
cp docker/.env.example docker/.env            # set real secrets (K3)
cp docker/docker-compose.override.yml.example docker/docker-compose.override.yml
cd docker && docker compose up -d --build
docker compose ps                             # sqlserver/rabbitmq/seq/orderservice/paymentservice

# §3.8 broker-outage → recovery demo (manual)
docker compose stop rabbitmq                  # confirm an order → outbox accumulates (RetryCount 0); Seq EventId 3004 PublishDeferred
docker compose start rabbitmq                 # MassTransit reconnects → outbox publishes → order Paid; Seq EventId 3000 Published
# RabbitMQ Management UI: http://localhost:15672  (queues + _error DLQ)   Seq: http://localhost:8081
docker compose down
```
