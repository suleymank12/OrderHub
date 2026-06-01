using OrderHub.Common.Exceptions;

namespace OrderHub.OrderService.Domain.Orders.Exceptions;

/// <summary>
/// Geçersiz bir sipariş durum geçişi denendiğinde fırlatılır
/// (ör. Cancelled bir siparişi Confirm etmek).
/// </summary>
public sealed class InvalidOrderStatusTransitionException : DomainException
{
    /// <summary>Mevcut (kaynak) durum.</summary>
    public OrderStatus From { get; }

    /// <summary>Hedeflenen durum.</summary>
    public OrderStatus To { get; }

    /// <summary>Kaynak ve hedef durumla exception oluşturur.</summary>
    public InvalidOrderStatusTransitionException(OrderStatus from, OrderStatus to)
        : base($"Cannot transition order from {from} to {to}.")
    {
        From = from;
        To = to;
    }
}
