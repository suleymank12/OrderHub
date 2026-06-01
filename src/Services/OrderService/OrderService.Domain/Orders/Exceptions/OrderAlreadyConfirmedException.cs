using OrderHub.Common.Exceptions;

namespace OrderHub.OrderService.Domain.Orders.Exceptions;

/// <summary>Zaten onaylanmış bir sipariş yeniden onaylanmaya çalışıldığında fırlatılır.</summary>
public sealed class OrderAlreadyConfirmedException : DomainException
{
    /// <summary>Onaylanmaya çalışılan siparişin kimliği.</summary>
    public Guid OrderId { get; }

    /// <summary>Belirtilen sipariş kimliği için exception oluşturur.</summary>
    public OrderAlreadyConfirmedException(Guid orderId)
        : base($"Order {orderId} is already confirmed.")
    {
        OrderId = orderId;
    }
}
