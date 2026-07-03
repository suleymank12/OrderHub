# ADR-0008: Gateway Edge + Distributed Observability

- **Status:** Accepted
- **Tarih:** 2026-07-03
- **Karar verenler:** Süleyman

## Context

Faz 6 (ROADMAP §6), altı-servis mesh'ini **operable** bir sisteme çevirir: tek giriş noktası (YARP gateway),
gateway→downstream **resilience** (Polly), ve tüm hop'larda **distributed tracing** (OpenTelemetry). Bu ADR,
faz boyunca alınan ve mülakatta savunulacak dört kritik kararı kaydeder. Her biri bir *alternatif* karşısında
bilinçli seçildi.

## Decision

### Karar 1 — HealthChecks.UI için scoped Central Package Management (nearest-props)

**Karar:** Gateway (ve gateway integration test projesi) kendi `Directory.Packages.props`'unu taşır; root'un
~40-pin'lik merkezi seti yerine bu **scoped** set devreye girer.

**Neden:** `AspNetCore.HealthChecks.UI` 8.0.1'i eklemek root CPM setiyle birleşince NuGet resolver'ı bozuyordu:
paketin ağır transitive grafiği (KubernetesClient / YamlDotNet / Fractions / prometheus-net — bazıları sürümsüz
transitive bağımlılık deklare eder) büyük pin setiyle etkileşince **TÜM top-level paketler alt-sınırını kaybediyor**
(NU1604) ve **en eski** sürüme çöküyordu — `Yarp.ReverseProxy` → 1.0.0 (güvenlik açıklı), `Serilog.Sinks.Seq`
→ 1.4.4. Kök-neden throwaway ile izole edildi: paketler tek başına ve KÜÇÜK bir CPM setiyle sağlıklı; kırılma yalnız
büyük merkezi-pin setiyle. Tek paket çıkarmak düzeltmiyor (bisect: çoklu-tetikleyici).

**Alternatifler:**
- *CPM'den tümüyle çık (opt-out, explicit sürümler):* §1.1'in "tüm versiyon yönetimi CPM ile" kuralını deler,
  güvenlik-sürüm kontrolünü kaybeder → red.
- *Root'ta tek suçlu pin'i bul + sabitle:* bisect çoklu-tetikleyici gösterdi, kırılgan → red.
- **Scoped CPM (seçilen):** NuGet'in *en yakın* `Directory.Packages.props`'u kullanma özelliği (merge YOK).
  CPM **korunur** (hiyerarşik), yalnız gateway'e scope'lanır. Gateway zaten **mimari izole** (saf-edge, hiçbir
  ProjectReference, kendi Dockerfile restore'u) → scoped props bu izolasyonu paket-yönetimine yansıtır.

**Bedel (dokümante):** root ile ortak paketler (JwtBearer, Serilog) iki yerde pinlenir → güvenlik güncellemesinde
iki dosya gözden geçirilir. Bu **yalnız gateway'in** (ve test'inin) deseni; başka servis kopyalamamalı (props'ta not).

### Karar 2 — Kafka'da manuel W3C traceparent propagation (RabbitMQ otomatik)

**Karar:** Kafka custom producer/consumer'ında trace context **manuel** taşınır (W3C `traceparent`, producer inject
+ consumer extract, ortak `KafkaTraceContextPropagation` helper). RabbitMQ hop'ları MassTransit ile **otomatik**.

**Neden:** RabbitMQ akışı MassTransit üzerinden gider; MassTransit trace context'i native olarak mesaj header'ında
taşır (yalnız `AddSource("MassTransit")` yeter). Kafka ise doğrudan `Confluent.Kafka` client'ıyla kullanılıyor
(ADR-0006 Karar 2: idempotent producer'ı doğrudan kontrol için MassTransit rider değil) → **otomatik propagation
yok**. Aksi halde `Order → Kafka → Analytics/Notification` trace'i Kafka hop'unda **kopardı** (yarım-trace).

**Alternatif:** *MassTransit Kafka rider* — Kafka'yı da MassTransit'e alıp otomatik propagation kazanmak; ancak
ADR-0006'nın idempotent-producer doğrudan-kontrol kararını bozardı → red. Bunun yerine ince manuel helper +
`Propagators`/`TraceContextPropagator`. Otomatik test (gerçek Kafka + in-memory exporter): producer-span ile
consumer-span AYNI TraceId → propagation kırık olsaydı consumer yeni root başlatırdı (yakalanır).

**Dürüst sınır:** Outbox pattern publish'i asenkron arka-plan processor'ında yapar → orijinal HTTP isteğinin trace'i
outbox sınırında segmentlenir (bir sipariş = birden çok trace-id segmenti). Her hop KENDİ segmentinde kesintisiz;
uçtan uca tek-trace-id için outbox'a traceparent saklama gerekir (dokümante follow-up).

### Karar 3 — Idempotent-only retry (allowlist) + çift-order guard

**Karar:** Gateway→downstream retry YALNIZ **allowlist** metodlarına (GET/HEAD/OPTIONS) uygulanır; POST/PUT/PATCH/
DELETE ve bilinmeyen her method **asla** retry edilmez.

**Neden:** `POST /api/orders` idempotent değil (idempotency-key yok) → retry **çift order** yaratır. Guard **iki
katman**: (1) allowlist method-gating (kod invariant'ı), (2) YARP request body'yi stream eder → POST body tek-sefer
okunur, teknik olarak da re-send edemez (belt-and-suspenders).

**Alternatif:** *Denylist ("POST hariç hepsi retry")* — yeni/bilinmeyen bir non-idempotent method eklenirse
sessizce retry'a girer (güvensiz varsayılan) → red. **Allowlist** güvenli varsayılan: listede yoksa retry yok.
Deterministik test (WireMock): POST fail'de downstream hit=1 (çift-order imkânsız); allowlist-dışı PUT hit=1.
Circuit-breaker **per-cluster** (bir downstream çökünce diğerleri etkilenmez), timeout ile birlikte tüm metodlara.

### Karar 4 — Defense-in-depth JWT (gateway merkezi + downstream tekrar)

**Karar:** JWT doğrulaması gateway'de **merkezi** (aynı secret ile erken 401 → geçersiz token downstream'e hiç
gitmez) AMA downstream servisler de doğrulamayı **korur** (kaldırılmaz).

**Neden:** Gateway erken-401 performans/attack-surface kazancı verir; ancak downstream'in auth'unu kaldırmak,
gateway bypass edilirse (internal network, yanlış config, gelecekte doğrudan erişim) servisleri **açıkta** bırakırdı.
Downstream auth'u korumak = **defense-in-depth**: her katman kendi başına güvenli.

**Alternatif:** *Yalnız gateway'de auth, downstream açık (claims forward)* — tek doğrulama noktası basit ama
gateway'e tam güven gerektirir; edge bypass → tam açık → red. Gateway ve downstream **aynı** `TokenValidationParameters`
(secret/issuer/audience) paylaşır (config'ten, K3).

## Consequences

- Gateway **saf-edge** kalır: hiçbir servis/Contracts/building-block ProjectReference'ı yok; downstream'e yalnız
  HTTP forward (YARP) + HTTP poll (dashboard). Observability için bile building-block ref almaz — gateway kendi
  küçük local tracing wiring'ini taşır (AspNetCore + Http + OTLP; SqlClient/MassTransit yok).
- OpenTelemetry root CPM'e **güvenli** eklendi (HealthChecks.UI'nin aksine): throwaway + locked-mode ile EF Core 8
  LTS kilidi ve Extensions 8.x korundu, sıfır 9.x. DB span'leri **SqlClient** (stable) ile — EF Core instrumentation
  beta olduğundan kullanılmadı.
- Tam suite paralel koşumu Testcontainers'ı over-subscribe ediyordu (cross-assembly); test-runner (check-acceptance)
  projeleri **sıralı** koşacak şekilde düzeltildi (ürün/test kodu değişmeden).

## Follow-ups

- **Metrics/Grafana ayrı faz** (Karar C): bu faz yalnız *tracing*; `/metrics` Prometheus + Grafana gelecek fazda.
- Uçtan uca tek-trace için **outbox'ta traceparent saklama** (Karar 2 dürüst sınırı).
- Gateway prod'da yalnız edge expose (dev'de downstream portları da açık); rate-limit/CORS prod-sıkılaştırma.
