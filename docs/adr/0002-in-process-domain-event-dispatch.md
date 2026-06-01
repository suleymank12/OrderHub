# ADR-0002: In-Process Domain Event Dispatch

- **Status:** Accepted
- **Tarih:** 2026-06-01
- **Karar verenler:** Süleyman

## Context

Faz 1'de aggregate'ler domain event **raise ediyor** (`Order.Create()` → `OrderCreated`),
ama bu event'leri tüketen kimse yok. `ClearDomainEventsInterceptor`, `SavedChangesAsync`'te
(post-commit) event listelerini yalnızca **temizliyor** — seam/hijyen görevi görüyor, dispatch
etmiyor. Interceptor yorumu bu boşluğu açıkça not ediyor: *"in-process dispatch Faz 2, Outbox Faz 3"*.

Faz 2.2 ise *"`OrderCreatedDomainEventHandler` içinde Hangfire delayed job schedule et"* diyor.
Yani 2.2'nin var saydığı handler ve onu besleyen dispatch mekanizması **henüz yok**. Bu, ROADMAP'in
4 alt-fazına yazılmamış ama 2.2'nin önüne geçen gizli bir **Faz 2.0 ön-koşulu**: domain event'ler
in-process bir handler'a ulaşmadan timeout job'u kurulamaz.

## Decision

`SavedChangesAsync` içinde çalışan bir **`DispatchDomainEventsInterceptor`** ekliyoruz (post-commit).
Akış: aggregate'lerin biriken domain event'leri toplanır → her biri `DomainEventNotification<TDomainEvent>`
(Application'da tanımlı `INotification` köprüsü) ile sarılıp MediatR `IPublisher.Publish` ile yayımlanır
→ **sonra** temizlenir. Domain `INotification`'a bağlanmaz (DIP); adaptasyon wrapper + dispatcher'dadır.
Her domain event bir `INotificationHandler<DomainEventNotification<TDomainEvent>>` ile tüketilir
(ör. `OrderCreatedDomainEventHandler`). `ClearDomainEventsInterceptor`'ın "yalnız temizle" davranışı,
bu interceptor'ın **"publish → sonra clear"** akışına evrilir (clear ≥ dispatch timing'i korunur).

**Gerekçe:**

- **Post-commit:** Handler exception'ı command'i bloke etmez — job schedule hatası order create'i
  durdurmaz. State değişimi (order) iş açısından kritik, yan etki (job) decoupled ve retry-safe kalır.
- **"Hiç çalışmama" riski** (post-commit pencerede crash → event kayıp) Faz 2.2'nin **sweep backstop**'u
  ile karşılanır (bkz. ADR-0003). Dispatcher tek başına teslimat garantisi vermez, vermesi de beklenmez.

## Consequences

- **Olumlu:** Domain event raise → yan etki (Hangfire job schedule) artık **çalışır**; 2.2 mümkün hale gelir.
- **Olumlu:** Faz 3'te Outbox dispatcher bu seam'in **üstüne biner** — handler hem local iş yapar hem
  integration event'i outbox'a yazar. Bugünkü in-process dispatch atılmaz, temel olur.
- **Olumlu:** Order commit (business-critical) ile yan etki (retry-safe) decouple → sorumluluk ayrımı net.
- **Olumsuz / dikkat:** Dispatch transient fail olursa event kaybolur (in-process, garanti yok) — bunu
  Faz 3 Outbox çözer.
- **Olumsuz / dikkat:** Post-commit ile enqueue arası crash → event kayıp; Faz 2.2 **sweep backstop**
  bu pencereyi kapsar (asılı `Pending` order'ları reconcile eder).
- **Evrim yolu (yeni doğan görev):** Faz 3'te **outbox-aware dispatcher** — domain event publish + outbox
  write aynı transaction'da atomik; in-process garanti boşluğu kapanır.

## Alternatives Considered

### Seçenek A: Pre-commit dispatch (`SavingChangesAsync`)

- **Artılar:** Event'ler commit'ten önce işlenir; aynı transaction içinde tutulabilir.
- **Eksiler:** Handler exception'ı SaveChanges'i fail eder → order oluşmaz; yan etki command'i bloke eder
  ("side effect blocks command" anti-pattern), transactional coupling.
- **Karar:** **Reddedildi** — yan etkinin business state'i bloke etmesi kabul edilemez.

### Seçenek B: Handler içinde manuel publish (`await mediator.Publish(...)`)

- **Artılar:** Açık, ekstra interceptor yok.
- **Eksiler:** Her command handler'da boilerplate; unutmaya açık; encapsulation kaybı (raise eden ile
  publish eden ayrışmaz). Aggregate'in raise ettiği event "otomatik" yayılmaz.
- **Karar:** **Reddedildi** — merkezi, unutulamaz dispatch tercih edildi.

### Seçenek C: Outbox pattern'i hemen (Faz 2'de)

- **Artılar:** Teslimat garantisi şimdi gelir.
- **Eksiler:** ROADMAP'te **Faz 3 scope**'u; kapsam genişlemesi, MassTransit/RabbitMQ bağımlılığı erkene çekilir.
- **Karar:** **Reddedildi** — faz sınırı korunur; in-process dispatch zaten Faz 3'ün temeli.

## İlgili

- [CLAUDE.md](../../CLAUDE.md) §9 (Application/Domain ayrımı, pipeline behavior)
- [ROADMAP.md](../../ROADMAP.md) §2.2 (`OrderCreatedDomainEventHandler`), §3.2 (Outbox)
- ADR-0003 (Hangfire storage + timeout reliability — sweep backstop bu ADR'ın garanti boşluğunu kapatır)
- İleride: ADR (Outbox pattern adoption) — Faz 3'te yazılacak
