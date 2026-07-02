using OrderHub.Common.Exceptions;

namespace OrderHub.InventoryService.Domain.Stock.Exceptions;

/// <summary>
/// Geçersiz bir rezervasyon durum geçişi denendiğinde fırlatılır (ör. zaten Confirmed bir rezervasyonu Release
/// etmek). Terminal durumlar (Confirmed/Released/Expired) yeniden farklı bir terminale geçemez → idempotency +
/// tutarlılık korunur. PaymentService'in InvalidPaymentStatusTransitionException precedent'iyle aynı.
/// </summary>
public sealed class InvalidReservationStatusTransitionException : DomainException
{
    /// <summary>Mevcut (kaynak) durum.</summary>
    public ReservationStatus From { get; }

    /// <summary>Hedeflenen durum.</summary>
    public ReservationStatus To { get; }

    /// <summary>Kaynak ve hedef durumla exception oluşturur.</summary>
    public InvalidReservationStatusTransitionException(ReservationStatus from, ReservationStatus to)
        : base($"Cannot transition reservation from {from} to {to}.")
    {
        From = from;
        To = to;
    }
}
