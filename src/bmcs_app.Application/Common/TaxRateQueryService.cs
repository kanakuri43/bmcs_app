using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace bmcs_app.Application.Common;

/// <summary>
/// 税率マスタの取得（TODO.md 5-1）。売上・受注・請求の各ユースケースから使う。
/// <c>tax_rate_master</c> はごく少数行のため全件取得し、伝票日付ごとの解決は
/// Domain の <see cref="TaxRateResolver"/> に任せる（1伝票／1締め期間で1クエリで済む）。
/// </summary>
public class TaxRateQueryService(BmcsDbContext dbContext)
{
    public async Task<IReadOnlyList<TaxRateMaster>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.TaxRateMasters
            .AsNoTracking()
            .Where(m => !m.IsDeleted)
            .ToListAsync(cancellationToken);
    }

    public async Task<decimal> ResolveRateAsync(
        DateOnly slipDate, TaxCategory taxCategory, CancellationToken cancellationToken = default)
    {
        var taxRateMasters = await GetAllAsync(cancellationToken);
        return TaxRateResolver.ResolveRate(taxRateMasters, slipDate, taxCategory);
    }
}
