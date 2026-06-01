namespace OrderHub.OrderService.Domain.Orders;

/// <summary>
/// Sipariş yaşam döngüsü durumları. Faz 1'de yalnızca Pending→Confirmed ve →Cancelled
/// geçişleri uygulanır; <see cref="Paid"/> (Faz 3) ve <see cref="Shipped"/> (Faz 5)
/// sonraki fazlarda eklenecek behavior'larla doldurulur.
/// </summary>
public enum OrderStatus
{
    /// <summary>Oluşturuldu, henüz onaylanmadı (varsayılan).</summary>
    Pending = 0,

    /// <summary>Onaylandı, ödeme bekleniyor.</summary>
    Confirmed = 1,

    /// <summary>Ödendi.</summary>
    Paid = 2,

    /// <summary>Kargolandı.</summary>
    Shipped = 3,

    /// <summary>İptal edildi (terminal).</summary>
    Cancelled = 4
}
