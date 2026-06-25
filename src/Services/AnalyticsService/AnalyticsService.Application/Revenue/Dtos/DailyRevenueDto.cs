namespace OrderHub.AnalyticsService.Application.Revenue.Dtos;

/// <summary>
/// Bir günün gelir aggregate read-model'inin outbound temsili. <see cref="Date"/> gün hassasiyetindedir
/// (<see cref="DateOnly"/>; System.Text.Json .NET 8'de native serialize eder). Domain entity'si doğrudan
/// serialize edilmez; Mapster ile bu tipe map'lenir (outbound-only).
/// </summary>
/// <param name="Date">Gün (UTC tarih).</param>
/// <param name="TotalOrders">O gün ödenen sipariş sayısı.</param>
/// <param name="TotalRevenue">O günün toplam geliri.</param>
/// <param name="AvgOrderValue">Ortalama sipariş değeri (toplam gelir / sipariş sayısı).</param>
public sealed record DailyRevenueDto(
    DateOnly Date,
    int TotalOrders,
    decimal TotalRevenue,
    decimal AvgOrderValue);
