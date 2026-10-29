using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace bmcs_app.Application.Analysis;

/// <summary>担当者別売上・粗利の1行。担当者コードが null の行は「担当者なし」。</summary>
public sealed record EmployeeSalesItem(string? EmployeeCode, string EmployeeName, decimal SalesAmount, decimal CostAmount)
{
    public decimal GrossProfit => SalesAmount - CostAmount;

    /// <summary>粗利率（%）。売上額が0なら null。</summary>
    public decimal? GrossProfitRate => SalesAmount == 0m ? null : Math.Round(GrossProfit / SalesAmount * 100m, 1);
}

/// <summary>
/// 担当者別売上・粗利の都度集計（保存しない。<c>docs/design_document.md</c> 33章）。
/// 担当者は伝票の <c>employee_code</c>。未設定の伝票は得意先の営業担当（<c>customers.sales_employee_code</c>）に計上する。
/// 売上額は税抜（<c>tax_unit=3</c> は <c>amount − tax_amount</c>）で、月次締めの売上額と同じ定義。
/// </summary>
public class EmployeeSalesQueryService(BmcsDbContext dbContext)
{
    /// <summary>指定した暦月の担当者別売上・粗利を、担当者コード順（担当者なしは末尾）に返す。</summary>
    public async Task<List<EmployeeSalesItem>> GetByMonthAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        var periodFrom = new DateOnly(year, month, 1);
        var periodTo = periodFrom.AddMonths(1).AddDays(-1);

        // 担当者・金額とも明細行の値。伝票単位の値（slip_tax_amount 等）は SUM しない（database-schema.md 2.8節）。
        var rows = await (
            from s in dbContext.Sales.AsNoTracking()
            where !s.IsDeleted && s.SlipDate >= periodFrom && s.SlipDate <= periodTo
            join c in dbContext.Customers.AsNoTracking() on s.CustomerCode equals c.CustomerCode
            group new { Sales = s.Amount - (s.TaxAmount ?? 0m), Cost = s.Quantity * s.CostPrice }
                by s.EmployeeCode ?? c.SalesEmployeeCode into g
            select new { EmployeeCode = g.Key, SalesAmount = g.Sum(x => x.Sales), CostAmount = g.Sum(x => x.Cost) })
            .ToListAsync(cancellationToken);

        var codes = rows.Where(r => r.EmployeeCode != null).Select(r => r.EmployeeCode!).ToList();
        var names = await dbContext.Employees.AsNoTracking()
            .Where(e => codes.Contains(e.EmployeeCode))
            .ToDictionaryAsync(e => e.EmployeeCode, e => e.EmployeeName, cancellationToken);

        return rows
            .OrderBy(r => r.EmployeeCode is null)
            .ThenBy(r => r.EmployeeCode)
            .Select(r => new EmployeeSalesItem(
                r.EmployeeCode,
                r.EmployeeCode is null ? "（担当者なし）" : names.GetValueOrDefault(r.EmployeeCode, r.EmployeeCode),
                r.SalesAmount,
                r.CostAmount))
            .ToList();
    }
}
