using Hangfire;
using OrderHub.OrderService.Application.Abstractions.Scheduling;
using OrderHub.OrderService.Application.Orders.BackgroundJobs;

namespace OrderHub.OrderService.Infrastructure.Scheduling;

/// <summary>
/// <see cref="IOrderTimeoutScheduler"/>'ın Hangfire adapter'ı: gecikmeli <see cref="CancelUnpaidOrderJob"/>
/// planlar. Hangfire bağımlılığı bu Infrastructure sınıfında izoledir (Application Hangfire'ı bilmez, DIP).
/// <c>CancellationToken.None</c> yalnızca expression yer tutucusudur; Hangfire çalışma anında gerçek bir
/// iptal token'ı enjekte eder.
/// </summary>
internal sealed class HangfireOrderTimeoutScheduler(IBackgroundJobClient backgroundJobClient)
    : IOrderTimeoutScheduler
{
    public void ScheduleCancellation(Guid orderId, TimeSpan delay) =>
        backgroundJobClient.Schedule<CancelUnpaidOrderJob>(
            job => job.ExecuteAsync(orderId, CancellationToken.None),
            delay);
}
