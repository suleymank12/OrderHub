Phase 7 — Documentation + CI (#7)

## Summary

Phase 7 makes OrderHub **CV/interview-ready**: it does not add product features — it adds the layer that
lets someone *understand and defend* the system in five minutes. A professional **README** (overview, real
7-host topology, tech-stack, honest `:8000` quick-start), **three GitHub-rendered Mermaid diagrams** (C4-style
container, event-flow sequence, saga state — all reflecting the *real* topology, no invention), a **two-job
GitHub Actions pipeline** (fast feedback + full Testcontainers gate) that **reuses the existing local quality
gate** rather than duplicating it, a **Postman collection** with an honest flow boundary, and the **ADR set
completed and indexed** (0001–0009). No `.cs` changed; the 418-test suite and its coverage are untouched.

The phase's engineering shape came from three honesty calls: (1) the committed compose override template was
**Phase 5-era** and never exposed the gateway, so a literal "copy → hit `:8000`" quick-start was *false* — the
template was refreshed to match reality; (2) the diagrams and the Postman collection **omit any `Confirm`/`Pay`
HTTP endpoint** because none exists (saga-driven, ADR-0007) — no fabricated requests; (3) CI's real green is
only observable **after push**, so nothing here claims "CI is green" — it claims "CI is written and validated
as far as is possible locally."

## What's in this phase

**README overhaul (7a-1)**
- Full rewrite from the stale "Phase 1 in progress" stub to the real Phase 1–6 system: overview in interview
  language, a 7-host topology table (HTTP/DB/RabbitMQ/Kafka/role per host), a reality-checked tech-stack table
  (Redis **omitted** — it is in the locked stack but not yet deployed; documented under Future Work), an honest
  `:8000` quick-start (real `CreateOrderRequest` body), the real project tree (OrderProcessingService shown with
  only Api·Infrastructure — no domain aggregate), testing, and roadmap.
- **★ HTTP domain-seam note:** the README states plainly that there is no `POST /confirm` or `/pay` — those
  transitions are saga-command-driven (ADR-0007).

**Mermaid diagrams (7a-2)**
- Three diagrams into the README's Architecture placeholders: **C4 container** (`flowchart` — Mermaid's native
  C4 is experimental on GitHub, so the render-safe form was chosen), **event-flow** (`sequenceDiagram`), and
  **saga state** (`stateDiagram-v2`). All three were **rendered to SVG with the real Mermaid engine** (mmdc +
  headless Chromium — the same renderer GitHub uses); a semicolon-as-statement-separator bug in a sequence Note
  was caught and fixed there.

**Compose override template fix (7a-1)**
- `docker-compose.override.yml.example` refreshed with `gateway :8000` + `orderprocessing :8085` +
  `notification :8086` (was Phase 5-era, missing the edge). Verified: base + example override + `.env.example`
  merges to a valid config that **publishes `:8000`** — the quick-start now honestly works.

**GitHub Actions CI (7b)**
- `.github/workflows/ci.yml` — two jobs: **fast** (every push + PR: build, unit tests, lint K1/K3/LOCK, compose
  syntax; no Testcontainers → fast feedback) and **integration** (PR + main push: full sequential Testcontainers
  suite + coverage + container build, no push).
- ★ **DRY, single source of truth:** both jobs call `scripts/check-acceptance.ps1` — the *same* gate developers
  run locally — parameterised with new `-TestScope {All,Unit,None}` and `-Coverage` switches (backward
  compatible: no args = old behaviour). No copy-pasted check logic to drift. "Green locally, different in CI"
  is structurally impossible.

**Postman collection (7c)**
- `postman/OrderHub.postman_collection.json` + environment: all real endpoints through the `:8000` edge, auth
  flow (Dev Token → `{{token}}` → Bearer), env vars (baseUrl/token/orderId/paymentId), test scripts that capture
  `token` and `orderId`. **★ Honest flow boundary:** no `Confirm`/`Pay` request (none exists); the collection
  description spells out the triggerable path (token → create → get → analytics projection).

**ADR completion (7a-1 + 7c)**
- ADR index backfilled from 2 rows (0001, 0008) to the full set, and **ADR-0009 (database-per-service)** written
  — the one foundational decision referenced across ADRs but never recorded on its own. Index now lists **0001–0009**.

## Test breakdown

**418 tests green** (unchanged — Phase 7 adds no product code and no tests), build 0 warnings / 0 errors. The
`check-acceptance.ps1` change is additive (new optional params) and **backward compatible**; the parameterless
gate still runs the identical 6 checks (K1/K3/LOCK/BUILD/TEST/DOCKER) and passes 6/6.

## Coverage

Unchanged from Phase 6 — **79% line / 66.6% branch** (no `.cs` touched). CI's `integration` job reproduces this
via `check-acceptance.ps1 -Coverage` (test + coverage in one pass — no double test run) and uploads the report
as an artifact.

## Engineering discipline highlights

- **Honest quick-start over a convenient lie.** The committed override template didn't expose the gateway; rather
  than write a `:8000` quick-start that silently fails, the template was fixed and the merged config verified to
  publish `:8000` — proof, not assertion.
- **Diagrams verified by rendering, not by eyeballing.** All three Mermaid blocks were rendered with the real
  engine; the sequence diagram's parse error (a `;` in a Note) was found and fixed before shipping.
- **CI reuses the local gate.** The single biggest CI design choice was *not* to re-implement K1/K3/build/test in
  YAML but to parameterise the one script both sides already trust — DRY across the local/CI boundary.
- **No fabricated API surface.** Neither the diagrams, the README, nor the Postman collection invent a
  `Confirm`/`Pay` endpoint — the saga-driven seam is documented as-is (ADR-0007).
- **ADR set closed honestly.** Every significant decision is now recorded and indexed (0001–0009), including the
  previously-implicit database-per-service.

## Honest limits / open follow-ups

- **★ CI's real green is only visible after push.** `ci.yml` is written and validated as far as possible locally
  (YAML valid; job structure correct; the `-TestScope Unit` filter verified to select exactly the 10 unit / exclude
  the 8 Testcontainers projects; script parses, stays ≤400 lines, 6/6 locally). The **actual** green appears in the
  PR #7 run on GitHub — this document does **not** claim it is green yet.
- **Metrics/Grafana still deferred (the headline open item).** Tracing is done (OpenTelemetry → Seq); `/metrics`
  Prometheus + Grafana remain a future iteration — architecture ready (OTel SDK installed), documented in README
  Roadmap and ROADMAP §6.7.
- **Single end-to-end trace-id** across all seven hosts is not yet in place (outbox async boundary segments the
  trace; follow-up per ADR-0008).
- **Postman "full flow" is bounded by the domain seam** — HTTP can only trigger order creation; the saga drives
  the rest. `paymentId` must be read from PaymentService/Seq (no HTTP payment trigger).
- **Server-side pricing** and **Redis caching** remain planned (in the locked stack, not yet wired).
- **Bruno note:** the collection is Postman v2.1 JSON (verifiable, interview-standard); Bruno/Insomnia users can
  import it directly.

## What's next

Beyond Phase 7's scope, the roadmap's next iterations are the deferred **metrics/Grafana** observability layer,
**server-side pricing** (remove client-supplied `UnitPrice`), and **Redis caching** for read-heavy projections.

## Verification commands

```bash
# Quality gate (local) — identical checks CI runs; 6/6 expected
pwsh scripts/check-acceptance.ps1          # default: All scope, no coverage (backward compatible)
pwsh scripts/check-acceptance.ps1 -TestScope Unit    # what CI's 'fast' job runs
pwsh scripts/check-acceptance.ps1 -Coverage          # what CI's 'integration' job runs (test + coverage, one pass)

# Docs / collection validation
python -c "import json; json.load(open('postman/OrderHub.postman_collection.json')); json.load(open('postman/OrderHub.postman_environment.json')); print('collection + env: valid JSON')"

# Diagrams (optional) — render the README's Mermaid blocks with the real engine
#   npx -y @mermaid-js/mermaid-cli -i <extracted>.mmd -o out.svg

# CI: real green is observed in the PR #7 Actions run on GitHub (not asserted here).
```
