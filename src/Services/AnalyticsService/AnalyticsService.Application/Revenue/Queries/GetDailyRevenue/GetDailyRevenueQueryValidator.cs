using FluentValidation;

namespace OrderHub.AnalyticsService.Application.Revenue.Queries.GetDailyRevenue;

/// <summary>
/// <see cref="GetDailyRevenueQuery"/> doğrulaması. <see cref="GetDailyRevenueQuery.From"/> &lt;=
/// <see cref="GetDailyRevenueQuery.To"/> zorunludur: ters aralık bir kontrat hatasıdır → sessizce boş liste
/// dönmek yerine 400 dönmeyi tercih ediyoruz (kontrat netliği, K5). Geçerli ama veri içermeyen aralık valid'dir
/// (handler boş liste döner).
/// </summary>
public sealed class GetDailyRevenueQueryValidator : AbstractValidator<GetDailyRevenueQuery>
{
    public GetDailyRevenueQueryValidator()
    {
        RuleFor(query => query.To)
            .GreaterThanOrEqualTo(query => query.From)
            .WithMessage("'To' date must be greater than or equal to 'From' date.");
    }
}
