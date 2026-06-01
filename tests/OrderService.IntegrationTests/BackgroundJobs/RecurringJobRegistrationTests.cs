using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.DependencyInjection;
using OrderHub.OrderService.IntegrationTests.Fixtures;

namespace OrderHub.OrderService.IntegrationTests.BackgroundJobs;

/// <summary>
/// ROADMAP §2.4 + §2.6 kabul kriteri: Hangfire'da üç recurring job kayıtlı, cron'ları doğru, next execution var.
/// <para>
/// Doğrulama stratejisi: <see cref="ApiTestFactory"/> host'u tam olarak başlatır (DI + Hangfire schema +
/// <c>RecurringJobRegistrar.StartAsync</c>). <see cref="JobStorage"/> DI container'dan resolve edilir
/// → <c>GetConnection().GetRecurringJobs()</c> ile kayıtlı job listesi sorgulanır.
/// <c>JobStorage.Current</c> global singleton'a GÜVENMEYİZ (parallel test run sorunu); DI resolve güvenli.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class RecurringJobRegistrationTests(ApiTestFactory factory)
{
    // -------------------------------------------------------------------------
    // §2.6 kabul kriteri: daily-sales-report job'u kayıtlı
    // -------------------------------------------------------------------------

    [Fact]
    public void DailySalesReportJob_IsRegistered_InHangfire()
    {
        // Arrange — Hangfire storage DI'dan resolve et.
        var storage = factory.Services.GetService<JobStorage>();

        storage.Should().NotBeNull(
            "Hangfire JobStorage DI container'da kayıtlı olmalıdır");

        // Act
        using var connection = storage!.GetConnection();
        var recurringJobs = connection.GetRecurringJobs();

        // Assert — "daily-sales-report" job'u listede olmalı.
        recurringJobs.Should().Contain(j => j.Id == "daily-sales-report",
            "RecurringJobRegistrar daily-sales-report job'unu Hangfire'a kaydetmelidir (§2.6)");
    }

    [Fact]
    public void DailySalesReportJob_HasCorrectCron_0200Utc()
    {
        // Arrange
        var storage = factory.Services.GetRequiredService<JobStorage>();

        // Act
        using var connection = storage.GetConnection();
        var recurringJobs = connection.GetRecurringJobs();
        var job = recurringJobs.SingleOrDefault(j => j.Id == "daily-sales-report");

        // Assert — CRON "0 2 * * *": her gece 02:00 UTC.
        job.Should().NotBeNull("daily-sales-report job'u Hangfire'da kayıtlı olmalıdır");
        job!.Cron.Should().Be("0 2 * * *",
            "Daily sales report job'u her gece 02:00 UTC'de çalışacak şekilde yapılandırılmış olmalıdır");
    }

    [Fact]
    public void DailySalesReportJob_IsScheduledInUtc()
    {
        // Arrange
        var storage = factory.Services.GetRequiredService<JobStorage>();

        // Act
        using var connection = storage.GetConnection();
        var recurringJobs = connection.GetRecurringJobs();
        var job = recurringJobs.SingleOrDefault(j => j.Id == "daily-sales-report");

        // Assert — TimeZoneId UTC olmalı (ADR-0003 Karar 4: tüm zamanlar UTC).
        job.Should().NotBeNull();
        job!.TimeZoneId.Should().Be("UTC",
            "ADR-0003 gereği recurring job'lar UTC timezone ile kaydedilmelidir");
    }

    [Fact]
    public void DailySalesReportJob_HasNextExecution_NotNull()
    {
        // Arrange
        var storage = factory.Services.GetRequiredService<JobStorage>();

        // Act
        using var connection = storage.GetConnection();
        var recurringJobs = connection.GetRecurringJobs();
        var job = recurringJobs.SingleOrDefault(j => j.Id == "daily-sales-report");

        // Assert — §2.6: "rapor job'u Hangfire'da görünür, next run var".
        job.Should().NotBeNull();
        job!.NextExecution.Should().NotBeNull(
            "Kayıtlı recurring job'un geçerli bir cron ifadesinden hesaplanmış NextExecution'ı olmalıdır");
    }

    // -------------------------------------------------------------------------
    // §2.4 kabul kriteri: low-stock-alert job'u kayıtlı
    // -------------------------------------------------------------------------

    [Fact]
    public void LowStockAlertJob_IsRegistered_InHangfire()
    {
        // Arrange — Hangfire storage DI'dan resolve et.
        var storage = factory.Services.GetRequiredService<JobStorage>();

        // Act
        using var connection = storage.GetConnection();
        var recurringJobs = connection.GetRecurringJobs();

        // Assert — "low-stock-alert" job'u listede olmalı.
        recurringJobs.Should().Contain(j => j.Id == "low-stock-alert",
            "RecurringJobRegistrar low-stock-alert job'unu Hangfire'a kaydetmelidir (§2.4)");
    }

    [Fact]
    public void LowStockAlertJob_HasCorrectCron_HourlyUtc()
    {
        // Arrange
        var storage = factory.Services.GetRequiredService<JobStorage>();

        // Act
        using var connection = storage.GetConnection();
        var recurringJobs = connection.GetRecurringJobs();
        var job = recurringJobs.SingleOrDefault(j => j.Id == "low-stock-alert");

        // Assert — CRON "0 * * * *": her saat başı (saatlik).
        job.Should().NotBeNull("low-stock-alert job'u Hangfire'da kayıtlı olmalıdır");
        job!.Cron.Should().Be("0 * * * *",
            "Low stock alert job'u her saat başı çalışacak şekilde yapılandırılmış olmalıdır (§2.4)");
    }

    [Fact]
    public void LowStockAlertJob_IsScheduledInUtc()
    {
        // Arrange
        var storage = factory.Services.GetRequiredService<JobStorage>();

        // Act
        using var connection = storage.GetConnection();
        var recurringJobs = connection.GetRecurringJobs();
        var job = recurringJobs.SingleOrDefault(j => j.Id == "low-stock-alert");

        // Assert — TimeZoneId UTC olmalı (ADR-0003 Karar 4: tüm zamanlar UTC).
        job.Should().NotBeNull();
        job!.TimeZoneId.Should().Be("UTC",
            "ADR-0003 gereği recurring job'lar UTC timezone ile kaydedilmelidir");
    }

    [Fact]
    public void LowStockAlertJob_HasNextExecution_NotNull()
    {
        // Arrange
        var storage = factory.Services.GetRequiredService<JobStorage>();

        // Act
        using var connection = storage.GetConnection();
        var recurringJobs = connection.GetRecurringJobs();
        var job = recurringJobs.SingleOrDefault(j => j.Id == "low-stock-alert");

        // Assert — §2.4: geçerli bir cron ifadesinden hesaplanmış NextExecution olmalı.
        job.Should().NotBeNull();
        job!.NextExecution.Should().NotBeNull(
            "Kayıtlı recurring job'un geçerli bir cron ifadesinden hesaplanmış NextExecution'ı olmalıdır");
    }

    // -------------------------------------------------------------------------
    // Sweep + daily-sales-report job'ları hâlâ kayıtlı
    // (regresyon: yeni kayıt eskileri silmemeli)
    // -------------------------------------------------------------------------

    [Fact]
    public void AllThreeJobs_AreStillRegistered_AfterLowStockAlertAdded()
    {
        // Arrange
        var storage = factory.Services.GetRequiredService<JobStorage>();

        // Act
        using var connection = storage.GetConnection();
        var recurringJobs = connection.GetRecurringJobs();

        // Assert — üç job birlikte kayıtlı olmalı (RecurringJobRegistrar.StartAsync hepsini AddOrUpdate eder).
        recurringJobs.Should().Contain(j => j.Id == "sweep-unpaid-orders",
            "sweep-unpaid-orders job'u low-stock-alert eklendikten sonra da kayıtlı kalmalıdır");

        recurringJobs.Should().Contain(j => j.Id == "daily-sales-report",
            "daily-sales-report job'u low-stock-alert eklendikten sonra da kayıtlı kalmalıdır");

        recurringJobs.Should().Contain(j => j.Id == "low-stock-alert",
            "low-stock-alert job'u kayıtlı olmalıdır (§2.4)");
    }
}
