using FluentValidation;

namespace OrderHub.OrderService.Application.Orders.Queries.ListOrders;

/// <summary>
/// <see cref="ListOrdersQuery"/> doğrulaması. <see cref="ListOrdersQuery.PageSize"/>'a üst sınır koymak
/// güvenlik/perf gereğidir (K5): aksi halde tek istekle tüm tablo materialize edilip bellek/DB
/// tüketilebilir (DoS vektörü). Geçersiz değerleri sessizce clamp etmek yerine 400 dönmeyi tercih
/// ediyoruz → kontrat netliği.
/// </summary>
public sealed class ListOrdersQueryValidator : AbstractValidator<ListOrdersQuery>
{
    private const int MaxPageSize = 100;

    public ListOrdersQueryValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, MaxPageSize);
    }
}
