# ADR-0004: MassTransit + RabbitMQ (Command Messaging Layer)

- **Status:** Accepted
- **Tarih:** 2026-06-01
- **Karar verenler:** Süleyman

## Context

Faz 3, OrderService ile PaymentService arasında **command-style** mesajlaşma kuruyor: `Order.Confirm()`
→ `ProcessPaymentIntegrationEvent` → PaymentService işler → `PaymentSucceeded`/`PaymentFailed` →
OrderService tüketir (ROADMAP §3.4). Bu akış reliable olmalı: retry, dead-letter, idempotent consumer
(§3.5). Ayrıca Faz 5 **Saga** (MassTransit state machine, ROADMAP §5.2) bu taşıma katmanının üstüne
kurulacak. Stack §3 zaten **MassTransit 8.x + RabbitMQ 3.13** olarak kilitli; bu ADR seçimi *gerekçelendirir*
ve iki açık tasarım kararını (exchange tipi, serializer/Newtonsoft durumu) kayda geçirir.

Mesaj **taşıma** katmanı (publish/transport) bu ADR'nin konusu. Mesajın **güvenilir üretimi**
(transactional outbox, pre-commit interceptor, dedup) ayrı karardır:
[ADR-0002 "Faz 3 Evrim"](0002-in-process-domain-event-dispatch.md). İkisi katmanlıdır: outbox *ne zaman
ve atomik olarak* yazılacağını, MassTransit *nasıl taşınacağını* çözer.

## Decision

### Karar 1 — MassTransit, raw `RabbitMQ.Client` yerine

Taşıma soyutlaması olarak **MassTransit 8.5.x** kullanılır (raw `RabbitMQ.Client` ile elle topology/consumer
yönetimi değil).

**Gerekçe:**

- **Retry + DLQ first-class:** §3.5'in istediği exponential backoff retry ve `_error` DLQ queue'ları
  MassTransit'te konfigürasyon; raw client'ta elle ack/nack + retry state machine + ölü mesaj yönlendirmesi
  yazmak gerekir (yüzlerce satır, hata açık).
- **Topology convention:** Exchange/queue/binding'leri mesaj tipinden otomatik türetir; consumer endpoint
  konfigürasyonu deklaratif.
- **Faz 5 saga bedavaya gelir:** `MassTransit.SagaStateMachine` (Automatonymous) aynı kütüphanenin parçası.
  Raw client seçilseydi Faz 5'te ya saga'yı elle yazacaktık ya da o noktada MassTransit'e geçecektik —
  ikinci durumda taşıma katmanını bir kez daha yazmak. Şimdi MassTransit, Faz 5'i **risksiz** kılar.
- **Test edilebilirlik:** `MassTransit.TestFramework` (in-memory harness) + Testcontainers RabbitMQ ile
  §3.7 integration testleri kolaylaşır.

**Trade-off (kabul edildi):** Ekstra bağımlılık ağırlığı (`MassTransit`, `MassTransit.Abstractions`,
`MassTransit.RabbitMQ` + transitive `RabbitMQ.Client`) ve bir öğrenme eğrisi (consumer/endpoint/topology
zihinsel modeli). Karşılığında §3.5 reliability ve Faz 5 saga'nın elle-yazım maliyeti tasarruf edilir —
mülakatta savunulabilir net bir kazanç. Lisans: MassTransit **8.x Apache-2.0** (ücretsiz); **v9 ticari**
→ Directory.Packages.props'ta 8.5.9'a pinlenir, v9'a sessiz sıçrama engellenir (FluentValidation/MediatR
pin disiplini ile aynı, CLAUDE.md §3).

### Karar 2 — Exchange tipi: command için **direct** routing

`ProcessPaymentIntegrationEvent` için **direct exchange + sabit routing key** (point-to-point), topic değil.

- **Gerekçe:** Bu bir **command**'dir — "şu ödeme işlensin", tek bir mantıksal tüketici
  (`payment-service.process-payment`). Topic exchange'in wildcard/multi-binding esnekliği burada
  kullanılmayan bir özelliktir; point-to-point niyeti routing key pattern'ı (`payment.process`) en sade
  ve en az sürprizle ifade eder. ROADMAP §3.4 topolojisiyle uyumlu:
  - Exchange: `order-hub.payment` (type: **direct**)
  - Queue: `payment-service.process-payment`
  - Routing key: `payment.process`
- **Sınır:** Bu karar **command** akışı içindir. Faz 4'te domain event **stream**'i (pub-sub, çok-tüketicili,
  replay) farklı bir araç olan **Kafka**'ya gidecek (ROADMAP §4, ADR-0001'de gerekçelendirilecek). RabbitMQ
  command, Kafka event-stream ayrımı bilinçlidir; topic exchange ihtiyacı doğarsa o Kafka tarafında karşılanır.
- **Uygulama notu:** Somut MassTransit endpoint/exchange konfigürasyonu (mesaj tipi → topology eşlemesi)
  servis entegrasyon adımında (OrderService/PaymentService composition root) yapılır; bu BuildingBlocks
  adımında yalnızca publish soyutlaması (`IIntegrationEventPublisher`) ve RabbitMQ wrapper kurulur.

### Karar 3 — Serializer: System.Text.Json — §3 Newtonsoft yasağı durumu

CLAUDE.md §3 `Newtonsoft.Json`'u yasaklar. **MassTransit 8 varsayılan serializer'ı System.Text.Json'dır**
→ bu yasakla **uyumludur, istisna gerektirmez**.

- **Kanıt (nuspec, net8.0 hedefi, doğrulandı):** `MassTransit 8.5.9` ve `MassTransit.RabbitMQ 8.5.9`
  bağımlılık grafiğinde **`Newtonsoft.Json` yoktur**. net8.0 grubu yalnızca `MassTransit.Abstractions`,
  `Microsoft.Extensions.*` (DI/Hosting/Logging/Options/HealthChecks) ve `RabbitMQ.Client 7.2.1` getirir;
  `System.Text.Json` net8.0'da framework'ün parçasıdır.
- **ADR-0003 ile kontrast:** Hangfire iş argümanlarını **internal Newtonsoft.Json** ile serialize eder →
  orada bilinçli bir **transitive §3 istisnası** notu gerekti (Hangfire'ın internal kullanımı, bizim
  *doğrudan* kullanımımız değildir). MassTransit'te böyle bir istisnaya **gerek yoktur** — ne doğrudan ne
  transitive Newtonsoft bağımlılığı vardır. (İleride MassTransit serializer'ı Newtonsoft'a *değiştirmek*
  ayrı opt-in `MassTransit.Newtonsoft` paketi gerektirir; almıyoruz.)
- **Tutarlılık:** Bizim integration event'lerimiz `OrderHub.Outbox`'ta da System.Text.Json ile serialize
  edilir (outbox payload) — uçtan uca tek serializer ailesi.

## Consequences

- **Olumlu:** §3.5 retry/DLQ ve Faz 5 saga düşük maliyetle gelir; topology convention boilerplate'i keser.
- **Olumlu:** Tek serializer ailesi (System.Text.Json) — outbox payload ↔ transport tutarlı, §3 temiz.
- **Olumlu:** 8.5.9 pini v9 ticari lisansına sessiz geçişi engeller.
- **Olumsuz / dikkat:** MassTransit zihinsel modeli (consumer, endpoint, ConfigureEndpoints, topology)
  öğrenilmeli; yanlış endpoint konfigürasyonu sessiz "mesaj kayboldu" semptomu verebilir → integration
  test (Testcontainers RabbitMQ) §3.7'de zorunlu.
- **Olumsuz / dikkat:** `RabbitMQ.Client 7.x` API'si 6.x'ten farklı; doğrudan client'a düşersek (debug)
  sürüm farkı bilinmeli. Normalde MassTransit soyutlamasının altında kalır.
- **Evrim:** Faz 4'te event-stream Kafka'ya gider; RabbitMQ command-only kalır. Faz 6'da MassTransit
  OpenTelemetry instrumentation eklenir (ROADMAP §6.3).

## Implementation Note — Publish vs Send Topology (3c)

- **Eklendi:** 2026-06-01 (Faz 3 Adım 3c).

ADR Karar 2, command akışı için **direct/point-to-point** intent'i belirtti. 3c implementasyonu, outbox
processor'ın `IIntegrationEventPublisher.PublishAsync` → MassTransit **`Publish`** kullanması nedeniyle
mesajı **message-type exchange**'ine (pub-sub semantiği) gönderir; consumer'lar `ConfigureEndpoints`
convention'ı ile otomatik queue'ya bağlanır.

- **Neden de facto point-to-point:** Her integration event'in **tek** bir tüketici servisi vardır
  (`ProcessPaymentIntegrationEvent` → yalnız PaymentService; `PaymentSucceeded/Failed` → yalnız OrderService).
  Tek consumer + tek queue → mesaj tek hedefe gider; davranışsal olarak point-to-point.
- **Neden `Send` değil:** Strict direct-routing (gerçek `Send` + explicit endpoint adresi) outbox
  publisher'ının her mesaj tipi için hedef endpoint adresini bilmesini gerektirir → producer'a topology
  bilgisi sızdırır, ekstra konfigürasyon. Tek tüketici varken kazanç yok (**YAGNI**).
- **Bilinçli sadeleştirme, sessiz drift değil:** `Publish`+convention seçimi burada kayda geçirildi. Birden
  çok bağımsız tüketici (audit, analytics) gerektiğinde `Publish`'in pub-sub'ı zaten doğru araçtır; gerçek
  command-only direct-routing gerekirse (örn. competing-consumer load balancing dışı bir neden) ayrı bir
  karar olarak revize edilir. Faz 4'te event-stream zaten Kafka'ya gider (RabbitMQ command-only kalır).

## Implementation Note — Retry Policy + `_error` DLQ (3d-3)

- **Eklendi:** 2026-06-25 (Faz 3 Adım 3d-3).

Karar 1 retry/DLQ'yu "MassTransit'te konfigürasyon" diye gerekçelendirmişti; bu not somut yapılandırmayı kayda
geçirir. 3d öncesi `AddRabbitMqEventBus` retry **içermiyordu** → MassTransit default 0 retry → faulted mesaj
direkt `_error`'a düşüyordu (§3.5 exponential/max-5 yapılandırılmamış).

- **Bus-level, EN DIŞTA:** `UsingRabbitMq` içinde `rabbit.UseMessageRetry(r => r.Exponential(...))`, inbox
  consume filter'ından **önce** eklenir. MassTransit consume-pipe spec'leri ekleme sırasıyla uygulanır (ilk =
  en dış) → sıra: **retry → [her denemede] inbox filter → consumer**. Bus-level seçimi: hem retry hem inbox
  aynı pipe'ta deterministik sırada kalır (endpoint-level olsa göreli sıra muğlaklaşırdı) + tek noktadan tüm
  consumer'lara uygulanır.
- **Inbox ile etkileşim (ADR-0005 Karar 4 ile tutarlı):** retry en dışta olduğundan her deneme **yeni
  consume-scope** (yeni DbContext) alır. Transient hata → consumer `SaveChanges`'e ulaşmadan throw → inbox
  filter'ın tracked-Add'i rollback → satır yok → retry **temiz** tekrar dener. Başarı → atomik commit → satır
  var → sonraki redelivery skip. (3d-3 testi bunu kanıtlar: 6 consume = inbox hiç skip etmedi + commit edilmiş
  satır yok.)
- **Exponential parametreleri:** `RetryLimit=5` (§3.5), `MinInterval=1s`, `MaxInterval=30s`, `IntervalDelta=2s`.
  Transient kesinti (broker reconnection, kısa DB kilidi) için yeterli pencere; üst sınır 30s ile retry storm
  ve queue tıkanması engellenir. Worst-case ~1+3+7+15+30 ≈ 56s sonra DLQ.
- **`_error` DLQ = otomatik:** retry tükenince MassTransit mesajı `<queue>_error`'a taşır (convention) +
  `Fault<T>` publish eder. Ekstra DLQ kodu **yazılmaz** (K2). 3d-3 testi mesajı gerçek `_error` queue'dan okuyarak doğrular.
- **API kararı (config, hardcode değil):** policy `RabbitMqOptions.Retry` (`RabbitMqRetryOptions`) üzerinden
  gelir; varsayılanlar = production değerleri. Gerekçe: tek policy kaynağı + env-tunable (retry sayısı/interval
  secret değil ama ortama göre ayarlanabilir) + test edilebilirlik (gerçek-broker testi aynı pipe sırasını
  çalıştırıp interval'i ms'ye düşürür; reimplementation değil, prod kod yolu doğrulanır).

## Alternatives Considered

### Seçenek A: Raw `RabbitMQ.Client`

- **Artılar:** Sıfır soyutlama, tam kontrol, minimal bağımlılık.
- **Eksiler:** Retry/DLQ/topology/idempotency elle yazılır (çok kod, hataya açık); Faz 5 saga için yine
  bir kütüphane gerekir → ya elle saga ya sonradan MassTransit'e migration (taşıma katmanını iki kez yaz).
- **Karar:** **Reddedildi** — reliability ve saga maliyeti MassTransit'i net haklı çıkarır.

### Seçenek B: NServiceBus / Brighter / Wolverine

- **Artılar:** Olgun alternatifler; bazıları outbox'ı built-in sunar.
- **Eksiler:** NServiceBus ticari lisans (maliyet). Stack §3 zaten MassTransit'e kilitli; sapma gerekçesiz
  scope/risk artışı. Wolverine farklı bir mimari fikir dayatır (kendi mediator'ı) — MediatR §3 kilidiyle çakışır.
- **Karar:** **Reddedildi** — stack kilidi + lisans + mimari uyum MassTransit lehine.

### Seçenek C: Exchange tipi **topic** (direct yerine)

- **Artılar:** İleride routing esnekliği (wildcard binding).
- **Eksiler:** Command akışı point-to-point; kullanılmayan esneklik = gereksiz karmaşıklık (YAGNI). Pub-sub
  ihtiyacı zaten Faz 4 Kafka'ya ait.
- **Karar:** **Reddedildi** — command için direct; stream için Kafka.

## İlgili

- [CLAUDE.md](../../CLAUDE.md) §3 (stack kilidi, Newtonsoft yasağı, MassTransit/RabbitMQ sürümleri)
- [ROADMAP.md](../../ROADMAP.md) §3.1 (EventBus), §3.4 (topology), §3.5 (retry/DLQ/inbox), §5.2 (saga)
- [ADR-0002](0002-in-process-domain-event-dispatch.md) "Faz 3 Evrim" (outbox: mesajın güvenilir üretimi — bu ADR taşıması)
- [ADR-0003](0003-hangfire-storage-and-timeout-reliability.md) §6 (Hangfire Newtonsoft transitive istisnası — kontrast)
- İleride: ADR-0001 (RabbitMQ vs Kafka — neden ikisi de) Faz 4'te yazılacak
