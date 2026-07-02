using MassTransit;

namespace OrderHub.OrderProcessingService.Infrastructure.Saga;

/// <summary>
/// OrderProcessingSaga'nın kalıcı durumu (MassTransit <see cref="SagaStateMachineInstance"/>, EF saga repository
/// ile <c>OrderHub_Sagas</c>'a persist edilir). Bu bir <b>process state</b>'idir, domain aggregate'i DEĞİL —
/// bu yüzden Domain katmanı yoktur ve alanlar mutable (EF + MassTransit yazar).
/// <para>
/// <b>Fan-out sayımı (5d-4 Karar B):</b> düz int sayaç yerine <see cref="ReservedProductIds"/> /
/// <see cref="ConfirmedProductIds"/> <b>kümeleri</b> tutulur — aynı ProductId yeniden gelince küme değişmez →
/// redelivery'ye karşı <b>doğal idempotent</b> (saga'ya inbox filter gerekmez). Tamamlanma guard'ı:
/// küme, <see cref="AllProductIds"/>'i kapsayınca. Eşzamanlı mesaj contention'ı <see cref="RowVersion"/>
/// optimistic concurrency + MassTransit retry ile serileşir (ADR-0007 Karar 3).
/// </para>
/// </summary>
internal sealed class OrderProcessingSagaState : SagaStateMachineInstance
{
    /// <summary>Saga örnek kimliği = OrderId (PK, correlation anahtarı).</summary>
    public Guid CorrelationId { get; set; }

    /// <summary>Geçerli durum (MassTransit string state — <c>InstanceState</c> ile eşlenir).</summary>
    public string CurrentState { get; set; } = null!;

    /// <summary>Optimistic concurrency token (SQL Server <c>rowversion</c>, EF üretir/yönetir).</summary>
    public byte[] RowVersion { get; set; } = null!;

    /// <summary>Sipariş sahibi (OrderPlaced'dan saklanır → <c>ProcessPayment</c> için PaymentSucceeded'a kadar tutulur).</summary>
    public Guid CustomerId { get; set; }

    /// <summary>Ödeme tutarı (OrderPlaced'dan saklanır → <c>ProcessPayment</c> için).</summary>
    public decimal Amount { get; set; }

    /// <summary>Para birimi (ISO 4217; OrderPlaced'dan saklanır → <c>ProcessPayment</c> için).</summary>
    public string Currency { get; set; } = null!;

    /// <summary>Kalem sayısı (OrderPlaced.Items.Count) — gözlemlenebilirlik/guard yardımcı bilgisi.</summary>
    public int ItemCount { get; set; }

    /// <summary>OrderPlaced'daki tüm ürün kimlikleri — fan-out tamamlanma guard'ının hedef kümesi.</summary>
    public HashSet<Guid> AllProductIds { get; set; } = [];

    /// <summary>Stoğu ayrılmış (<c>StockReserved</c>) ürünler — küme <see cref="AllProductIds"/>'i kapsayınca rezervasyon tamamlanır.</summary>
    public HashSet<Guid> ReservedProductIds { get; set; } = [];

    /// <summary>Rezervasyonu onaylanmış (<c>StockReservationConfirmed</c>) ürünler — küme tamamlanınca <c>ShipOrder</c>.</summary>
    public HashSet<Guid> ConfirmedProductIds { get; set; } = [];

    // --- Compensation (5e-2, mutsuz yol) ---

    /// <summary>
    /// Telafi başında DONDURULAN hedef release seti: stok-fail'de o ana kadar <see cref="ReservedProductIds"/>
    /// snapshot'ı, ödeme-fail'de <see cref="AllProductIds"/>. Fan-in guard'ının hedefidir — geç gelen event'ler
    /// (örn. straggler StockReserved) bu hedefi DEĞİŞTİRMEZ → telafi deterministik biter (snapshot kararı).
    /// </summary>
    public HashSet<Guid> ProductsToRelease { get; set; } = [];

    /// <summary>Serbest bırakılmış (<c>StockReleased</c>) ürünler — küme <see cref="ProductsToRelease"/>'i kapsayınca <c>CancelOrder</c>.</summary>
    public HashSet<Guid> ReleasedProductIds { get; set; } = [];

    /// <summary>İptal gerekçesi (<c>stock_unavailable</c>/<c>payment_failed</c>) — telafi girişinde saklanır, <c>CancelOrder</c>'a taşınır.</summary>
    public string? CancellationReason { get; set; }
}
