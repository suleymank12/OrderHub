# OrderHub

> **.NET 8 event-driven e-commerce microservices** — Clean Architecture + CQRS/MediatR temeli üzerine,
> ilerleyen fazlarda Outbox · RabbitMQ · Kafka · Hangfire · YARP ile genişletilen örnek (showcase) proje.
> Amaç: her satırı mülakatta savunulabilir, test edilebilir ve gözlemlenebilir bir mikroservis kod tabanı.

**Status:** 🚧 Phase 1 — Foundation (in progress)

---

## Tech Stack

- **Runtime / API:** .NET 8 (LTS), ASP.NET Core Web API
- **Persistence:** EF Core 8 + SQL Server 2022
- **Patterns:** Clean Architecture, CQRS (MediatR), FluentValidation, Result pattern
- **Observability:** Serilog → Seq, health checks (`/health/live`, `/health/ready`)
- **Testing:** xUnit · FluentAssertions · Moq · AutoFixture · Testcontainers
- **Container:** Docker + Docker Compose (v2)

> Tam stack ve mimari derinlik (event flow, ADR'ler, C4 diyagramları) Faz 7 README'sinde gelecek.
> Bu dosya **onboarding-minimal**: amacı repo'yu klonlayan birinin stack'i tek seferde ayağa kaldırması.

---

## Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (Compose v2 dahil)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) — yalnızca lokal build/test için
  (sadece `docker compose up` çalıştıracaksan SDK'ya gerek yok)
- PowerShell 7+ (`pwsh`) — coverage ve kontrol script'leri için (cross-platform)

---

## Quick Start

```bash
# 1) Repo'yu klonla
git clone <repo-url> OrderHub && cd OrderHub

# 2) Secret şablonunu kopyala ve gerçek değerleri gir (bu dosya gitignored)
cp docker/.env.example docker/.env
#    docker/.env içindeki SQL_SA_PASSWORD ve JWT_SECRET değerlerini DEĞİŞTİR.

# 3) Local port mapping şablonunu kopyala (bu dosya da gitignored)
cp docker/docker-compose.override.yml.example docker/docker-compose.override.yml

# 4) Stack'i ayağa kaldır (SQL Server + Seq + OrderService API)
cd docker && docker compose up -d
```

Ana `docker-compose.yml`'de bilinçli olarak **published port yoktur** (servisler yalnızca internal
ağda konuşur); host erişimi 3. adımdaki override ile açılır.

| Servis | URL |
|--------|-----|
| **Swagger UI** | http://localhost:8080/swagger |
| **Seq (loglar)** | http://localhost:8081 |
| **Health (live/ready)** | http://localhost:8080/health/live · `/health/ready` |

**Token al** (yalnızca Development) — korumalı endpoint'leri Swagger'dan denemek için:

```bash
curl -X POST http://localhost:8080/api/dev/token \
     -H "Content-Type: application/json" -d "{}"
# Dönen JWT'yi Swagger'daki "Authorize" kutusuna yapıştır.
```

---

## Development Workflow

```powershell
dotnet build OrderHub.sln           # Solution'ı derle (0 warning hedefi)
dotnet test  OrderHub.sln           # Tüm testleri çalıştır

.\scripts\coverage.ps1              # Coverage topla + HTML rapor (coverage/html/index.html)
.\scripts\check-acceptance.ps1      # Faz geçiş kalite kapısı (K1/K3/lock/build/test/docker)
```

- `coverage.ps1` rapor üretir (eşik zorlamaz); rapor `coverage/` altında ve **gitignored**.
- `check-acceptance.ps1` faz geçiş kontrollerini koşar, hepsi geçerse exit `0` döner;
  CI bunu tek satırla çağırır: `pwsh -File scripts/check-acceptance.ps1`.

---

## Architecture

Clean Architecture bağımlılık yönü: `Api → Application → Domain` ve `Infrastructure → Application + Domain`.
Domain hiçbir katmana bağlı değildir.

- **Mimari kararlar:** [`docs/adr/`](docs/adr/) — neden bu teknoloji/desen seçildi (ADR formatı).
- **Faz planı:** [`ROADMAP.md`](ROADMAP.md) — fazlar, kapsam ve kabul kriterleri.
- **Proje kuralları:** [`CLAUDE.md`](CLAUDE.md) — kodlama standartları ve ihlal edilemez kurallar.
