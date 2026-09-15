using bmcs_app.Application.Common;
using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SalesEntity = bmcs_app.Domain.Entities.Sales;

namespace bmcs_app.Application.Sales;

/// <summary>
/// 納品書の表示データ取得と発行記録（TODO.md 10-4）。帳票のレンダリング・印刷は
/// Presentation 層（<c>src/bmcs_app/Reports/</c>）が担うため、本クラスは WPF 型を一切含まない
/// プレーンな DTO（<see cref="DeliveryNoteData"/>）を返すことに責務を絞る。
/// </summary>
public class DeliveryNoteService(
    BmcsDbContext dbContext,
    ILogger<DeliveryNoteService> logger)
{
    /// <summary>
    /// 指定した売上伝票の納品書データを組み立てる（画面表示・印刷用、保存しない）。
    /// <see cref="SalesQueryService.GetSlipAsync"/>は編集用に追跡ありで取得するため、
    /// 読み取り専用の本メソッドでは使わず、AsNoTracking で取得し直す。
    /// </summary>
    /// <exception cref="DeliveryNoteException">自社情報が未登録の場合。適格請求書としては扱わない
    /// 方針（TODO.md 10-4）でも、登録番号・自社名は全税単位で印字するため必須とする。</exception>
    public async Task<DeliveryNoteData?> GetAsync(
        string salesSlipNumber, CancellationToken cancellationToken = default)
    {
        var lines = await dbContext.Sales
            .AsNoTracking()
            .Where(s => s.SalesSlipNumber == salesSlipNumber && !s.IsDeleted)
            .OrderBy(s => s.LineNumber)
            .ToListAsync(cancellationToken);

        if (lines.Count == 0)
        {
            return null;
        }

        var header = lines[0];

        // 過去伝票の再発行に対応するため、論理削除された得意先も取得できるようにする
        // （CustomerService.GetByCodeAsync と同じ方針。IsDeleted で絞らない）。
        var customer = await dbContext.Customers
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.CustomerCode == header.CustomerCode, cancellationToken);

        var company = await dbContext.CompanyInfos
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new DeliveryNoteException("自社情報が登録されていません。マスタ管理＞自社情報から登録してください。");

        var roundingType = customer?.RoundingType ?? RoundingType.RoundHalfUp;

        var (breakdowns, taxTotal) = BuildTaxBreakdown(header.TaxUnit, lines, roundingType, salesSlipNumber);
        var taxExcludedTotal = header.TaxUnit == TaxUnit.Line
            ? lines.Sum(l => l.Amount) - taxTotal
            : lines.Sum(l => l.Amount);
        var grandTotal = taxExcludedTotal + taxTotal;

        var data = new DeliveryNoteData(
            SalesSlipNumber: header.SalesSlipNumber,
            SlipDate: header.SlipDate,
            TaxUnit: header.TaxUnit,
            CustomerName: header.CustomerName,
            CustomerPostalCode: customer?.PostalCode,
            CustomerAddress1: customer?.Address1,
            CustomerAddress2: customer?.Address2,
            Company: company,
            SlipRemarks: header.SlipRemarks,
            IssueCount: header.DeliveryNoteIssueCount,
            Lines: lines.Select(ToLine).ToList(),
            TaxBreakdowns: breakdowns,
            TaxExcludedTotal: taxExcludedTotal,
            TaxTotal: taxTotal,
            GrandTotal: grandTotal);

        return data;
    }

    /// <summary>
    /// 納品書の発行を記録する（伝票の全行の発行日時を更新し、発行回数を+1する）。
    /// 請求済・入金済であっても再発行できる（product-spec.md 軸1「再発行は可能」）ため、
    /// <c>SalesEditLockEvaluator</c>（<c>src/bmcs_app.Domain/Calculations/</c>）の編集ロックは適用しない。
    /// 発行日時・発行回数は帳簿外の記録列（伝票内容そのものではない）のため rowversion による
    /// 楽観的排他制御も掛けず、集合更新（<see cref="RelationalQueryableExtensions.ExecuteUpdateAsync"/>）
    /// で行う。<see cref="Sales.UpdatedBy"/>／<see cref="Sales.UpdatedAt"/>も更新しない
    /// （専用の発行日時列があるため、伝票内容を最後に編集した者の記録を印刷操作で上書きしない）。
    /// TODO.md 10-2の一括発行も本メソッドをそのまま再利用する。
    /// </summary>
    /// <remarks>
    /// 呼び出し元（売上入力画面）が同じ <see cref="BmcsDbContext"/> スコープで
    /// <see cref="SalesQueryService.GetSlipAsync"/>（追跡あり）を使って同じ伝票を読み込んでいる場合、
    /// 集合更新は ChangeTracker を経由しないため、追跡中エンティティの
    /// <see cref="Sales.RowVersion"/>がDBの新しい値と食い違ったままになる。SQL Server の
    /// rowversion は列の値に関わらずどのUPDATEでも進むため、これを放置すると次の保存
    /// （<c>SalesService.UpdateAsync</c>）が誤って「他のユーザーが更新しました」という
    /// 偽の競合エラーになる。そのため更新後に同一スコープの追跡エンティティを
    /// <c>ReloadAsync</c>で最新化する。
    /// </remarks>
    public async Task MarkIssuedAsync(string salesSlipNumber, CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now;

        var updatedRows = await dbContext.Sales
            .Where(s => s.SalesSlipNumber == salesSlipNumber && !s.IsDeleted)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.DeliveryNoteIssuedAt, _ => now)
                .SetProperty(s => s.DeliveryNoteIssueCount, s => (short)(s.DeliveryNoteIssueCount + 1)),
                cancellationToken);

        if (updatedRows == 0)
        {
            throw new DeliveryNoteException($"売上No. {salesSlipNumber} が見つかりません。");
        }

        // ChangeTracker.Entries は遅延実行のビューであり、ReloadAsync がトラッカーの内部状態を
        // 書き換えるため、列挙しながら reload すると「Collection was modified」で落ちる。
        // 先にリスト化してから reload する。
        var trackedEntries = dbContext.ChangeTracker.Entries<SalesEntity>()
            .Where(e => e.Entity.SalesSlipNumber == salesSlipNumber && e.State != EntityState.Detached)
            .ToList();
        foreach (var entry in trackedEntries)
        {
            await entry.ReloadAsync(cancellationToken);
        }

        logger.LogInformation("売上No. {SalesSlipNumber} の納品書発行を記録しました（{Rows}行）。", salesSlipNumber, updatedRows);
    }

    private (IReadOnlyList<TaxRateBucket> Breakdowns, decimal TaxTotal) BuildTaxBreakdown(
        TaxUnit taxUnit, List<SalesEntity> lines, RoundingType roundingType, string salesSlipNumber)
    {
        switch (taxUnit)
        {
            case TaxUnit.Invoice:
                // 請求単位: 伝票時点で税額を一切持たない（請求締めで初めて確定）。
                return ([], 0m);

            case TaxUnit.Slip:
                var buckets = ConsumptionTaxCalculator.CalculateExternalTaxBuckets(lines.Select(ToTaxLine), roundingType);
                var savedSlipTax = lines[0].SlipTaxAmount ?? 0m;
                var computedSlipTax = buckets.Sum(b => b.TaxAmount);
                if (computedSlipTax != savedSlipTax)
                {
                    logger.LogWarning(
                        "売上No. {SalesSlipNumber} の税率別内訳合計（{Computed}）が保存済みの伝票税額（{Saved}）と一致しません。",
                        salesSlipNumber, computedSlipTax, savedSlipTax);
                }

                return (buckets, savedSlipTax);

            case TaxUnit.Line:
                var internalBuckets = lines
                    .GroupBy(l => (l.TaxCategory, l.TaxRate))
                    .Select(g => new TaxRateBucket(
                        g.Key.TaxCategory,
                        g.Key.TaxRate,
                        g.Sum(l => l.Amount) - g.Sum(l => l.TaxAmount ?? 0m),
                        g.Sum(l => l.TaxAmount ?? 0m)))
                    .OrderBy(b => b.TaxCategory)
                    .ThenBy(b => b.TaxRate)
                    .ToList();
                var savedLineTax = lines.Sum(l => l.TaxAmount ?? 0m);

                return (internalBuckets, savedLineTax);

            default:
                throw new ArgumentOutOfRangeException(nameof(taxUnit), taxUnit, "未対応の税区分です。");
        }
    }

    private static TaxLine ToTaxLine(SalesEntity line) => new(line.TaxCategory, line.TaxRate, line.Amount);

    private static DeliveryNoteLine ToLine(SalesEntity line) => new(
        LineNumber: line.LineNumber,
        SlipType: line.SlipType,
        ProductCode: line.ProductCode,
        ProductName: line.ProductName,
        Specification: line.Specification,
        UnitName: line.UnitName,
        Quantity: line.Quantity,
        UnitPrice: line.UnitPrice,
        Amount: line.Amount,
        TaxCategory: line.TaxCategory,
        TaxRate: line.TaxRate,
        LineRemarks: line.LineRemarks);
}

/// <summary>納品書データの取得・発行記録に失敗した場合の業務例外。</summary>
public sealed class DeliveryNoteException(string message) : Exception(message);
