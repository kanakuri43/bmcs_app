using System.Linq.Expressions;
using SalesEntity = bmcs_app.Domain.Entities.Sales;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace bmcs_app.Application.Analysis;

/// <summary>得意先別・商品別売上・粗利の1行。</summary>
public sealed record SalesSummaryItem(string Code, string Name, decimal SalesAmount, decimal CostAmount)
{
    public decimal GrossProfit => SalesAmount - CostAmount;

    /// <summary>粗利率（%）。売上額が0なら null。</summary>
    public decimal? GrossProfitRate => SalesAmount == 0m ? null : Math.Round(GrossProfit / SalesAmount * 100m, 1);
}

/// <summary>
/// 得意先別・商品別売上・粗利の都度集計（保存しない。<c>docs/design_document.md</c> 34章）。
/// 売上額・原価の定義は <see cref="EmployeeSalesQueryService"/> と同じ（税抜、<c>quantity × cost_price</c>）。
/// 得意先は伝票の <c>customer_code</c>（請求集約元）単位で、請求集約先には合算しない。
/// </summary>
public class SalesSummaryQueryService(BmcsDbContext dbContext)
{
    /// <summary>指定した暦月の得意先別売上・粗利を、得意先コード順に返す。</summary>
    public async Task<List<SalesSummaryItem>> GetByCustomerAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        var rows = await SumByAsync(year, month, s => s.CustomerCode, cancellationToken);

        var codes = rows.Select(r => r.Code).ToList();
        var names = await dbContext.Customers.AsNoTracking()
            .Where(c => codes.Contains(c.CustomerCode))
            .ToDictionaryAsync(c => c.CustomerCode, c => c.CustomerName, cancellationToken);

        return rows.Select(r => new SalesSummaryItem(r.Code, names.GetValueOrDefault(r.Code, r.Code), r.SalesAmount, r.CostAmount)).ToList();
    }

    /// <summary>指定した暦月の商品別売上・粗利を、商品コード順に返す。</summary>
    public async Task<List<SalesSummaryItem>> GetByProductAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        var rows = await SumByAsync(year, month, s => s.ProductCode, cancellationToken);

        var codes = rows.Select(r => r.Code).ToList();
        var names = await dbContext.Products.AsNoTracking()
            .Where(p => codes.Contains(p.ProductCode))
            .ToDictionaryAsync(p => p.ProductCode, p => p.ProductName, cancellationToken);

        return rows.Select(r => new SalesSummaryItem(r.Code, names.GetValueOrDefault(r.Code, r.Code), r.SalesAmount, r.CostAmount)).ToList();
    }

    // 金額は明細行の値。伝票単位の値（slip_tax_amount 等）は SUM しない（database-schema.md 2.8節）。
    private async Task<List<(string Code, decimal SalesAmount, decimal CostAmount)>> SumByAsync(
        int year, int month, Expression<Func<SalesEntity, string>> key, CancellationToken cancellationToken)
    {
        var periodFrom = new DateOnly(year, month, 1);
        var periodTo = periodFrom.AddMonths(1).AddDays(-1);

        var rows = await dbContext.Sales.AsNoTracking()
            .Where(s => !s.IsDeleted && s.SlipDate >= periodFrom && s.SlipDate <= periodTo)
            .GroupBy(key)
            .Select(g => new
            {
                Code = g.Key,
                SalesAmount = g.Sum(s => s.Amount - (s.TaxAmount ?? 0m)),
                CostAmount = g.Sum(s => s.Quantity * s.CostPrice),
            })
            .OrderBy(r => r.Code)
            .ToListAsync(cancellationToken);

        return rows.Select(r => (r.Code, r.SalesAmount, r.CostAmount)).ToList();
    }
}
