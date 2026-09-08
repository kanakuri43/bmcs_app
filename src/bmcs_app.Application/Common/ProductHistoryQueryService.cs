using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace bmcs_app.Application.Common;

/// <summary>
/// 商品検索モーダル（TODO.md 3-2）の「過去の取引履歴から」軸のユースケース。
/// 対象は売上のみ（受注は対象外。2026-09-08 ユーザー確認済み）。
/// 得意先の税区分（<see cref="TaxUnit"/>）によって参照する売上テーブルが
/// sales_tax_unit_invoice / _slip / _line の3つに分かれるため、ここで振り分ける。
/// </summary>
public class ProductHistoryQueryService(BmcsDbContext dbContext)
{
    private const int MaxSourceRows = 500;

    public async Task<List<ProductHistoryHit>> SearchAsync(
        string customerCode,
        CancellationToken cancellationToken = default)
    {
        var customer = await dbContext.Customers
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.CustomerCode == customerCode, cancellationToken)
            ?? throw new InvalidOperationException($"得意先コード「{customerCode}」が見つかりません。");

        return customer.TaxUnit switch
        {
            TaxUnit.Invoice => await QueryAsync(dbContext.SalesTaxUnitInvoices, customerCode, cancellationToken),
            TaxUnit.Slip => await QueryAsync(dbContext.SalesTaxUnitSlips, customerCode, cancellationToken),
            TaxUnit.Line => await QueryAsync(dbContext.SalesTaxUnitLines, customerCode, cancellationToken),
            _ => throw new NotSupportedException($"未対応の税区分です。TaxUnit={customer.TaxUnit}"),
        };
    }

    /// <summary>
    /// 商品ごとの最新売上行だけを残す。「商品ごとの最新行」をEF Core（SQL）側で素直に
    /// 組めないため、直近 <see cref="MaxSourceRows"/> 件をメモリに読み、そこで
    /// 商品コードごとに重複を除く（得意先1件あたりの取引件数は限られる想定）。
    /// </summary>
    private static async Task<List<ProductHistoryHit>> QueryAsync<T>(
        IQueryable<T> source,
        string customerCode,
        CancellationToken cancellationToken)
        where T : SalesTaxUnitBase
    {
        var rows = await source
            .AsNoTracking()
            .Where(x => x.CustomerCode == customerCode)
            .OrderByDescending(x => x.SlipDate)
            .ThenByDescending(x => x.SalesSlipNumber)
            .Take(MaxSourceRows)
            .ToListAsync(cancellationToken);

        return rows
            .DistinctBy(x => x.ProductCode)
            .Select(x => new ProductHistoryHit(
                x.ProductCode,
                x.ProductName,
                x.Specification,
                x.UnitName,
                x.SlipDate,
                x.UnitPrice,
                x.TaxCategory))
            .ToList();
    }
}
