using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using OrderHub.OrderService.Api.Identity;
using OrderHub.OrderService.Infrastructure.Persistence;
using OrderHub.OrderService.IntegrationTests.TestData;

namespace OrderHub.OrderService.IntegrationTests.Fixtures;

/// <summary>
/// Test yardımcıları: token'ı <see cref="ITokenGenerator"/> servisinden üretir (HTTP dev endpoint'e
/// bağımlı değil → environment-bağımsız, hızlı). Bilinen <c>userId</c> ile authenticated client üretmek
/// IDOR/sahiplik assert'lerini mümkün kılar.
/// </summary>
internal static class ApiTestExtensions
{
    public static string CreateToken(this ApiTestFactory factory, Guid userId) =>
        factory.Services.GetRequiredService<ITokenGenerator>().GenerateToken(userId).Token;

    public static HttpClient CreateAuthenticatedClient(this ApiTestFactory factory, Guid userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateToken(userId));
        return client;
    }

    /// <summary>DB'ye doğrudan (HTTP'siz) bir sipariş ekler; okuma testleri için deterministik seed.</summary>
    public static async Task<Guid> SeedOrderAsync(this ApiTestFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
        var order = OrderTestData.NewOrder();
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order.Id;
    }
}
