# ADR-0005: Custom Inbox over MassTransit Built-in (Consumer-Side Dedup)

- **Status:** Accepted
- **Tarih:** 2026-06-02
- **Karar verenler:** Süleyman

## Context

ROADMAP §3.5 her consumer için **idempotent işleme (Inbox pattern)** zorunlu kılıyor: "her consumer mesaj
id'sini bir tabloda tutar, aynı id ikinci kez işlenmez". MassTransit at-least-once teslim eder (retry +
redelivery), bu yüzden consumer-side dedup gerekir. 3c'de aggregate status-guard ile davranışsal idempotency
(zaten Paid → no-op) vardı; inbox bunun ÜSTÜNE consume'a hiç ulaşmadan kesen kalıcı bir savunma ekler.

İki yol var: (a) MassTransit'in built-in EF inbox'u (`AddEntityFrameworkOutbox` → `InboxState`), (b) kendi
inbox building block'umuz. Custom outbox (ADR-0002) zaten kendi pattern'imizi kuruyor.

## Decision

### Karar 1 — Custom inbox (MassTransit built-in DEĞİL)

Kendi **`OrderHub.Inbox`** building block'umuzu kuruyoruz; MassTransit'in `AddEntityFrameworkOutbox`
inbox'unu **kullanmıyoruz**.

- **Gerekçe:** Custom outbox (ADR-0002) ile tutarlılık; iki farklı framework kavramının (bizim outbox + MT
  inbox) yan yana karışmaması; tam kontrol; pattern'i mülakatta savunulabilir biçimde **açıkça gösterme**
  (projenin CV amacı). MT'nin EF outbox'u bizim pre-commit interceptor outbox'umuzla çakışırdı.
- **Reddedilen:** MT built-in — production-grade ama "kendi outbox'unu kurdun ama inbox'u framework'e bıraktın"
  tutarsızlığı + iki outbox mekanizması riski.

### Karar 2 — Dedup anahtarı = `IIntegrationEvent.Id` (MassTransit envelope MessageId DEĞİL)

Inbox dedup, mesajın `IIntegrationEvent.Id`'si üzerinden yapılır; MassTransit'in envelope `MessageId`'si
üzerinden DEĞİL.

- **Gerekçe:** Outbox processor at-least-once republish yaptığında (örn. publish oldu ama `ProcessedOnUtc`
  commit'inden önce crash → tekrar publish) MassTransit her publish'e **yeni** envelope MessageId atar; ama
  bizim `evt.Id` (= kaynak domain EventId, ADR-0002 Karar 4) **sabit** kalır. Uçtan uca dedup ancak sabit
  `evt.Id` ile doğru çalışır.

### Karar 3 — Atomiklik = tek SaveChanges (açık transaction YOK)

Inbox kaydı, consumer'ın iş transaction'ı ile **atomik** yazılır. Consume filter, mesajı tüketmeden önce
`InboxMessage`'ı consume-scope DbContext'ine **tracked olarak Add eder** (commit etmez); consumer →
`ISender` → handler → **TransactionBehavior'ın tek `SaveChanges`'i** inbox + iş state + outbox'u **birlikte**
commit eder. Filter ve handler MassTransit consume-scope'unda **aynı scoped DbContext**'i paylaşır.

- **Gerekçe:** Ayrı transaction / iki SaveChanges → inbox kaydı ile iş state'i ayrı commit olur → arada crash
  → ya çift işleme ya kayıp. Tek SaveChanges = tek transaction = atomik. Açık `BeginTransaction` gereksiz
  (handler zaten tek SaveChanges yapıyor; filter sadece tracked Add ekler).
- **Sonuç:** Handler `SaveChanges` çağırmazsa (örn. NotFound → `Result.Failure` → TransactionBehavior commit
  etmez) inbox satırı da yazılmaz → redelivery tekrar işler (zararsız no-op). Inbox yalnızca **başarıyla
  commit edilmiş** consume'ları deduplike eder — doğru davranış.

### Karar 4 — Varlık = işlendi (ProcessedOnUtc ara durumu YOK)

`InboxMessage` satırının **commit edilmiş varlığı** = "bu (MessageId, ConsumerType) tam olarak işlendi".
Ayrı bir `ProcessedOnUtc` (received-ama-işlenmedi) ara durumu **yoktur**; yalnızca audit için `ReceivedOnUtc`.

- **Gerekçe:** "Consume başında received işaretle, sonunda processed işaretle" iki-aşamalı yaklaşımda
  **retry-redelivery yarışı** doğar: retry (commit'ten önce) received'ı görüp yanlışça skip edebilir.
  Tek-aşamalı (atomik commit = varlık) bu yarışı baştan eler: retry (rollback olmuş) → satır yok → re-run;
  redelivery (commit sonrası) → satır var → skip.

### Karar 5 — Concurrency = check-then-add + unique index (composite PK) backstop

Filter önce var mı kontrol eder (yoksa Add). İki worker aynı mesajı aynı anda alırsa ikisi de "yok" görüp
Add eder → **composite PK (MessageId, ConsumerType)** SaveChanges'te birini reddeder (unique ihlali) → o
consume fault → MassTransit retry → artık satır var → skip. Index = kesin backstop.

- **Composite PK seçimi (ayrı surrogate PK + unique index yerine):** `(MessageId, ConsumerType)` çifti zaten
  doğal kimliktir; composite PK hem benzersizliği hem varlık-sorgusunun (clustered index seek) ihtiyacını
  tek yapıyla karşılar → surrogate kolon YAGNI.

### Karar 6 — Consume filter (transparent)

Dedup, her consumer'a şeffaf bir **MassTransit consume filter** (`InboxConsumeFilter<TConsumer, TMessage>`)
ile uygulanır; consumer base class veya her consumer'da explicit kod DEĞİL. Per-consumer dedup için
consumer-level context kullanılır (`ConsumerType = typeof(TConsumer).Name`) → aynı mesaj farklı consumer'larca
işlenebilir.

## Consequences

- **Olumlu:** §3.5 inbox karşılanır; aggregate-guard + inbox = iki katmanlı idempotency (belt & suspenders).
- **Olumlu:** Atomiklik açık transaction olmadan (tek SaveChanges) → basit, doğru.
- **Olumlu:** Database-per-service: `OrderDbContext` + `PaymentDbContext` ikisi de `IInboxDbContext` implement
  eder, her servis kendi inbox tablosunu tutar.
- **Olumsuz / dikkat:** Inbox = transport-spesifik (MassTransit filter) → `OrderHub.Inbox` MassTransit'e bağlanır
  (outbox transport-agnostik kalmıştı). Inbox doğası gereği consume-pipeline concern'ü olduğundan kabul edilir.
- **Olumsuz / dikkat:** Filter, consumer ile aynı scoped DbContext'i paylaşmalı (3d-2 wiring kritik).

## İlgili

- [ADR-0002](0002-in-process-domain-event-dispatch.md) "Faz 3 Evrim" (custom outbox; EventId dedup zinciri)
- [ADR-0004](0004-masstransit-rabbitmq.md) (MassTransit retry/DLQ — 3d-3 inbox ile birlikte çalışır)
- [ROADMAP.md](../../ROADMAP.md) §3.5 (idempotent consumer / inbox)
- **Not (3d-4):** OutboxProcessor şu an broker-down (transient) ile poison (kalıcı) hatayı AYIRMIYOR
  (publish fail → RetryCount++ → MaxRetryCount'ta kalıcı düşer). §3.8 "broker down → birikir → ayağa
  kalkınca publish" için 3d-4'te processor: deserialize hatası → terminal, publish (broker) hatası →
  transient (terminal sayma) ayrımı yapacak. Bu ADR yalnızca consumer-side inbox'u kapsar.
