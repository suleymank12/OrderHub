# Architecture Decision Records (ADR)

Bu dizin, OrderHub'da alınan **önemli mimari kararları** ve gerekçelerini kalıcı olarak tutar.
Amaç: "bunu neden böyle yaptık?" sorusunun cevabı kodun değil, **yazılı kararın** içinde olsun —
mülakatçı da, 3 yıl sonra projeyi açan geliştirici de aynı gerekçeyi okusun.

## Format

[Michael Nygard / MADR](https://adr.github.io/) tarzı, hafif. Başlıklar İngilizce
(**Status / Context / Decision / Consequences / Alternatives Considered**), ana metin Türkçe
(proje karma dilde). Her ADR, CLAUDE.md §K5'teki beş soruyu cevaplar:
hangi problem, hangi seçenekler, hangi kriterler, neden bu, hangi yeni sorunlar.

## Naming

- Dosya: `NNNN-kebab-title.md` (4 haneli, monoton artan numara).
- Numara **geri kullanılmaz**; bir karar geçersizleşirse `Superseded by ADR-XXXX` ile işaretlenir, silinmez.

## Status yaşam döngüsü

`Proposed` → `Accepted` → (`Deprecated` | `Superseded by ADR-XXXX`)

## Index

| #    | Başlık                                          | Faz  | Durum    | Tarih      |
|------|-------------------------------------------------|------|----------|------------|
| [0001](0001-migration-in-docker.md) | Docker'da veritabanı migration stratejisi | 1 | Accepted | 2026-06-01 |
| [0002](0002-in-process-domain-event-dispatch.md) | In-process domain event dispatch | 2–3 | Accepted | 2026-06-01 |
| [0003](0003-hangfire-storage-and-timeout-reliability.md) | Hangfire storage, timeout reliability ve dashboard security | 2 | Accepted | 2026-06-01 |
| [0004](0004-masstransit-rabbitmq.md) | MassTransit + RabbitMQ (command messaging layer) | 3 | Accepted | 2026-06-01 |
| [0005](0005-custom-inbox.md) | Custom Inbox (consumer-side dedup) | 3 | Accepted | 2026-06-02 |
| [0006](0006-kafka-event-streaming.md) | Kafka event streaming (RabbitMQ command-only, Kafka event-log) | 4 | Accepted | 2026-06-25 |
| [0007](0007-saga-orchestration.md) | Saga orchestration (orchestration vs choreography) | 5 | Accepted | 2026-06-26 |
| [0008](0008-gateway-edge-and-observability.md) | Gateway edge + distributed observability | 6 | Accepted | 2026-07-03 |

## İlgili

- [CLAUDE.md](../../CLAUDE.md) — proje mutlak kuralları (K1–K5)
- [ROADMAP.md](../../ROADMAP.md) — faz planı
