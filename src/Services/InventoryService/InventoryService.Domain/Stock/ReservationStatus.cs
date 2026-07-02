namespace OrderHub.InventoryService.Domain.Stock;

/// <summary>
/// Bir <see cref="Reservation"/>'ın yaşam döngüsü durumu. <see cref="Pending"/> başlangıç (stok ayrıldı,
/// onay bekliyor); <see cref="Confirmed"/>/<see cref="Released"/>/<see cref="Expired"/> terminaldir
/// (yeniden geçiş yok → idempotent işleme). <see cref="Released"/> = compensation ile iade;
/// <see cref="Expired"/> = 15dk timeout ile iade (saga için ayrı sinyal — release ≠ expiry).
/// </summary>
public enum ReservationStatus
{
    /// <summary>Stok ayrıldı, saga onayı/iadesi bekleniyor (varsayılan).</summary>
    Pending = 0,

    /// <summary>Rezervasyon onaylandı; stok kalıcı düştü (terminal).</summary>
    Confirmed = 1,

    /// <summary>Rezervasyon serbest bırakıldı (compensation); stok iade edildi (terminal).</summary>
    Released = 2,

    /// <summary>Rezervasyon zaman aşımına uğradı (15dk); stok iade edildi (terminal). Davranış 5c.</summary>
    Expired = 3
}
