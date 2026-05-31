namespace OrderHub.OrderService.Application.ValueObjects.Dtos;

/// <summary>
/// <c>Money</c> value object'inin dış kontrat temsili. <see cref="Currency"/> string'tir (enum adı) →
/// API kontrat stabilitesi: enum'ın sayısal değeri değişse bile client kırılmaz.
/// </summary>
/// <param name="Amount">Tutar.</param>
/// <param name="Currency">Para birimi adı (ör. "TRY", "USD").</param>
public sealed record MoneyDto(decimal Amount, string Currency);
