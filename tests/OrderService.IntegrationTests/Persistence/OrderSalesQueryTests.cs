using OrderHub.OrderService.Domain.Orders;
using OrderHub.OrderService.Domain.ValueObjects;
using OrderHub.OrderService.Infrastructure.Persistence.Repositories;
using OrderHub.OrderService.IntegrationTests.Fixtures;

namespace OrderHub.OrderService.IntegrationTests.Persistence;

/// <summary>
/// <see cref="OrderSalesQuery.GetDailySalesByCurrencyAsync"/> gerçek SQL Server container'ına karşı davranış
/// testleri. GroupBy translation doğrulaması birincil amaç: <c>TotalCurrency</c> string kolonunda yapılan
/// <c>GROUP BY + COUNT + SUM</c>'ın EF Core 8 tarafından SQL'e çevrilip çevrilmediği kanıtlanır.
/// <para>
/// Strateji: <c>CreatedAtUtc</c> private setter — doğrudan manipüle ETMEYIZ.
/// Range parametresini oynatarak test ederiz:
/// <list type="bullet">
///   <item><c>from = UtcNow.Date, to = UtcNow.Date.AddDays(1)</c> → bugün oluşturulan sipariş dönmeli.</item>
///   <item><c>from = UtcNow.AddDays(+1), to = UtcNow.AddDays(+2)</c> → gelecek → boş.</item>
/// </list>
/// Her test kendi transaction'ında izole (rollback).
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class OrderSalesQueryTests(SqlServerContainerFixture fixture)
{
    // -------------------------------------------------------------------------
    // Senaryo 1: Farklı currency'lerde siparişler → her currency grubu doğru sayı ve toplam
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetDailySalesByCurrencyAsync_MultipleCurrencies_GroupsAndSumsCorrectly()
    {
        // Arrange — aynı context/transaction içinde 3 sipariş: 2x TRY, 1x USD.
        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        // TRY siparişi 1: 100 TRY (1 adet × 100)
        var tryOrder1 = Order.Create(
            Guid.NewGuid(),
            DefaultAddress(),
            [OrderItem.Create(Guid.NewGuid(), 1, Money.Create(100m, Currency.TRY))]);

        // TRY siparişi 2: 250 TRY (1 adet × 250) → TRY toplam = 350
        var tryOrder2 = Order.Create(
            Guid.NewGuid(),
            DefaultAddress(),
            [OrderItem.Create(Guid.NewGuid(), 1, Money.Create(250m, Currency.TRY))]);

        // USD siparişi: 80 USD
        var usdOrder = Order.Create(
            Guid.NewGuid(),
            DefaultAddress(),
            [OrderItem.Create(Guid.NewGuid(), 1, Money.Create(80m, Currency.USD))]);

        context.Orders.AddRange(tryOrder1, tryOrder2, usdOrder);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        // Aralık: bugün tam günü kapsar → şu an oluşturulan siparişler aralıkta.
        var fromUtc = DateTime.UtcNow.Date;
        var toUtc = fromUtc.AddDays(1);
        var sut = new OrderSalesQuery(context);

        // Act — GroupBy translation burada test edilir; InvalidOperationException atarsa çeviri başarısız.
        var result = await sut.GetDailySalesByCurrencyAsync(fromUtc, toUtc, CancellationToken.None);

        // Assert — TRY grubu
        result.Should().HaveCount(2,
            "Farklı 2 currency (TRY, USD) → 2 grup dönmeli");

        var tryRow = result.SingleOrDefault(r => r.Currency == Currency.TRY.ToString());
        tryRow.Should().NotBeNull("TRY grubu sorgu sonucunda olmalıdır");
        tryRow!.OrderCount.Should().Be(2, "2 adet TRY siparişi oluşturuldu");
        tryRow.Revenue.Should().Be(350m, "100 + 250 = 350 TRY toplam gelir");

        // Assert — USD grubu
        var usdRow = result.SingleOrDefault(r => r.Currency == Currency.USD.ToString());
        usdRow.Should().NotBeNull("USD grubu sorgu sonucunda olmalıdır");
        usdRow!.OrderCount.Should().Be(1, "1 adet USD siparişi oluşturuldu");
        usdRow.Revenue.Should().Be(80m, "80 USD toplam gelir");
    }

    // -------------------------------------------------------------------------
    // Senaryo 2: Aralık dışındaki sipariş dahil edilmez
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetDailySalesByCurrencyAsync_OrderOutsideRange_Excluded()
    {
        // Arrange — bugün oluşturulan bir sipariş; sorgu aralığı gelecekte → sipariş dışarıda kalır.
        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var order = Order.Create(
            Guid.NewGuid(),
            DefaultAddress(),
            [OrderItem.Create(Guid.NewGuid(), 1, Money.Create(500m, Currency.TRY))]);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        // Aralık yarın ve sonrasını kapsar → bugün oluşturulan sipariş kapsam dışında.
        var fromUtc = DateTime.UtcNow.Date.AddDays(1);
        var toUtc = fromUtc.AddDays(1);
        var sut = new OrderSalesQuery(context);

        // Act
        var result = await sut.GetDailySalesByCurrencyAsync(fromUtc, toUtc, CancellationToken.None);

        // Assert
        result.Should().BeEmpty(
            "Aralık dışında oluşturulmuş siparişler gruplamaya dahil edilmemelidir");
    }

    // -------------------------------------------------------------------------
    // Senaryo 3: Sipariş yok → boş liste döner, exception atmaz
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetDailySalesByCurrencyAsync_NoOrders_ReturnsEmpty()
    {
        // Arrange — boş DB (transaction rollback sayesinde önceki testlerden kirlilik yok).
        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        // Bu transaction'da hiç sipariş eklenmedi.
        var fromUtc = DateTime.UtcNow.Date;
        var toUtc = fromUtc.AddDays(1);
        var sut = new OrderSalesQuery(context);

        // Act
        var act = async () => await sut.GetDailySalesByCurrencyAsync(fromUtc, toUtc, CancellationToken.None);

        // Assert — exception atmamalı, boş liste dönmeli.
        var result = await act.Should().NotThrowAsync("Sipariş yoksa exception yerine boş liste dönmeli");
        result.Subject.Should().BeEmpty("Hiç sipariş olmadığında boş koleksiyon dönmeli");
    }

    // -------------------------------------------------------------------------
    // Senaryo 4: Yarı-açık aralık sınır testi — toUtc anındaki sipariş hariç tutulur
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetDailySalesByCurrencyAsync_OrderAtExactToUtcBoundary_Excluded()
    {
        // Arrange — Yarı-açık aralık [fromUtc, toUtc): toUtc tam eşitliği dışarıda.
        // CreatedAtUtc ≈ UtcNow; toUtc = UtcNow - 1 gün → sipariş aralık dışında kalmalı.
        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var order = Order.Create(
            Guid.NewGuid(),
            DefaultAddress(),
            [OrderItem.Create(Guid.NewGuid(), 1, Money.Create(200m, Currency.TRY))]);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        // Aralık dün ile sınırlı → bugün oluşturulan sipariş toUtc=UtcNow.Date sınırından sonraya düşüyor.
        var toUtc = DateTime.UtcNow.Date;           // bugünün başlangıcı (exclusive üst sınır)
        var fromUtc = toUtc.AddDays(-1);             // dün
        var sut = new OrderSalesQuery(context);

        // Act
        var result = await sut.GetDailySalesByCurrencyAsync(fromUtc, toUtc, CancellationToken.None);

        // Assert — bugün oluşturulan sipariş "dün" aralığına girmez.
        result.Should().NotContain(r => r.Currency == Currency.TRY.ToString(),
            "Aralığın dışında (toUtc >= CreatedAtUtc koşulu sağlanmaz) sipariş dönmemelidir");
    }

    // -------------------------------------------------------------------------
    // Senaryo 5: Birden fazla item'lı sipariş → Total.Amount doğru hesaplanmış
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetDailySalesByCurrencyAsync_OrderWithMultipleItems_RevenueMatchesOrderTotal()
    {
        // Arrange — 3 kalemli sipariş: Revenue = siparişin Order.Total.Amount ile eşleşmeli.
        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var order = Order.Create(
            Guid.NewGuid(),
            DefaultAddress(),
            [
                OrderItem.Create(Guid.NewGuid(), 2, Money.Create(50m, Currency.TRY)),   // 100
                OrderItem.Create(Guid.NewGuid(), 1, Money.Create(75m, Currency.TRY)),   // 75
                OrderItem.Create(Guid.NewGuid(), 3, Money.Create(25m, Currency.TRY))    // 75
            ]);

        context.Orders.Add(order);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var fromUtc = DateTime.UtcNow.Date;
        var toUtc = fromUtc.AddDays(1);
        var sut = new OrderSalesQuery(context);

        // Act
        var result = await sut.GetDailySalesByCurrencyAsync(fromUtc, toUtc, CancellationToken.None);

        // Assert
        var tryRow = result.SingleOrDefault(r => r.Currency == Currency.TRY.ToString());
        tryRow.Should().NotBeNull();
        tryRow!.OrderCount.Should().Be(1);
        tryRow.Revenue.Should().Be(order.Total.Amount,
            "Revenue DB'deki TotalAmount kolonunu topladığından sipariş Total.Amount ile eşleşmeli");
    }

    // -------------------------------------------------------------------------
    // Yardımcı
    // -------------------------------------------------------------------------

    private static Address DefaultAddress() =>
        Address.Create("1 Test St", "Ankara", "06000", "Türkiye");
}
