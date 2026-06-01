using OrderHub.Common.Exceptions;

namespace OrderHub.OrderService.Domain.Orders.Exceptions;

/// <summary>En az bir kalem içermeyen bir sipariş oluşturulmaya çalışıldığında fırlatılır.</summary>
public sealed class EmptyOrderException : DomainException
{
    /// <summary>Yeni bir <see cref="EmptyOrderException"/> oluşturur.</summary>
    public EmptyOrderException()
        : base("An order must contain at least one item.")
    {
    }
}
