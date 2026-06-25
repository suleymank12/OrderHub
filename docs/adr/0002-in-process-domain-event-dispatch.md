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

## Faz 3 Evrim — Transactional Outbox Entegrasyonu

- **Status (bu bölüm):** Accepted — 2026-06-01
- **Kapsam:** Bu ADR'nin Faz 3 uzantısı. **Yeni ADR açılmadı** — ADR-0002 zaten *"Faz 3'te Outbox
  dispatcher bu seam'in üstüne biner"* sözünü vermişti; bu bölüm o sözü somut kararlara bağlar.
  MassTransit/RabbitMQ taşıma katmanı kararları ayrı: [ADR-0004](0004-masstransit-rabbitmq.md).

Faz 3'te in-process dispatch **kaldırılmaz**; transactional outbox onun **yanına**, SaveChanges'in
**farklı bir aşamasına** eklenir. İki mekanizma rakip değil, katmanlıdır.

### Karar 1 — Outbox interceptor PRE-COMMIT (`SavingChangesAsync`)

`OutboxInterceptor`, `SaveChangesInterceptor.SavingChangesAsync` override eder → EF'in INSERT/UPDATE'i
göndermesinden **hemen önce**, henüz commit edilmemiş ChangeTracker üzerinde çalışır. Ürettiği
`OutboxMessage` satırları **domain state ile aynı SQL transaction'da** commit edilir.

- **Gerekçe:** Atomiklik commit sınırından gelir. "DB değişti ama mesaj yazılmadı" ve "mesaj yazıldı
  ama DB rollback oldu" pencerelerinin **ikisi de** kapanır. Outbox'ın tek varlık sebebi budur.
- **Reddedilen:** Post-commit ayrı `SaveChanges` ile outbox yazımı → tam da outbox'ın çözdüğü dual-write
  boşluğunu geri açar; outbox'ı anlamsızlaştırır.
- **Faz 2 ile tutarlılık:** Bu, in-process dispatcher'ın bilinçli **post-commit** seçiminin (yan etki
  business state'i bloke etmesin) zıttı değil **tamamlayıcısıdır**. Ayrım niyet ayrımıdır: integration
  mesajı *business state'in kendisidir* (kaybı kabul edilemez → state ile atomik); Hangfire schedule
  *retry-safe yan etkidir* (commit'i bloke etmemeli → commit sonrası, garantisini sweep backstop verir).

### Karar 2 — Clear sahipliği invariantı (tek temizleyici)

`ClearDomainEvents()` **yalnızca** post-commit `DispatchDomainEventsInterceptor` tarafından çağrılır.
Pre-commit `OutboxInterceptor`, `aggregate.DomainEvents` listesine **yalnızca OKUR**, asla clear etmez.

- **Sebep:** Pre-commit clear, aynı SaveChanges'in post-commit aşamasında dispatcher'a **boş liste**
  bırakır → Faz 2'nin `OrderCreated → Hangfire timeout` zinciri **sessizce** kırılır (ADR olmadan fark
  edilmesi zor bir regression). Tek temizleyici kuralı, hem **erken-clear** hem **çift-tüketim** riskini
  birden eler.
- **Zaman çizgisi:** `SavingChangesAsync` (outbox **okur**) → SQL COMMIT → `SavedChangesAsync` (in-process
  **dispatch eder**, *sonra* **clear eder**). "clear ≥ dispatch" invariantı (mevcut) korunur, üstüne
  "clear yalnızca post-commit" invariantı eklenir.

### Karar 3 — Domain event ≠ Integration event (Type→factory registry)

Domain event (`IDomainEvent`, `OrderHub.Common`) servis-içi kalır; integration event
(`IIntegrationEvent`, `OrderHub.EventBus`) servis sınırını aşar. Bunlar **ayrı tiplerdir**; outbox'a
**integration tipi** serileştirilip yazılır (domain tipi sınırı aşmaz → contract coupling önlenir).

- Çeviri `OutboxInterceptor` içinde, bir **Type→factory registry** ile yapılır:
  `OutboxEventRegistry` ≈ `IReadOnlyDictionary<Type, Func<IDomainEvent, IIntegrationEvent>>`.
- Interceptor hangi domain event'in hangi integration event'e çevrileceğini **hardcode etmez**; eşleme
  **DI'dan beslenir** (OrderService composition root — sonraki adım). Registry'de karşılığı olmayan
  domain event yalnızca in-process kalır (örn. yalnız-timeout `OrderCreated`). Böylece tek bir domain
  event in-process'e, integration'a veya her ikisine gidebilir; karar merkezîdir, dağınık değil.

### Karar 4 — EventId uçtan uca taşınır (dedup zinciri)

`IDomainEvent.EventId` → `IIntegrationEvent.Id` → `OutboxMessage.Id` (PK, **unique index**).

- Aynı domain event ikinci kez outbox'a yazılmaya çalışılırsa unique index reddeder (**producer-side
  dedup**, ROADMAP §3.2). Aynı `Id` consumer'a ulaştığında Inbox pattern ikinci işlemeyi engeller
  (**consumer-side dedup**, §3.5).
- `OutboxEventRegistry`, çeviri sonrası `integrationEvent.Id == domainEvent.EventId` invariantını
  **runtime'da doğrular** (eşleşmezse fail-fast). Böylece dedup zinciri bir factory yazım hatasıyla
  sessizce kopamaz (K5). `IDomainEvent.EventId`'nin XML-doc'u Faz 1'den beri bu rolü öngörüyordu.

### Karar 5 — Processor hata sınıfı: transient (publish) ≠ poison (deserialize)

- **Eklendi:** 2026-06-25 (Faz 3 Adım 3d-4a).

`OutboxProcessorService.PublishAsync` ilk yazımında deserialize ve publish hatasını **tek catch'te** ele
alıyordu; ikisi de `MarkFailed` (`RetryCount++`) çağırıyordu. Sorgu `RetryCount < MaxRetryCount(5)` ile
filtrelediğinden, **broker ~5 poll (≈10 sn) down kalırsa** geçerli mesajların `RetryCount`'u 5'e ulaşıp
**kalıcı düşüyordu** → broker dönse bile asla publish edilmiyordu (§3.8 ihlali = veri kaybı). Düzeltme iki
hata sınıfını ayırır:

- **Deserialize hatası = KALICI/poison:** `OutboxMessageSerializer.Deserialize` çözülemez tip veya
  `IIntegrationEvent`'e dönmeyen payload için fail-fast atar → retry düzeltmez. **Terminal sayaç korunur**
  (`RetryCount++` → `MaxRetryCount`'ta DLQ, `DeadLettered` Error log → Seq alert, §3.8). Sayaç tutulur (anında
  terminal değil): eksik CLR tipi sonradan deploy edilirse bounded pencerede kurtulabilir.
- **Publish hatası = GEÇİCİ/transient:** broker-down. **`RetryCount` ARTMAZ**, `ProcessedOnUtc` null kalır →
  sonraki poll yeniden dener. Yalnız `PublishDeferred` (Warning) log; `MarkFailed` çağrılmaz.
- **Publish timeout (fail-fast):** broker erişilemezken MassTransit publish'i belirsiz süre bloke edebilir →
  poll döngüsü asılır. Publish, `stoppingToken` + `OutboxProcessorOptions.PublishTimeout` (default 10 sn)
  linked-CTS ile çağrılır → timeout iptali transient'tir (deferred). **Shutdown iptali ayrılır:**
  `OperationCanceledException when stoppingToken.IsCancellationRequested` → yeniden fırlatılıp döngü temiz
  durur (deferred log basılmaz); aksi (timeout) → deferred.
- **Transport-agnostik sınır korunur:** publish catch'i **geniş** (`Exception`); RabbitMQ/MassTransit exception
  tipine referans YOK (`OrderHub.Outbox` transport'u bilmez, yalnız `IIntegrationEventPublisher`'ı). Gerekçe:
  outbox'tan deserialize edilen nesne zaten geçerli bir `IIntegrationEvent` CLR objesidir → publish-anı poison
  bu mimaride pratikte imkânsız (gerçek poison deserialize'da yakalanır); dolayısıyla tüm publish hataları
  transient kabul edilir. Artık residual risk (deserialize olup broker'ın hep reddettiği mesaj) kabul edilir;
  gerekirse ileride ayrı transient-eşik/alert eklenir (YAGNI).

**Reddedilen:** publish exception'ını broker-down vs poison diye **tipiyle ayırmak** → transport-spesifik
exception tiplerini building block'a sızdırır (transport-agnostik tasarım ihlali) + publish-poison
neredeyse-imkânsız senaryo için karmaşıklık. Gerçek-broker fail-fast/reconnect davranışı 3d-4b'de doğrulanır.

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
- Bu ADR'nin "Faz 3 Evrim — Transactional Outbox Entegrasyonu" bölümü (Outbox adoption kararları)
- [ADR-0004](0004-masstransit-rabbitmq.md) (MassTransit/RabbitMQ taşıma katmanı + exchange topolojisi)
