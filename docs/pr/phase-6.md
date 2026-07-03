Phase 6 — Gateway + Resilience + Observability (#6)

## Summary

Phase 6 turns the six-service mesh into an **operable system**: a single **YARP edge gateway** (central JWT,
per-client rate-limiting, CORS, resilience, health dashboard) and **end-to-end distributed tracing** with
OpenTelemetry across all seven hosts and both brokers. Traffic now enters through **one edge** (`:8000`); the
gateway validates JWT centrally (early 401), applies **per-cluster Polly resilience** (circuit-breaker + timeout +
**idempotent-only retry**) on the forward to downstream, and hosts a **HealthChecks.UI dashboard** that polls every
service. Every hop — HTTP (edge + service), SQL, RabbitMQ (saga), and **Kafka** — emits spans to Seq, with logs
enriched by trace-id for log↔trace correlation.

The gateway stays **pure-edge**: no `ProjectReference` to any service, contract, or building block — it forwards
HTTP (YARP) and polls health (HTTP), nothing more. Two decisions defined the phase's engineering shape: a
**scoped Central Package Management** carve-out to contain the `HealthChecks.UI` dependency-resolver crisis, and
**manual W3C traceparent propagation** across the custom Kafka hop (RabbitMQ is automatic via MassTransit). Both,
plus the retry double-order guard and defense-in-depth JWT, are recorded in **ADR-0008**.

## What's in this phase

**Gateway — the edge (6a)**
- **YARP reverse proxy** routing `/api/orders|payments|analytics/*` to the services + anonymous `/api/dev/token`.
  Stateless (no DB/EF/migration), no `ProjectReference` — a true edge.
- **Central JWT** (same secret as downstream) → invalid/missing token gets a **401 at the gateway**, never reaching
  downstream. ★ Kept **defense-in-depth**: downstream services retain their own JWT validation (gateway bypass ≠
  open service). **Per-client rate-limiting** (fixed-window, partitioned by `sub`/IP → 429 + `Retry-After`) and
  **CORS** (config-driven).
- **HealthChecks.UI dashboard** at `/health-ui` polling all six services' `/health/ready` (config-driven), hosted in
  the gateway.

**Resilience — Polly (6b)**
- `Microsoft.Extensions.Http.Resilience` (8.10.0) attached to YARP's forwarder via a custom
  `ResilientForwarderHttpClientFactory` (`WrapHandler`). Pipeline **per-cluster** (`ResiliencePipelineRegistry`,
  keyed by clusterId → one downstream's open circuit never trips another): **TotalTimeout → CircuitBreaker →
  Retry → AttemptTimeout**.
- ★ **Idempotent-only retry (allowlist).** Only `GET`/`HEAD`/`OPTIONS` retry; `POST`/`PUT`/unknown **never** —
  `POST /api/orders` is non-idempotent, so a retry would create a **double order**. Two guard layers: the allowlist
  (code invariant) and YARP's request-body streaming (a POST body is read once, can't be re-sent).

**Observability — tracing (6c)**
- **`OrderHub.Observability`** building block: `AddObservability(serviceName)` wires ASP.NET Core + HttpClient +
  **SqlClient** (DB spans — ★ EF Core instrumentation is beta, so the **stable** SqlClient one was chosen) +
  MassTransit sources, an OTLP exporter to Seq, and a Serilog **`TraceContextEnricher`** (TraceId/SpanId on every
  log line). The six services reference it; the gateway keeps pure-edge with its own small local wiring (no
  building-block reference; ASP.NET Core + Http + OTLP only).
- ★ **Kafka manual W3C propagation.** RabbitMQ hops propagate trace context automatically (MassTransit); the custom
  Kafka producer/consumer do not, so `KafkaTraceContextPropagation` injects the `traceparent` header on publish and
  extracts it on consume (shared by the producer and **both** consumers — Analytics + Notification). Without it the
  `Order → Kafka → Analytics/Notification` trace would break.

**Health-dashboard completion (6d)**
- The fresh-volume smoke revealed the dashboard showed every service **Unhealthy** — HealthChecks.UI JSON-parses the
  polled response, but `/health/ready` returned plain text `"Healthy"` (`'H' is an invalid start of a value`). Fixed
  by emitting the UI-JSON via `UIResponseWriter.WriteHealthCheckUIResponse` on each service's `/health/ready`
  (`AspNetCore.HealthChecks.UI.Client`). Dashboard now shows all six **green**.

**Test-harness reliability**
- Running `dotnet test <solution>` ran every test assembly in parallel, over-subscribing Testcontainers (six
  integration projects spinning up SQL/RabbitMQ/Kafka at once → different projects flaked on different runs). The
  acceptance runner now executes test projects **sequentially** (product/test code unchanged).

**ADR**
- `docs/adr/0008-gateway-edge-and-observability.md` — 4 decisions: scoped CPM (HealthChecks.UI resolver crisis),
  Kafka manual W3C propagation, idempotent-only retry + POST double-order guard, defense-in-depth JWT.

## Test breakdown

**418 tests green** (Phase 5: 398 → **+20**), build 0 warnings / 0 errors.

| Category | Count | Phase-6 additions |
|---|---:|---|
| Unit | 273 | Observability wiring 3, EventBus Kafka-propagation round-trip +3 |
| Integration (Testcontainers / WebApplicationFactory) | 145 | Gateway 13 (routing, invalid-JWT 401, per-cluster CB fast-fail, attempt-timeout, dashboard-up + config, idempotent retry, POST no-retry), EventBus 1 (real-Kafka trace propagation e2e) |

**Honest test-shape note.** The gateway/resilience tests are deterministic by construction: the circuit-breaker
proof anchors on the **downstream hit-count freezing** once the circuit opens (no timing), and the POST-no-retry
test asserts the downstream is called **exactly once** (the double-order safety invariant). The Kafka propagation
e2e uses a **real Kafka container** + in-memory OTel exporter and asserts the consumer span shares the producer's
TraceId (a broken hop would start a new root — caught). The **full seven-host chain** is proven by the fresh-volume
smoke below, not an automated test (impractical; the riskiest custom hop — Kafka — is the one isolated in code).

## Coverage

**79% line / 66.6% branch** (6884 of 8710 lines, 589 of 884 branches; full suite with Docker integration), run via
`scripts/coverage.ps1` — steady vs Phase 5's 78.5% / 66.2%. Phase 6 adds an edge
gateway and a cross-cutting observability layer — both largely instrumentation/wiring whose value is operational
rather than branch-heavy. The logic that carries risk is well covered: the resilience pipeline (CB open→shield,
timeout bound, retry allowlist including the POST double-order guard), the Kafka propagation (inject/extract
round-trip + real-broker e2e), and the observability wiring (source registration + enricher). The gateway's YARP
forward path and the OTel instrumentation hooks are exercised end-to-end by the fresh-volume smoke.

## Engineering discipline highlights

- **Scoped CPM to contain a dependency-resolver crisis.** `AspNetCore.HealthChecks.UI` (with its `KubernetesClient`
  / `YamlDotNet` / version-less transitive graph) collapsed the whole root Central-Package set — **every** top-level
  package lost its lower bound and resolved to its oldest version (`Yarp.ReverseProxy` → 1.0.0, a vulnerable build).
  Root-caused in a throwaway (healthy alone and with a *small* CPM set; broken only against the ~40-pin root),
  bisected (multi-trigger, no single culprit), and fixed with a **scoped `Directory.Packages.props`** for the gateway
  (and its test project) — NuGet's nearest-props rule, so CPM is preserved, just hierarchically. The gateway is
  already architecturally isolated, so scoping its package set is consistent. OpenTelemetry, by contrast, was
  **verified safe** in the root set (throwaway + locked-mode: EF Core 8.0.11 and Extensions 8.x held, zero 9.x).
- **Defense-in-depth JWT.** Central validation at the gateway (early 401) *and* retained downstream validation, so a
  gateway bypass leaves no service open — not "gateway-only auth + claims forward".
- **Idempotent-only retry with an allowlist, not a denylist.** A denylist ("retry everything except POST") would
  silently retry a new non-idempotent method; the allowlist is a safe default (not listed → no retry). Proven by a
  deterministic WireMock test: POST failure hits the downstream **exactly once**.
- **Per-cluster circuit-breaker, deterministically proven.** State keyed by clusterId (one downstream's open circuit
  doesn't affect others), and the test anchors on the **frozen downstream hit-count** after the circuit opens —
  timing-independent.
- **Kafka manual W3C propagation, proven over a real broker.** RabbitMQ is automatic (MassTransit); the custom Kafka
  hop injects/extracts `traceparent`. The e2e asserts the consumer span joins the producer's trace — the exact
  "half-trace" failure it prevents.
- **Fresh-volume smoke as a real gate — and it caught a bug.** Seven hosts (gateway included) cold-start clean
  (`Restarts=0`) on a wiped volume; the live flow runs **through the `:8000` edge** (token → happy → compensation).
  The smoke surfaced the dashboard-parse bug (all-Unhealthy), which was **fixed within the phase** and re-verified.
- **Sequential test harness.** Parallel `dotnet test <sln>` over-subscribes Testcontainers; the runner now serialises
  test projects — three consecutive clean 418-green runs where parallel runs flaked on rotating projects.
- **Phase 4/5 lessons applied.** Dockerfile `COPY` closures kept in sync with the new `OrderHub.Observability`
  reference; the `--locked-mode` container restore re-verified after every package change; no `9.x` crept into the
  EF Core 8 LTS lock.

## Honest limits / open follow-ups

- **Metrics/Grafana deferred (the headline).** Phase 6 is *tracing only* (decision C). `/metrics` Prometheus +
  Grafana are a later phase; the observability minimum-bar's metrics item is intentionally open, documented in
  ROADMAP §6.3/§6.7.
- **No single end-to-end trace-id across all seven hosts.** The **outbox** publishes asynchronously in a background
  processor, so the original HTTP request's trace is decoupled from the saga/Kafka publishes — one order spans
  several trace-id **segments**, each hop intact within its segment. A single end-to-end trace needs the outbox to
  store and restore the `traceparent` (follow-up, ADR-0008 Karar 2). The automated test isolates the riskiest
  segment (Kafka); the full chain is a manual Seq observation (below).
- **Gateway dev-expose.** In dev the downstream ports (8080–8086) are also published for direct debugging; in prod
  only the edge is exposed (documented boundary).
- **Rate-limit / CORS are dev-loose** by default (prod tightening is config).
- **Carried from Phase 4/5:** the §4.7 consumer-lag volume test, PaymentService API read-side coverage, and the saga
  15-min timeout full-duration e2e remain open.

## What's next

**Phase 7 — Docs + CI + metrics.** README (C4 + sequence + saga diagrams), the remaining ADR index backfill,
GitHub Actions (restore/build/test/coverage), a Postman/Bruno collection — and the deferred **metrics/Grafana**
observability layer.

## Verification commands

```bash
# Build + full test suite (418 tests, needs Docker for Testcontainers)
dotnet build OrderHub.sln
pwsh scripts/check-acceptance.ps1    # K1/K3/LOCK/BUILD/TEST(sequential)/DOCKER — 6/6
pwsh scripts/coverage.ps1

# Fresh-volume smoke: 7 hosts (6 services + GATEWAY) healthy on a clean volume + live edge flow
cp docker/.env.example docker/.env                                   # real dev secrets (K3, gitignored)
cp docker/docker-compose.override.yml.example docker/docker-compose.override.yml
cd docker && docker compose down -v && docker compose up -d --build
docker compose ps    # gateway + 6 services + sqlserver/rabbitmq/kafka/seq → healthy, RestartCount=0

# Live flow THROUGH THE GATEWAY EDGE (:8000), not direct:
#  1. seed stock (SQL): OrderHub_Inventory.StockItems — happy productId qty 100, "insufficient" productId qty 0
#  2. POST :8000/api/dev/token  -> JWT   (anonymous route, gateway -> OrderService)
#  3. POST :8000/api/orders (Bearer, happy productId)        -> saga -> order Status = Shipped
#  4. POST :8000/api/orders (Bearer, insufficient productId) -> StockReservationFailed -> saga release + CancelOrder
#                                                             -> order Status = Cancelled
#  Dashboard: http://localhost:8000/health-ui  -> all 6 services GREEN (UIResponseWriter, 6d)

# ★★ Trace observation in Seq (http://localhost:8081) — the manual proof of hop continuity:
#  - Seq -> filter logs by  OrderId = '<orderId>'  -> read the enriched TraceId(s) (one order = several segments,
#    the outbox boundary; this is expected).
#  - Filter by a TraceId -> the span tree shows the hop's hosts share it. Observed host-sets (one trace-id each):
#       edge:   gateway + orderservice                                   (YARP traceparent, auto)
#       saga:   orderservice + orderprocessingservice + inventoryservice + paymentservice   (RabbitMQ, MassTransit auto)
#       kafka:  orderservice + analyticsservice + notificationservice     (manual W3C propagation, 6c-2)
#    All seven services emit spans to Seq (service.name resource). The gateway dashboard poll also shows as
#    gateway+<service> traces (proving the dashboard actively polls all six).
docker compose down -v
```
