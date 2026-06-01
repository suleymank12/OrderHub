using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using OrderHub.OrderService.Application.Orders.Configuration;
using OrderHub.OrderService.Domain.Orders;
using OrderHub.OrderService.IntegrationTests.Fixtures;
using OrderHub.OrderService.IntegrationTests.TestData;

namespace OrderHub.OrderService.IntegrationTests.BackgroundJobs;

/// <summary>
/// ROADMAP §2.5 kabul kriteri: ödenmeden kalan sipariş otomatik olarak Cancelled'a geçer.
/// <para>
/// Akış: POST /api/orders → OrderCreated event → <c>OrderCreatedDomainEventHandler</c> →
/// <c>HangfireOrderTimeoutScheduler.ScheduleCancellation</c> → Hangfire gecikmeli job →
/// <c>CancelUnpaidOrderJob.ExecuteAsync</c> → sipariş DB'de Cancelled.
/// </para>
/// <para>
/// Config override stratejisi: <c>UnpaidTimeout</c> <see cref="Microsoft.Extensions.Options.IOptions{T}"/>
/// ile LAZY okunur (handler çalışma anında) → <c>WithWebHostBuilder + ConfigureAppConfiguration +
/// AddInMemoryCollection</c> bu değeri başarıyla override eder. Connection string EAGER okunduğundan
/// env var ile verilir (ApiTestFactory üzerinden). <c>_shortTimeoutFactory</c> yalnızca bu
/// testte kullanılır: diğer ApiCollection testlerinin siparişleri risk altında değildir.
/// </para>
/// Token üretimi: <c>ApiTestExtensions.CreateAuthenticatedClient</c> yalnızca <c>ApiTestFactory</c>
/// alır. <c>WithWebHostBuilder</c> <c>WebApplicationFactory&lt;Program&gt;</c> döndürdüğünden
/// token üretimi <c>_baseFactory</c> servislerinden yapılır, client <c>_shortTimeoutFactory</c>
/// üzerinden oluşturulur — <c>JwtSettings:Secret</c> aynı olduğundan token geçerlidir.
/// </summary>
public sealed class OrderTimeoutAutoCancel2FlowTests : IAsyncLifetime, IDisposable
{
    private readonly ApiTestFactory _baseFactory = new();

    // UnpaidTimeout 2 saniyeye indirilmiş factory (IOptions lazy → in-memory override yeterli).
    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>? _shortTimeoutFactory;

    private const string ShortTimeoutValue = "00:00:02";
    private static readonly TimeSpan MaxWaitTime = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    public async Task InitializeAsync()
    {
        await _baseFactory.InitializeAsync();
        await _baseFactory.ResetDatabaseAsync();

        _shortTimeoutFactory = _baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"{OrderTimeoutOptions.SectionName}:UnpaidTimeout"] = ShortTimeoutValue
                })));
    }

    public async Task DisposeAsync()
    {
        if (_shortTimeoutFactory is not null)
        {
            await _shortTimeoutFactory.DisposeAsync();
        }

        await _baseFactory.DisposeAsync();
    }

    // CA1001: senkron Dispose yolu — async cleanup DisposeAsync'te yapıldı; burada ek kaynak yok.
    public void Dispose() { }

    // -------------------------------------------------------------------------
    // §2.5 kabul kriteri: tam akış — CreateOrder → bekle → Status=Cancelled
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateOrder_NotPaid_AutomaticallyCancelledAfterTimeout()
    {
        // Arrange — token _baseFactory'den (aynı secret), client _shortTimeoutFactory'den.
        var userId = Guid.NewGuid();
        var token = _baseFactory.CreateToken(userId);
        var client = CreateAuthenticatedClient(_shortTimeoutFactory!, token);

        // Act — sipariş oluştur (ödeme yapılmaz).
        var createResponse = await client.PostAsJsonAsync("/api/orders", ApiRequests.ValidCreateOrder());
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created,
            "Geçerli istekle sipariş başarıyla oluşturulmalıdır");

        var orderId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();

        // Assert — poll: Hangfire job çalışıp sipariş Cancelled olana kadar bekle.
        // Thread.Sleep YASAK (K2/flaky) → async poll.
        var status = await WaitForOrderStatusAsync(client, orderId, OrderStatus.Cancelled, MaxWaitTime);

        status.Should().Be(OrderStatus.Cancelled.ToString(),
            $"Sipariş {ShortTimeoutValue} timeout'tan sonra otomatik iptal edilmiş olmalıdır");
    }

    [Fact]
    public async Task CreateOrder_NotPaid_StatusIsPendingImmediatelyAfterCreation()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var token = _baseFactory.CreateToken(userId);
        var client = CreateAuthenticatedClient(_shortTimeoutFactory!, token);

        // Act
        var createResponse = await client.PostAsJsonAsync("/api/orders", ApiRequests.ValidCreateOrder());
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var orderId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();

        // Assert — oluşturulunca hemen Pending olmalı (job henüz tetiklenmedi).
        var orderDto = await client.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}");
        var immediateStatus = orderDto.GetProperty("status").GetString();

        immediateStatus.Should().Be(OrderStatus.Pending.ToString(),
            "Sipariş oluşturulur oluşturulmaz Pending durumunda olmalıdır");
    }

    // -------------------------------------------------------------------------
    // Yardımcı: generic WebApplicationFactory'den JWT'li client oluşturur.
    // -------------------------------------------------------------------------

    private static System.Net.Http.HttpClient CreateAuthenticatedClient(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory,
        string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // -------------------------------------------------------------------------
    // Yardımcı: status beklenen değere ulaşana kadar poll eder.
    // -------------------------------------------------------------------------

    private static async Task<string?> WaitForOrderStatusAsync(
        System.Net.Http.HttpClient client,
        Guid orderId,
        OrderStatus expectedStatus,
        TimeSpan maxWait)
    {
        var deadline = DateTime.UtcNow + maxWait;

        while (DateTime.UtcNow < deadline)
        {
            var response = await client.GetAsync($"/api/orders/{orderId}");
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var dto = await response.Content.ReadFromJsonAsync<JsonElement>();
                var currentStatus = dto.GetProperty("status").GetString();
                if (currentStatus == expectedStatus.ToString())
                {
                    return currentStatus;
                }
            }

            await Task.Delay(PollInterval);
        }

        // Deadline aşıldı — son durumu döndür (assertion dışarıda fail eder).
        var lastResponse = await client.GetAsync($"/api/orders/{orderId}");
        if (lastResponse.StatusCode != HttpStatusCode.OK)
        {
            return null;
        }

        var lastDto = await lastResponse.Content.ReadFromJsonAsync<JsonElement>();
        return lastDto.GetProperty("status").GetString();
    }
}
