using MassTransit;
using OrderHub.Contracts.Inventory;
using OrderHub.Contracts.Orders;
using OrderHub.Contracts.Payments;

namespace OrderHub.OrderProcessingService.IntegrationTests.Saga.Support;

/// <summary>
/// Saga'nın <b>yayımladığı</b> tüm command'leri (diğer servislerin tüketeceği: happy — ReserveStock/ConfirmOrder/
/// ProcessPayment/MarkOrderPaid/ConfirmStockReservation/ShipOrder; compensation (5e-3) — ReleaseStock/CancelOrder)
/// gerçek broker üzerinden yakalayan tek test-double consumer. Diğer servis host'ları e2e'ye dâhil edilmez; bu
/// consumer onların yerine geçer — mesajları kaydeder, cevap event'lerini (StockReserved/PaymentSucceeded/
/// StockReservationConfirmed, ve compensation: StockReservationFailed/PaymentFailed/StockReleased) testin kendisi
/// publish eder (adım adım deterministik doğrulama için). Tek consumer → tek endpoint, 8 exchange binding.
/// </summary>
internal sealed class SagaCommandRecordingConsumer(SagaMessageRecorder recorder) :
    IConsumer<ReserveStockIntegrationEvent>,
    IConsumer<ConfirmOrderIntegrationEvent>,
    IConsumer<ProcessPaymentIntegrationEvent>,
    IConsumer<MarkOrderPaidIntegrationEvent>,
    IConsumer<ConfirmStockReservationIntegrationEvent>,
    IConsumer<ShipOrderIntegrationEvent>,
    IConsumer<ReleaseStockIntegrationEvent>,
    IConsumer<CancelOrderIntegrationEvent>
{
    public Task Consume(ConsumeContext<ReserveStockIntegrationEvent> context) => Record(context.Message);
    public Task Consume(ConsumeContext<ConfirmOrderIntegrationEvent> context) => Record(context.Message);
    public Task Consume(ConsumeContext<ProcessPaymentIntegrationEvent> context) => Record(context.Message);
    public Task Consume(ConsumeContext<MarkOrderPaidIntegrationEvent> context) => Record(context.Message);
    public Task Consume(ConsumeContext<ConfirmStockReservationIntegrationEvent> context) => Record(context.Message);
    public Task Consume(ConsumeContext<ShipOrderIntegrationEvent> context) => Record(context.Message);
    public Task Consume(ConsumeContext<ReleaseStockIntegrationEvent> context) => Record(context.Message);
    public Task Consume(ConsumeContext<CancelOrderIntegrationEvent> context) => Record(context.Message);

    private Task Record(object message)
    {
        recorder.Record(message);
        return Task.CompletedTask;
    }
}
