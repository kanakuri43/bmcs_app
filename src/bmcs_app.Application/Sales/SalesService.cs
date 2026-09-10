using bmcs_app.Application.Common;
using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.Extensions.Logging;
using SalesEntity = bmcs_app.Domain.Entities.Sales;

namespace bmcs_app.Application.Sales;

/// <summary>
/// 売上入力（都度売上の直接入力）のユースケース（TODO.md 5-2）。新規登録のみを扱う。
/// <see cref="OrderService"/>（受注入力）と同じ構成だが、保存前に税額カラムを確定する点が異なる
/// （<c>order_slip</c> は税額列を持たないため）。
/// </summary>
public class SalesService(
    BmcsDbContext dbContext,
    SlipNumberService slipNumberService,
    ICurrentEmployeeContext currentEmployeeContext,
    ILogger<SalesService> logger)
{
    /// <summary>
    /// 売上を新規登録する。伝票番号の採番・税額カラムの確定・明細行の登録を同一トランザクションで行う
    /// （docs/architecture.md 6章）。呼び出し順は固定: エンティティ組み立て（呼び出し元）
    /// → トランザクション開始 → 採番 → 番号を明細行へ代入 → 税額確定（<see cref="SalesTaxAmountAssigner"/>）
    /// → SaveChangesAsync(1回) → コミット。
    /// </summary>
    /// <param name="lines">
    /// 保存対象の明細行。<see cref="SalesEntity.SalesSlipNumber"/> は仮値でよい（採番後に上書きする）。
    /// <see cref="SalesEntity.SlipTaxAmount"/>／<see cref="SalesEntity.TaxAmount"/> は未設定でよい
    /// （本メソッドが確定する）。すべて同一の <see cref="SalesEntity.TaxUnit"/> を持つこと。
    /// </param>
    /// <param name="roundingType">得意先マスタの端数区分。税額計算に使う。</param>
    public async Task<string> CreateAsync(
        IReadOnlyList<SalesEntity> lines, RoundingType roundingType, CancellationToken cancellationToken = default)
    {
        var employeeCode = currentEmployeeContext.EmployeeCode;
        var now = DateTime.Now;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var salesSlipNumber = await slipNumberService.NextAsync(SlipNumberKind.SalesSlip, cancellationToken);

        foreach (var line in lines)
        {
            line.SalesSlipNumber = salesSlipNumber;
            line.BillingNumber = null; // 新規登録は常に未請求（既存の請求紐付けを消す経路はここではない）
            line.CreatedBy = employeeCode;
            line.CreatedAt = now;
            line.UpdatedBy = employeeCode;
            line.UpdatedAt = now;
            line.IsDeleted = false;
        }

        SalesTaxAmountAssigner.Assign(lines, roundingType);

        dbContext.Sales.AddRange(lines);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "売上を登録しました。SalesSlipNumber={SalesSlipNumber} 行数={LineCount}",
            salesSlipNumber, lines.Count);

        return salesSlipNumber;
    }
}
