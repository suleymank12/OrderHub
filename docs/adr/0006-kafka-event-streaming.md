# ADR-0006: Kafka for Event Streaming (RabbitMQ command-only, Kafka event-log)

- **Status:** Accepted
- **Tarih:** 2026-06-25
- **Karar verenler:** Süleyman

## Context

Faz 4, OrderService'in sipariş yaşam-döngüsü olaylarını (OrderCreated/Confirmed/Paid/Cancelled) bir
**event-stream**'e yayımlayıp AnalyticsService'in bunu tüketerek CQRS read-model üretmesini istiyor
(ROADMAP §4). Faz 3 mesajlaşmayı **RabbitMQ + MassTransit** (command, point-to-point) ve **transactional
outbox** üzerine kurmuştu (ADR-0004, ADR-0002 "Faz 3 Evrim"). ADR-0004 zaten "Evrim: Faz 4'te event-stream
Kafka'ya gider; RabbitMQ command-only kalır" notunu düşmüştü; bu ADR o sözü somut kararlara bağlar.

## Decision

### Karar 1 — İki taşıma, net rol ayrımı

- **RabbitMQ (MassTransit):** *command* / point-to-point — "şu iş yapılmalı" (ProcessPayment). Tek mantıksal
  tüketici, retry/DLQ kolay, düşük throughput'lu kritik mesajlaşma (Faz 3).
- **Kafka:** *event-log* / pub-sub / **replay edilebilir** — yüksek throughput, **çok bağımsız tüketici** aynı
  stream'i kendi offset'iyle okur (Analytics şimdi; audit/ML/future sonra). "Bu oldu" semantiği.

Rol ayrımı bilinçli: command'i Kafka'ya, event-stream'i RabbitMQ'ya koymak ikisinin de güçlü yanını israf
ederdi. Mülakat argümanı: *command ≠ event*; biri imperatif tek-hedef, diğeri deklaratif çok-tüketici.

### Karar 2 — Confluent.Kafka (MassTransit Kafka rider DEĞİL)

Kafka producer/consumer için **doğrudan `Confluent.Kafka`** (CLAUDE.md §3 stack kilidi: Confluent.Kafka 2.x),
MassTransit'in Kafka rider'ı değil.

- **Gerekçe:** ROADMAP §4.3 producer'da `acks=all` + `enable.idempotence=true` ister → **idempotent producer**
  üzerinde doğrudan kontrol; manual offset commit (§4.4, at-least-once) consumer tarafında açık kontrol.
  MassTransit rider bu ayarları soyutlayıp kontrolü azaltırdı. RabbitMQ MassTransit'te kalır; Kafka raw client.
- **Trade-off (kabul):** İki mesajlaşma kütüphanesi (RabbitMQ=MassTransit, Kafka=Confluent). Rol ayrımıyla
  tutarlı; her transport kendi idiomatik client'ıyla.

### Karar 3 — Tek outbox + routing publisher (transport-agnostik outbox korunur)

Faz 3 outbox (tablo + `OutboxInterceptor` + `OutboxProcessorService`) **bilinçli transport-agnostik** kuruldu
(ADR-0002 Karar 5 geniş-catch sınırı, ADR-0004 Karar 3 System.Text.Json). Kafka **ayrı outbox/mekanizma
istemez**: aynı outbox iki target'a route eder.

- `IIntegrationEvent` altına marker'lar: **`IRabbitMqEvent`** (command) ve **`IKafkaEvent`** (event-stream,
  `Topic` + `PartitionKey` taşır). Processor'ın gördüğü `IIntegrationEventPublisher`, **`RoutingIntegrationEventPublisher`**
  olur: `IKafkaEvent` → Kafka publisher, aksi (`IRabbitMqEvent` veya **işaretsiz**) → MassTransit/RabbitMQ.
- **İşaretsiz → RabbitMQ (default):** RabbitMQ tek transport'tu; işaretsiz event'i ona route etmek Faz 3
  davranışını korur (geriye uyum). `IKafkaEvent` yeni transport'a **explicit opt-in**.
- **Outbox tablosu/interceptor/processor DEĞİŞMEZ** — yalnız publisher routing'e genişler. §4.8 "Kafka down →
  outbox kaybetmez" bedavaya gelir: Kafka publish aynı outbox + retry + transient/poison ayrımından (3d-4a) geçer.

### Karar 4 — Composite outbox PK `(Id, Ordinal)`: 1:N fan-out, `Id == EventId` KORUNUR

Bir domain olayı **N** integration olayına fan-out edebilmeli (OrderConfirmed → RabbitMQ `ProcessPayment`
**+** Kafka `OrderConfirmed`). Eski tek-kolon PK = `Id` ve `Id == EventId` invariantı (ADR-0002 Karar 4) ile
**N satır aynı Id** → PK çakışması. Çözüm: outbox PK **`(Id, Ordinal)`** composite.

- `Id` hâlâ **== kaynak EventId** (ADR-0002 Karar 4 **harfi harfine korunur**; uçtan uca dedup zinciri sağlam).
  `Ordinal` (int) = registry fan-out sırası (deterministik: tek-factory → 0; çok-factory → 0,1,…). At-least-once
  republish aynı (Id, Ordinal)'leri üretir → unique PK producer-side dedup'ı korur.
- **Inbox precedent'iyle simetrik:** inbox PK zaten `(MessageId, MessageType)` (ADR-0005 Karar 5). Outbox da
  composite olur. `Ordinal` yalnız **outbox-içi** disambiguator'dır — consumer'a ulaşmaz; consumer inbox dedup'ı
  hâlâ `(Id=EventId, MessageType)` (aynı EventId'li iki farklı tip → farklı inbox satırı). `Type` PK'ya
  alınmadı çünkü AssemblyQualifiedName (nvarchar(500)) SQL Server PK key limitini (~900 byte) aşar; küçük
  `Ordinal` sığar.
- **Reddedilen — distinct deterministik Id + ayrı CausationId:** şema migration'ı gerekmezdi ama `Id == EventId`
  invariantını kırar (ADR-0002 Karar 4 revize), mevcut `Id` değerlerini/testlerini bozar, deterministik-Guid
  "magic" getirir. Composite PK invariantı korur + inbox ile tutarlı → tercih edildi.

### Karar 5 — Kafka topology: key = OrderId, JSON, schema registry yok

`order-hub.orders.events` (key = **OrderId** → aynı order aynı partition → per-order ordering), **JSON** value
(Avro/Protobuf bu scope'ta overkill — schema registry yok, ROADMAP §4.2/§4.6). `payments.events` topic'i
topolojide tanımlı ama §4.4 consumer'ı yalnız order events tüketir → şimdilik **boş** (YAGNI; audit consumer
gelince doldurulur).

## Consequences

- **Olumlu:** Faz 3 outbox yatırımı (atomiklik, retry, poison ayrımı) Kafka'ya bedavaya uzanır.
- **Olumlu:** Composite PK ADR-0002 Karar 4'ü korur + inbox ile simetrik (tek mental model).
- **Olumsuz / dikkat:** İki mesajlaşma kütüphanesi; outbox şema migration'ı (Order + Payment DB) — fresh sistemde
  veri taşıma yok, mevcut satırlar Ordinal=0 ile uyumlu.
- **Evrim:** Faz 5 Saga MassTransit (RabbitMQ) üstünde; Kafka event-stream paralelde büyür (audit/ML tüketicileri).

## İlgili

- [ADR-0004](0004-masstransit-rabbitmq.md) (RabbitMQ command + "Faz 4 Kafka evrimi" notu — bu ADR onu gerçekler)
- [ADR-0002](0002-in-process-domain-event-dispatch.md) "Faz 3 Evrim" Karar 3-4 (outbox çeviri + EventId dedup zinciri)
- [ADR-0005](0005-custom-inbox.md) Karar 5 (inbox composite PK — outbox composite PK simetrisi)
- [ROADMAP.md](../../ROADMAP.md) §4 (Kafka topology, AnalyticsService, producer two-target routing)
