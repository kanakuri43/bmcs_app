using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace bmcs_app.Application.Common;

/// <summary>
/// 商品検索モーダル（TODO.md 3-2）の「過去の取引履歴から」軸のユースケース。
/// 対象は売上のみ（受注は対象外。2026-09-08 ユーザー確認済み）。
/// 010_unify_tax_unit_tables.sql の統合前は得意先の税区分（TaxUnit）によって
/// 参照する売上テーブルが3つに分かれていたが、統合後は sales 1テーブルを
/// customer_code で絞るだけで済むため、得意先マスタへの事前SELECTも不要になった。
/// 返品・値引行（TODO.md 5-4）と論理削除された行（TODO.md 5-6）は、単価の参考値として
/// ふさわしくないため除外する。
/// </summary>
public class ProductHistoryQueryService(BmcsDbContext dbContext)
{
    private const int MaxSourceRows = 500;

    public async Task<List<ProductHistoryHit>> SearchAsync(
        string customerCode,
        CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Sales
            .AsNoTracking()
            .Where(x => x.CustomerCode == customerCode && !x.IsDeleted && x.SlipType == SlipType.Sales)
            .OrderByDescending(x => x.SlipDate)
            .ThenByDescending(x => x.SalesSlipNumber)
            .Take(MaxSourceRows)
            .ToListAsync(cancellationToken);

        // 商品ごとの最新売上行だけを残す。「商品ごとの最新行」をEF Core（SQL）側で
        // 素直に組めないため、直近 MaxSourceRows 件をメモリに読み、そこで商品コード
        // ごとに重複を除く（得意先1件あたりの取引件数は限られる想定）。
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
