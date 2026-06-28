using OrderHub.InventoryService.Domain.Stock;
using OrderHub.InventoryService.Domain.Stock.Events;
using OrderHub.InventoryService.Domain.Stock.Exceptions;

namespace OrderHub.InventoryService.UnitTests.Stock;

/// <summary>
/// <see cref="StockItem"/> aggregate davranış + invariant testleri (5b). Odak: "ayrılan ≤ mevcut" invariant'ı,
/// reserve/confirm/release durum geçişleri ve ★ idempotency (no-op'ta DUPLICATE domain event YOK — saga
/// at-least-once event tekrarına karşı). Handler/consumer testleri 5c'de.
/// </summary>
public sealed class StockItemTests
{
    private static readonly DateTime Expiry = new(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);

    private static StockItem ItemWith(int available) => StockItem.Create(Guid.NewGuid(), available);

    [Fact]
    public void Reserve_WithinAvailable_DecrementsAvailableAndRaisesStockReserved()
    {
        var item = ItemWith(10);
        var orderId = Guid.NewGuid();

        item.Reserve(orderId, 3, Expiry);

        item.AvailableQuantity.Should().Be(7);
        item.Reservations.Should().ContainSingle(r => r.OrderId == orderId && r.Quantity == 3
            && r.Status == ReservationStatus.Pending);
        item.DomainEvents.OfType<StockReserved>().Should().ContainSingle(e => e.OrderId == orderId && e.Quantity == 3);
    }

    [Fact]
    public void Reserve_QuantityExceedsAvailable_ThrowsInsufficientStockAndLeavesStateUnchanged()
    {
        var item = ItemWith(2);

        var act = () => item.Reserve(Guid.NewGuid(), 3, Expiry);

        act.Should().Throw<InsufficientStockException>()
            .Where(e => e.Requested == 3 && e.Available == 2);
        item.AvailableQuantity.Should().Be(2, "başarısız rezervasyon stoğu değiştirmemeli");
        item.Reservations.Should().BeEmpty();
    }

    [Fact]
    public void Reserve_ReservesExactlyAllAvailable_Succeeds()
    {
        var item = ItemWith(5);

        item.Reserve(Guid.NewGuid(), 5, Expiry);

        item.AvailableQuantity.Should().Be(0, "ayrılan = mevcut sınır değeri geçerli");
    }

    [Fact]
    public void Reserve_SameOrderTwice_IsIdempotent_NoDoubleReserveNoSecondEvent()
    {
        var item = ItemWith(10);
        var orderId = Guid.NewGuid();

        item.Reserve(orderId, 4, Expiry);
        item.Reserve(orderId, 4, Expiry); // ★ aynı OrderId için ReserveStock ikinci kez (saga re-delivery).

        item.AvailableQuantity.Should().Be(6, "ikinci reserve no-op → stok bir kez düşmeli");
        item.Reservations.Should().ContainSingle();
        item.DomainEvents.OfType<StockReserved>().Should().ContainSingle("idempotent reserve ikinci event raise etmez");
    }

    [Fact]
    public void ConfirmReservation_Pending_ConfirmsAndRaisesConfirmedOnce()
    {
        var item = ItemWith(10);
        var orderId = Guid.NewGuid();
        item.Reserve(orderId, 2, Expiry);

        item.ConfirmReservation(orderId);

        item.Reservations.Single().Status.Should().Be(ReservationStatus.Confirmed);
        item.AvailableQuantity.Should().Be(8, "confirm available'ı değiştirmez (reserve'de düştü)");
        // 5d-3: olay OrderId + ProductId (aggregate'ten) taşır — saga per-ürün confirm sayacı için.
        item.DomainEvents.OfType<StockReservationConfirmed>().Should()
            .ContainSingle(e => e.OrderId == orderId && e.ProductId == item.ProductId);
    }

    [Fact]
    public void ConfirmReservation_CalledTwice_IsIdempotent_RaisesConfirmedOnlyOnce()
    {
        var item = ItemWith(10);
        var orderId = Guid.NewGuid();
        item.Reserve(orderId, 2, Expiry);

        item.ConfirmReservation(orderId);
        item.ConfirmReservation(orderId); // ★ no-op (zaten Confirmed).

        item.DomainEvents.OfType<StockReservationConfirmed>().Should()
            .ContainSingle("idempotent no-op DUPLICATE domain event RAISE ETMEMELİ (refinement kanıtı)");
    }

    [Fact]
    public void ConfirmReservation_NoReservationForOrder_ThrowsReservationNotFound()
    {
        var item = ItemWith(10);

        var act = () => item.ConfirmReservation(Guid.NewGuid());

        act.Should().Throw<ReservationNotFoundException>();
    }

    [Fact]
    public void ReleaseReservation_Pending_RestoresAvailableAndRaisesReleased()
    {
        var item = ItemWith(10);
        var orderId = Guid.NewGuid();
        item.Reserve(orderId, 3, Expiry);

        item.ReleaseReservation(orderId);

        item.AvailableQuantity.Should().Be(10, "release ayrılan miktarı iade eder");
        item.Reservations.Single().Status.Should().Be(ReservationStatus.Released);
        item.DomainEvents.OfType<StockReleased>().Should().ContainSingle(e => e.Quantity == 3);
    }

    [Fact]
    public void ReleaseReservation_CalledTwice_IsIdempotent_RestoresOnceAndRaisesReleasedOnce()
    {
        var item = ItemWith(10);
        var orderId = Guid.NewGuid();
        item.Reserve(orderId, 3, Expiry);

        item.ReleaseReservation(orderId);
        item.ReleaseReservation(orderId); // ★ no-op (zaten Released).

        item.AvailableQuantity.Should().Be(10, "iade yalnız bir kez → stok ikiye katlanmaz");
        item.DomainEvents.OfType<StockReleased>().Should()
            .ContainSingle("idempotent no-op DUPLICATE StockReleased RAISE ETMEMELİ");
    }

    [Fact]
    public void ReleaseReservation_AfterConfirmed_ThrowsInvalidTransition()
    {
        var item = ItemWith(10);
        var orderId = Guid.NewGuid();
        item.Reserve(orderId, 3, Expiry);
        item.ConfirmReservation(orderId);

        var act = () => item.ReleaseReservation(orderId);

        act.Should().Throw<InvalidReservationStatusTransitionException>()
            .Where(e => e.From == ReservationStatus.Confirmed && e.To == ReservationStatus.Released);
        item.AvailableQuantity.Should().Be(7, "geçersiz release stoğu iade etmemeli");
    }

    [Fact]
    public void Reserve_NegativeOrZeroQuantity_Throws()
    {
        var item = ItemWith(10);

        var zero = () => item.Reserve(Guid.NewGuid(), 0, Expiry);
        var negative = () => item.Reserve(Guid.NewGuid(), -1, Expiry);

        zero.Should().Throw<ArgumentOutOfRangeException>();
        negative.Should().Throw<ArgumentOutOfRangeException>();
    }
}
