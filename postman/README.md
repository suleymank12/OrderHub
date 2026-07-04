# OrderHub — API Collection (Postman)

Gateway edge (`http://localhost:8000`) üzerinden tüm endpoint'leri kapsayan Postman collection'ı.

## Kullanım

1. Stack'i ayağa kaldır (repo kökü [README](../README.md) → Quick Start): `cd docker && docker compose up -d --build`.
2. Postman'de **Import** → bu klasördeki iki dosyayı seç:
   - `OrderHub.postman_collection.json`
   - `OrderHub.postman_environment.json`
3. Sağ üstten **OrderHub Local** environment'ını seç.
4. **Auth → Dev Token (anonymous)** çağır → test script JWT'yi `{{token}}`'a yazar.
5. **Orders → Create Order** → `{{orderId}}` otomatik set edilir; saga akışı otomatik başlar.
6. **Orders → Get Order By Id** ve **Analytics → Get Order Projection** ile durumu/projection'ı izle.

> Bruno / Insomnia kullanıyorsan bu Postman collection'ını doğrudan **import** edebilirsin
> (her ikisi de Postman v2.1 formatını okur).

## ★ Dürüst akış sınırı (K2)

`Confirm` / `Pay` / `Ship` için **HTTP endpoint yoktur** — bu geçişler saga tarafından RabbitMQ
command'leriyle **otomatik** sürülür (bkz. [ADR-0007](../docs/adr/0007-saga-orchestration.md)).
Bu yüzden collection'da **uydurma `POST /confirm` veya `POST /pay` isteği yoktur**. HTTP ile
tetiklenebilen tek lifecycle olayı sipariş oluşturmadır; gerisini saga yürütür.

`{{paymentId}}` yalnızca saga `ProcessPayment` çalışınca üretilir — id'yi PaymentService veya
Seq'ten alıp environment'a elle yaz.

## İçerik

| Klasör | İstek | Auth |
|--------|-------|------|
| Auth | Dev Token | anonymous |
| Orders | Create Order · Get Order By Id · List Orders | Bearer |
| Payments | Get Payment By Id | Bearer |
| Analytics | Get Order Projection · Get Daily Revenue | Bearer |
| Health | Gateway Liveness · Health Dashboard · OrderService Readiness (direct port) | anonymous |
