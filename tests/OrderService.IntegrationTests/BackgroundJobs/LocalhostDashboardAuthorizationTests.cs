using System.Net;
using OrderHub.OrderService.Api.BackgroundJobs;

namespace OrderHub.OrderService.IntegrationTests.BackgroundJobs;

/// <summary>
/// <see cref="LocalhostDashboardAuthorization.IsLoopback"/> saf mantık testleri.
/// Hangfire test harness veya TestServer gerektirmez — internal statik metod doğrudan çağrılır.
/// <para>
/// <b>Neden IntegrationTests projesi?</b> <c>InternalsVisibleTo</c> yalnızca
/// <c>OrderHub.OrderService.IntegrationTests</c> assembly'sine açık (Api.csproj §9).
/// UnitTests'e Api project referansı eklemek mimari kokusu yaratır (web API → unit test bağımlılığı).
/// Bu dosyada Testcontainer spin-up'ı YOK → hız piramidi korunur, test saf mantık testi olarak davranır.
/// </para>
/// </summary>
public sealed class LocalhostDashboardAuthorizationTests
{
    [Theory]
    [InlineData("127.0.0.1", true)]   // IPv4 loopback → erişim verilir
    [InlineData("::1", true)]          // IPv6 loopback → erişim verilir
    [InlineData("203.0.113.5", false)] // TEST-NET-3 public IPv4 → erişim reddedilir
    [InlineData("2001:db8::1", false)] // Documentation public IPv6 → erişim reddedilir
    [InlineData("192.168.1.1", false)] // Private ama loopback değil → erişim reddedilir
    [InlineData("10.0.0.1", false)]    // Private ama loopback değil → erişim reddedilir
    public void IsLoopback_VariousAddresses_ReturnsExpectedResult(string addressString, bool expected)
    {
        var address = IPAddress.Parse(addressString);

        var result = LocalhostDashboardAuthorization.IsLoopback(address);

        result.Should().Be(expected);
    }

    [Fact]
    public void IsLoopback_NullAddress_ReturnsFalse()
    {
        // K3 fail-closed: RemoteIpAddress null ise erişim reddedilir (TestServer default davranışı).
        var result = LocalhostDashboardAuthorization.IsLoopback(null);

        result.Should().BeFalse();
    }
}
