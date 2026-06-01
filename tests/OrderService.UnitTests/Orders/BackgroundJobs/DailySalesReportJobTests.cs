using Microsoft.Extensions.Logging;
using Moq;
using OrderHub.OrderService.Application.Abstractions.Persistence;
using OrderHub.OrderService.Application.Orders.BackgroundJobs;
using OrderHub.OrderService.Application.Orders.Queries;
using OrderHub.OrderService.UnitTests.TestData;

namespace OrderHub.OrderService.UnitTests.Orders.BackgroundJobs;

/// <summary>
/// <see cref="DailySalesReportJob"/> davranış testleri.
/// <para>
/// Job saf Application servisi: Hangfire bilmez, sadece <see cref="IOrderSalesQuery"/> + ILogger bağımlılığı.
/// Davranış kanıtları:
/// <list type="number">
///   <item>Satış verisi varsa her currency için log satırı basılır (<c>DailySalesReportLine</c>).</item>
///   <item>Satış verisi yoksa boş log basılır (<c>DailySalesReportEmpty</c>); line log asla gelmez.</item>
///   <item>Sorguya geçilen range: <c>to = UtcNow.Date</c>, <c>to - from = 1 gün</c>.</item>
/// </list>
/// </para>
/// </summary>
public sealed class DailySalesReportJobTests
{
    private readonly Mock<IOrderSalesQuery> _salesQuery = new(MockBehavior.Strict);
    private readonly CapturingLogger<DailySalesReportJob> _logger = new();

    private DailySalesReportJob BuildSut() => new(_salesQuery.Object, _logger);

    // -------------------------------------------------------------------------
    // Senaryo 1: Satış verisi var → her currency için log satırı basılır
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_SalesExist_LogsLinePerCurrency()
    {
        // Arrange — 2 currency'de satış verisi.
        IReadOnlyList<DailySalesByCurrency> salesData =
        [
            new DailySalesByCurrency("TRY", OrderCount: 3, Revenue: 750m),
            new DailySalesByCurrency("USD", OrderCount: 1, Revenue: 120m)
        ];

        _salesQuery
            .Setup(q => q.GetDailySalesByCurrencyAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(salesData);

        var sut = BuildSut();

        // Act
        await sut.ExecuteAsync(CancellationToken.None);

        // Assert — her currency için en az bir Information log satırı olmalı.
        _logger.Entries
            .Where(e => e.Level == LogLevel.Information && e.Message.Contains("TRY"))
            .Should().HaveCount(1, "TRY currency için tam bir log satırı basılmalıdır");

        _logger.Entries
            .Where(e => e.Level == LogLevel.Information && e.Message.Contains("USD"))
            .Should().HaveCount(1, "USD currency için tam bir log satırı basılmalıdır");
    }

    [Fact]
    public async Task ExecuteAsync_SalesExist_DoesNotLogEmpty()
    {
        // Arrange — satış var → "no orders" logu basılmamalı.
        IReadOnlyList<DailySalesByCurrency> salesData =
        [
            new DailySalesByCurrency("TRY", OrderCount: 2, Revenue: 400m)
        ];

        _salesQuery
            .Setup(q => q.GetDailySalesByCurrencyAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(salesData);

        var sut = BuildSut();

        // Act
        await sut.ExecuteAsync(CancellationToken.None);

        // Assert — "no orders" içeren mesaj OLMAMALI.
        _logger.Entries
            .Should().NotContain(e => e.Message.Contains("no orders"),
                "Satış verisi varken DailySalesReportEmpty logu basılmamalıdır");
    }

    // -------------------------------------------------------------------------
    // Senaryo 2: Satış verisi yok → DailySalesReportEmpty, line log asla yok
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_NoSales_LogsEmpty()
    {
        // Arrange — boş liste.
        IReadOnlyList<DailySalesByCurrency> empty = [];

        _salesQuery
            .Setup(q => q.GetDailySalesByCurrencyAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(empty);

        var sut = BuildSut();

        // Act
        await sut.ExecuteAsync(CancellationToken.None);

        // Assert — "no orders" mesajı içeren Information log olmalı.
        _logger.Entries
            .Should().Contain(e => e.Level == LogLevel.Information && e.Message.Contains("no orders"),
                "Satış yoksa DailySalesReportEmpty (no orders) logu basılmalıdır");
    }

    [Fact]
    public async Task ExecuteAsync_NoSales_DoesNotLogAnyLine()
    {
        // Arrange
        IReadOnlyList<DailySalesByCurrency> empty = [];

        _salesQuery
            .Setup(q => q.GetDailySalesByCurrencyAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(empty);

        var sut = BuildSut();

        // Act
        await sut.ExecuteAsync(CancellationToken.None);

        // Assert — satır bazlı "orders=" veya "revenue=" içeren mesaj OLMAMALI (DailySalesReportLine logu).
        _logger.Entries
            .Should().NotContain(e => e.Message.Contains("orders="),
                "Satış verisi yokken DailySalesReportLine logu basılmamalıdır");
    }

    // -------------------------------------------------------------------------
    // Senaryo 3: Sorguya geçilen range doğrulaması
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_QueryRange_IsOneDayEndingAtTodayUtcMidnight()
    {
        // Arrange — range'i capture edip ilişkiyi assert ederiz (midnight flake'e karşı önlem).
        DateTime? capturedFrom = null;
        DateTime? capturedTo = null;
        var beforeCall = DateTime.UtcNow.Date; // Test çalışmadan önce bugünün gece yarısı.

        IReadOnlyList<DailySalesByCurrency> empty = [];

        _salesQuery
            .Setup(q => q.GetDailySalesByCurrencyAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .Callback<DateTime, DateTime, CancellationToken>((from, to, _) =>
            {
                capturedFrom = from;
                capturedTo = to;
            })
            .ReturnsAsync(empty);

        var sut = BuildSut();

        // Act
        await sut.ExecuteAsync(CancellationToken.None);

        var afterCall = DateTime.UtcNow.Date; // Test çalıştıktan sonra bugünün gece yarısı (muhtemelen aynı).

        // Assert — to = UtcNow.Date (gece yarısı snapshot'u); midnight değişmediyse sabit, değiştiyse ±1 gün.
        capturedTo.Should().NotBeNull("Sorguya to parametresi geçilmelidir");
        capturedFrom.Should().NotBeNull("Sorguya from parametresi geçilmelidir");

        // to, [beforeCall, afterCall.AddDays(1)] aralığında olmalı (midnight geçiş toleransı).
        capturedTo!.Value.Should().BeOnOrAfter(beforeCall,
            "to ≥ bugünün başlangıcı olmalıdır");
        capturedTo.Value.Should().BeOnOrBefore(afterCall.AddDays(1),
            "to ≤ yarının başlangıcı olmalıdır (gece yarısı geçiş toleransı)");

        // to - from = tam 1 gün (job her zaman önceki tam günü raporlar).
        var span = capturedTo.Value - capturedFrom!.Value;
        span.Should().Be(TimeSpan.FromDays(1),
            "Rapor aralığı tam 1 gün olmalıdır (önceki UTC günü)");
    }

    [Fact]
    public async Task ExecuteAsync_QueryRange_FromIsOneDayBeforeTo()
    {
        // Arrange
        DateTime? capturedFrom = null;
        DateTime? capturedTo = null;
        IReadOnlyList<DailySalesByCurrency> empty = [];

        _salesQuery
            .Setup(q => q.GetDailySalesByCurrencyAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .Callback<DateTime, DateTime, CancellationToken>((from, to, _) =>
            {
                capturedFrom = from;
                capturedTo = to;
            })
            .ReturnsAsync(empty);

        var sut = BuildSut();

        // Act
        await sut.ExecuteAsync(CancellationToken.None);

        // Assert — from = to - 1 gün (dün gece yarısı).
        capturedFrom!.Value.Should().Be(capturedTo!.Value.AddDays(-1),
            "from = to - 1 gün: job önceki UTC günü raporlar, bugünü değil");
    }
}
