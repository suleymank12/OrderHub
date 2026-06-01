namespace OrderHub.OrderService.Application.Abstractions.Scheduling;

/// <summary>
/// Bir siparişin gecikmeli otomatik-iptal job'unu planlayan port. Application bu soyutlamayı kullanır;
/// somut zamanlama (Hangfire) Infrastructure'daki adapter'da implement edilir (DIP) → Application arka
/// plan job altyapısını bilmez. Senkron'dur: alttaki <c>IBackgroundJobClient.Schedule</c> da senkron bir
/// storage write'ıdır.
/// </summary>
public interface IOrderTimeoutScheduler
{
    /// <summary>
    /// <paramref name="delay"/> kadar sonra <paramref name="orderId"/> için iptal job'unu planlar. Job
    /// idempotenttir: sipariş artık Pending değilse no-op → çift planlama/çalışma güvenlidir.
    /// </summary>
    void ScheduleCancellation(Guid orderId, TimeSpan delay);
}
