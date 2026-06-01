using Microsoft.Extensions.Logging;
using OrderHub.OrderService.Application.Orders.BackgroundJobs;
using OrderHub.OrderService.UnitTests.TestData;

namespace OrderHub.OrderService.UnitTests.Orders.BackgroundJobs;

/// <summary>
/// <see cref="LowStockAlertJob"/> davranış testi: placeholder log kaydının doğru EventId ve seviyeyle
/// atıldığını kanıtlar (ROADMAP §2.4). InventoryService Faz 5'te geleceğinden job şu an yalnızca
/// "entegre edilmedi" log'u atar; bu test hem mevcut davranışı hem de Faz 5 evrimini belgeler.
/// Hangfire bağımlılığı yok — saf Application servisi (ADR-0003).
/// </summary>
public sealed class LowStockAlertJobTests
{
    // -------------------------------------------------------------------------
    // Senaryo 1: ExecuteAsync çağrıldığında tam 1 Information log, EventId 2007
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_WhenInvoked_LogsSkippedAtInformationWithEventId2007()
    {
        // Arrange — CapturingLogger ile EventId dahil tüm log alanlarını yakala.
        var logger = new CapturingLogger<LowStockAlertJob>();
        var sut = new LowStockAlertJob(logger);

        // Act
        await sut.ExecuteAsync(CancellationToken.None);

        // Assert — tam 1 log kaydı yayılmalı.
        logger.Entries.Should().HaveCount(1,
            because: "placeholder job yalnızca tek bir 'skipped' kaydı yayımlar (ROADMAP §2.4)");

        var entry = logger.Entries[0];

        // Seviye: Information — uyarı/hata değil, beklenen operasyonel durum.
        entry.Level.Should().Be(LogLevel.Information,
            because: "Faz 5'e kadar bu bir warning senaryosu değil, planlı placeholder'dır");

        // EventId 2007 — ApplicationLog.LowStockAlertSkipped sabit ID (source-gen şablonu).
        entry.EventId.Id.Should().Be(2007,
            because: "EventId 2007 log aggregation sistemlerinde bu placeholder'ı eşsiz tanımlar; " +
                     "Faz 5'te InventoryService entegrasyonu yapılınca bu ID değişmeden devam edecek " +
                     "ya da kasıtlı olarak retire edilecek");

        // Mesaj içeriği: InventoryService referansı render edilmiş mesajda bulunmalı.
        entry.Message.Should().Contain("InventoryService",
            because: "log mesajı hangi servisin eksik olduğunu açıkça belirtmelidir (ROADMAP §2.4, Faz 5)");
    }
}
