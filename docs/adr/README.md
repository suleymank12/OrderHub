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

| #    | Başlık                                          | Durum    | Tarih      |
|------|-------------------------------------------------|----------|------------|
| [0001](0001-migration-in-docker.md) | Docker'da veritabanı migration stratejisi | Accepted | 2026-06-01 |

## İlgili

- [CLAUDE.md](../../CLAUDE.md) — proje mutlak kuralları (K1–K5)
- [ROADMAP.md](../../ROADMAP.md) — faz planı
