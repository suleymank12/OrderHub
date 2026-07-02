using MassTransit;
using OrderHub.Contracts.Inventory;
using OrderHub.Contracts.Orders;
using OrderHub.Contracts.Payments;

namespace OrderHub.OrderProcessingService.IntegrationTests.Saga.Support;

/// <summary>
/// Saga'nın <b>yayımladığı</b> tüm command'leri (diğer servislerin tüketeceği: ReserveStock/ConfirmOrder/
/// ProcessPayment/MarkOrderPaid/ConfirmStockReservation/ShipOrder) gerçek broker üzerinden yakalayan tek
/// test-double consumer. Diğer servis host'ları e2e'ye dâhil edilmez; bu consumer onların yerine geçer —
/// mesajları kaydeder, cevap event'lerini (StockReserved/PaymentSucceeded/StockReservationConfirmed) testin
/// kendisi publish eder (adım adım deterministik doğrulama için). Tek consumer → tek endpoint, 6 exchange binding
/// (generic consumer isim çakışması yok).
/// </summary>
internal sealed class SagaCommandRecordingConsumer(SagaMessageRecorder recorder) :
    IConsumer<ReserveStockIntegrationEvent>,
    IConsumer<ConfirmOrderIntegrationEvent>,
    IConsumer<ProcessPaymentIntegrationEvent>,
    IConsumer<MarkOrderPaidIntegrationEvent>,
    IConsumer<ConfirmStockReservationIntegrationEvent>,
    IConsumer<ShipOrderIntegrationEvent>
{
    public Task Consume(ConsumeContext<ReserveStockIntegrationEvent> context) => Record(context.Message);
    public Task Consume(ConsumeContext<ConfirmOrderIntegrationEvent> context) => Record(context.Message);
    public Task Consume(ConsumeContext<ProcessPaymentIntegrationEvent> context) => Record(context.Message);
    public Task Consume(ConsumeContext<MarkOrderPaidIntegrationEvent> context) => Record(context.Message);
    public Task Consume(ConsumeContext<ConfirmStockReservationIntegrationEvent> context) => Record(context.Message);
    public Task Consume(ConsumeContext<ShipOrderIntegrationEvent> context) => Record(context.Message);

    private Task Record(object message)
    {
        recorder.Record(message);
        return Task.CompletedTask;
    }
}
