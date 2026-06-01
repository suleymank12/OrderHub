# ADR-0001: Docker'da Veritabanı Migration Stratejisi

- **Status:** Accepted
- **Tarih:** 2026-06-01
- **Karar verenler:** Süleyman

## Context

İki kural çatışıyor:

1. **CLAUDE.md §9 (prod-safe):** *"Migration manuel çalıştırılır, otomatik `Database.Migrate()` yok."*
   Gerekçesi production güvenliği: otomatik migration prod'da race condition (çok instance aynı anda
   migrate eder), yanlış sürüm uygulanması ve geri alınamaz şema değişikliği riski taşır.
2. **ROADMAP §1.8 (kabul kriteri):** *"`docker-compose up -d` ile tüm stack ayağa kalkıyor"* ve
   *"Swagger'dan order create edebiliyorsun"*.

`orderservice` container'ı boş bir SQL Server'a karşı ayağa kalkar; **şema bir yerden gelmeli**.
Auto-migrate'i tamamen yasaklarsak `docker-compose up` tek komutla çalışan bir stack vermez
(geliştirici elle bir adım atmak zorunda kalır). Tamamen otomatik yaparsak §9'u ihlal ederiz.

## Decision

**Seçenek B:** Startup'ta `MigrateAsync` **yalnızca Development ortamında**, environment guard'ı ile.
Production'da otomatik migration **kapalı** kalır (§9 korunur).

Faz 1.6'da `Program.cs`'e eklenecek niyet (implementasyon `devops-engineer`'da):

```csharp
// SADECE Development: local "docker-compose up" tek komutla çalışsın.
// Production'da migration deliberate bir adımdır (bkz. Consequences → evrim).
if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var context = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    await context.Database.MigrateAsync();
}
```

Compose'daki `orderservice`, `ASPNETCORE_ENVIRONMENT=Development` ile çalışır (local dev/demo stack)
ve `depends_on` ile SQL Server'ın **healthy** olmasını bekler.

## Consequences

- **Olumlu:** `docker-compose up -d` tek komutla çalışan stack verir (§1.8 ✓). §9'un asıl amacı
  (prod güvenliği) korunur — otomatik migration prod'da yok. Integration testlerdeki `MigrateAsync`
  (test fixture'ı, deliberate) ile tutarlı bir model.
- **Olumsuz / dikkat:** Birden fazla `orderservice` instance'ı Development'ta aynı anda kalkarsa
  migration'da yarışabilir. Faz 1'de tek instance var → sorun değil; ölçeklenince Seçenek A bunu çözer.
  Production migration stratejisi şimdilik **tanımsız** (bilinçli) — aşağıdaki evrim yolu ile kapatılır.
- **Evrim yolu (yeni doğan görev):** Faz 7 (CI/CD) geldiğinde Production için **Seçenek A**'ya yükselt:
  `dotnet ef migrations bundle` ile üretilen idempotent migration bundle'ı, deploy pipeline'ında veya
  bir init-container'da çalıştır. App startup migration'dan tamamen arındırılır.

## Alternatives Considered

### Seçenek A: Migration bundle / init-container

- **Artılar:** Prod-grade; migration app yaşam döngüsünden ayrışır; idempotent; CI/CD'ye doğal uyar.
- **Eksiler:** Compose'a ek `migrator` servisi + bundle build adımı → Faz 1 için fazla karmaşık.
- **Karar:** **Ertelendi** → Faz 7'de Production yolu olarak benimsenecek (evrim yolu).

### Seçenek C: Elle migration adımı (`docker compose run ... ef database update`)

- **Artılar:** En explicit kontrol; auto-migration hiç yok.
- **Eksiler:** *"`docker-compose up -d` ile tüm stack"* (§1.8) tek-komut kabul kriterini bozar;
  onboarding'de gizli bir manuel adım yaratır.
- **Karar:** **Reddedildi** — kabul kriteri ve geliştirici deneyimi ile çelişiyor.

## İlgili

- [CLAUDE.md](../../CLAUDE.md) §9 (prod-safe migration), §2 (K3 — secret/güvenlik)
- [ROADMAP.md](../../ROADMAP.md) §1.6 (Docker), §1.8 (kabul kriteri), §7 (CI/CD)
- İleride: ADR (Production migration / CI-CD bundle stratejisi) — Faz 7'de yazılacak
