using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderHub.OrderService.Application.Abstractions.Messaging;
using OrderHub.OrderService.Application.Abstractions.Scheduling;
using OrderHub.OrderService.Application.Common.Logging;
using OrderHub.OrderService.Application.Orders.Configuration;
using OrderHub.OrderService.Domain.Orders.Events;

namespace OrderHub.OrderService.Application.Orders.EventHandlers;

/// <summary>
/// <see cref="OrderCreated"/> tüketicisi: yeni Pending sipariş için <see cref="OrderTimeoutOptions.UnpaidTimeout"/>
/// sonra çalışacak otomatik-iptal job'unu planlar (ADR-0003 hibrit ana yol). Zamanlama bir port'tur
/// (<see cref="IOrderTimeoutScheduler"/>) → handler Hangfire'ı bilmez (DIP, ADR-0002).
/// <para>
/// Schedule transient fail olursa exception <b>propagate</b> olur (yutulmaz, K5): sipariş zaten commit
/// edilmiştir, rollback olmaz (ADR-0002). Same-DB seçimi (ADR-0003) bu pencereyi neredeyse yok eder; nadir
/// kayıp sweep backstop ile kapatılır. Bu yüzden burada savunmacı try/catch <b>yoktur</b>.
/// </para>
/// </summary>
internal sealed class OrderCreatedDomainEventHandler(
    IOrderTimeoutScheduler scheduler,
    IOptions<OrderTimeoutOptions> options,
    ILogger<OrderCreatedDomainEventHandler> logger)
    : INotificationHandler<DomainEventNotification<OrderCreated>>
{
    public Task Handle(
        DomainEventNotification<OrderCreated> notification,
        CancellationToken cancellationToken)
    {
        var orderId = notification.DomainEvent.OrderId;
        var timeout = options.Value.UnpaidTimeout;

        scheduler.ScheduleCancellation(orderId, timeout);

        ApplicationLog.OrderCancellationScheduled(logger, orderId, timeout);

        return Task.CompletedTask;
    }
}
