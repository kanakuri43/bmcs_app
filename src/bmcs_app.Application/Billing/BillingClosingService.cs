using bmcs_app.Application.Common;
using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using BillingEntity = bmcs_app.Domain.Entities.Billing;
using SalesEntity = bmcs_app.Domain.Entities.Sales;

namespace bmcs_app.Application.Billing;

/// <summary>
/// 請求締め処理のユースケース（TODO.md 6-1）。締め得意先（<c>tax_unit</c>＝請求単位／伝票単位）の
/// 期間内売上・入金を集計し、<c>billing</c>へ請求データを確定する。集計の詳細（期間の非対称、
/// 税額計算の税単位別分岐、二重締め防止）は docs/design_document.md 9章を参照。
/// </summary>
public class BillingClosingService(
    BmcsDbContext dbContext,
    SlipNumberService slipNumberService,
    ICurrentEmployeeContext currentEmployeeContext,
    ILogger<BillingClosingService> logger)
{
    /// <summary>
    /// 得意先マスタに実在する締め日区分（<c>closing_day</c>）を、締め得意先（請求単位／伝票単位）
    /// のもののみ重複なく返す。画面の締め日選択（ComboBox）用。
    /// </summary>
    public Task<List<byte>> GetClosingDayOptionsAsync(CancellationToken cancellationToken = default)
        => dbContext.Customers
            .AsNoTracking()
            .Where(c => !c.IsDeleted && (c.TaxUnit == TaxUnit.Invoice || c.TaxUnit == TaxUnit.Slip))
            .Select(c => c.ClosingDay)
            .Distinct()
            .OrderBy(d => d)
            .ToListAsync(cancellationToken);

    /// <summary>指定した締め日区分・請求日（＝締め切り日）の集計結果を読み取り専用で返す（保存しない）。</summary>
    public async Task<IReadOnlyList<BillingClosingTarget>> PreviewAsync(
        byte closingDay, DateOnly closingDate, CancellationToken cancellationToken = default)
    {
        var candidates = await BuildCandidatesAsync(closingDay, closingDate, tracking: false, cancellationToken);
        return candidates.Select(c => c.ToTarget()).ToList();
    }

    /// <summary>
    /// 同条件で再集計し、<c>billing</c>を確定登録して対象<c>sales</c>行へ紐付ける。
    /// <see cref="PreviewAsync"/>の結果は引数に取らない（プレビューと確定の間に他ユーザーが
    /// 伝票を登録しても、古い集計値で確定しないため）。
    /// </summary>
    /// <param name="closingDate">
    /// 締め切り日。売上・入金の集計対象範囲の上限（＜＝）であり、確定する<c>billing</c>の
    /// 請求日（<c>billing_date</c>）としてもそのまま使う（2026-09-11ユーザー確認。
    /// 「請求日でいつ締め切るか決まる」ため、締め切り日と請求日を別入力にしない）。
    /// </param>
    public async Task<IReadOnlyList<BillingClosingTarget>> ConfirmAsync(
        byte closingDay, DateOnly closingDate, CancellationToken cancellationToken = default)
    {
        var employeeCode = currentEmployeeContext.EmployeeCode;
        var now = DateTime.Now;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var candidates = await BuildCandidatesAsync(closingDay, closingDate, tracking: true, cancellationToken);
        var results = new List<BillingClosingTarget>();

        foreach (var candidate in candidates)
        {
            if (candidate.SkipReason is not null)
            {
                results.Add(candidate.ToTarget());
                continue;
            }

            var billingNumber = await slipNumberService.NextAsync(SlipNumberKind.Billing, cancellationToken);

            dbContext.Billings.Add(new BillingEntity
            {
                BillingNumber = billingNumber,
                CustomerCode = candidate.Customer.CustomerCode,
                TaxUnit = candidate.Customer.TaxUnit,
                CustomerName = candidate.Customer.CustomerName,
                BillingDate = closingDate,
                ClosingYearMonth = candidate.ClosingYearMonth,
                PreviousBalance = candidate.PreviousBalance,
                ReceiptAmount = candidate.ReceiptAmount,
                SalesAmount = candidate.TaxSummary.TaxableAmount,
                TaxAmount = candidate.TaxSummary.TaxAmount,
                CurrentBillingAmount = candidate.CurrentBillingAmount,
                StandardRateTaxableAmount = candidate.TaxSummary.StandardRateTaxableAmount,
                StandardRateTaxAmount = candidate.TaxSummary.StandardRateTaxAmount,
                ReducedRateTaxableAmount = candidate.TaxSummary.ReducedRateTaxableAmount,
                ReducedRateTaxAmount = candidate.TaxSummary.ReducedRateTaxAmount,
                TaxExemptAmount = candidate.TaxSummary.TaxExemptAmount,
                BillingStatus = BillingStatus.Confirmed,
                ConfirmedAt = now,
                ConfirmedBy = employeeCode,
                CreatedBy = employeeCode,
                CreatedAt = now,
                UpdatedBy = employeeCode,
                UpdatedAt = now,
            });

            foreach (var line in candidate.SalesLines)
            {
                line.BillingNumber = billingNumber;
                line.BillingStatus = BillingLinkStatus.Billed;
                line.UpdatedBy = employeeCode;
                line.UpdatedAt = now;
            }

            results.Add(candidate.ToTarget(billingNumber));
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new BillingClosingException(
                "他のユーザーが同じ売上・入金を更新しました。再読み込みしてください。", ex);
        }
        catch (DbUpdateException ex)
        {
            throw new BillingClosingException(
                "他のユーザーが同時に同じ得意先を締めました。再読み込みしてください。", ex);
        }

        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "請求締めを実行しました。締め日={ClosingDay} 請求日={ClosingDate} 確定件数={ConfirmedCount} スキップ件数={SkippedCount}",
            closingDay, closingDate,
            results.Count(r => r.SkipReason is null),
            results.Count(r => r.SkipReason is not null));

        return results;
    }

    /// <summary>
    /// 得意先ごとの集計本体。<see cref="PreviewAsync"/>／<see cref="ConfirmAsync"/>の両方から呼ぶ
    /// （金額を出す経路を1本にする）。<paramref name="tracking"/>＝falseならAsNoTrackingで読み、
    /// trueなら<see cref="ConfirmAsync"/>が明細行を更新できるよう追跡ありで読む。
    /// </summary>
    private async Task<List<BillingClosingCandidate>> BuildCandidatesAsync(
        byte closingDay, DateOnly closingDate, bool tracking, CancellationToken cancellationToken)
    {
        var closingYearMonth = $"{closingDate.Year:D4}{closingDate.Month:D2}";

        var customers = await dbContext.Customers
            .AsNoTracking()
            .Where(c => !c.IsDeleted && c.ClosingDay == closingDay
                && (c.TaxUnit == TaxUnit.Invoice || c.TaxUnit == TaxUnit.Slip))
            .OrderBy(c => c.CustomerCode)
            .ToListAsync(cancellationToken);

        var candidates = new List<BillingClosingCandidate>();

        foreach (var customer in customers)
        {
            candidates.Add(await BuildCandidateAsync(
                customer, closingDate, closingYearMonth, tracking, cancellationToken));
        }

        return candidates;
    }

    private async Task<BillingClosingCandidate> BuildCandidateAsync(
        Customer customer, DateOnly closingDate, string closingYearMonth, bool tracking,
        CancellationToken cancellationToken)
    {
        var latestConfirmed = await dbContext.Billings
            .AsNoTracking()
            .Where(b => b.CustomerCode == customer.CustomerCode
                && !b.IsDeleted
                && b.BillingStatus == BillingStatus.Confirmed)
            .OrderByDescending(b => b.ClosingYearMonth)
            .FirstOrDefaultAsync(cancellationToken);

        var previousBalance = latestConfirmed?.CurrentBillingAmount ?? 0m;

        string? skipReason = latestConfirmed is null
            ? null
            : string.CompareOrdinal(latestConfirmed.ClosingYearMonth, closingYearMonth) switch
            {
                0 => "既にこの締め年月で確定済みです。",
                > 0 => $"より新しい締め年月（{latestConfirmed.ClosingYearMonth}）が確定済みのため締められません。",
                _ => null,
            };

        if (skipReason is not null)
        {
            return new BillingClosingCandidate(
                customer, closingYearMonth, previousBalance, [], 0m, TaxSummary.Zero, skipReason);
        }

        var receiptPeriodStart = latestConfirmed?.BillingDate;

        var salesQuery = dbContext.Sales
            .Where(s => !s.IsDeleted && s.CustomerCode == customer.CustomerCode
                && s.BillingNumber == null && s.SlipDate <= closingDate);
        if (!tracking)
        {
            salesQuery = salesQuery.AsNoTracking();
        }

        var salesLines = await salesQuery
            .OrderBy(s => s.SalesSlipNumber).ThenBy(s => s.LineNumber)
            .ToListAsync(cancellationToken);

        var receiptQuery = dbContext.Receipts
            .AsNoTracking()
            .Where(r => !r.IsDeleted && r.CustomerCode == customer.CustomerCode && r.ReceiptDate <= closingDate);
        if (receiptPeriodStart is not null)
        {
            receiptQuery = receiptQuery.Where(r => r.ReceiptDate > receiptPeriodStart.Value);
        }

        var receiptLines = await receiptQuery.ToListAsync(cancellationToken);

        // receipt_amount は伝票単位の値のため、伝票番号ごとに1件へ畳んでから合計する
        // （docs/database-schema.md 2.8節「これらのカラムをSUMしてはいけない」）。
        var receiptAmount = receiptLines
            .GroupBy(r => r.ReceiptSlipNumber)
            .Sum(g => g.First().ReceiptAmount);

        var taxSummary = CalculateTaxSummary(customer, salesLines);

        if (salesLines.Count == 0 && receiptAmount == 0m && previousBalance == 0m)
        {
            skipReason = "対象データがないため締め対象外です。";
        }

        return new BillingClosingCandidate(
            customer, closingYearMonth, previousBalance, salesLines, receiptAmount, taxSummary, skipReason);
    }

    /// <summary>
    /// 税単位別の税額計算（docs/design_document.md 9-4章）。新しい計算ロジックは追加せず、
    /// 5-1で用意済みの<see cref="ConsumptionTaxCalculator"/>をそのまま使う。
    /// </summary>
    /// <exception cref="BillingClosingException">
    /// 伝票単位の得意先で、再計算した伝票税額の合計が保存済み<c>slip_tax_amount</c>の合計と
    /// 一致しない場合（端数区分は登録後不変のため、本来一致するはずのデータ異常）。
    /// </exception>
    private static TaxSummary CalculateTaxSummary(Customer customer, List<SalesEntity> salesLines)
    {
        if (customer.TaxUnit == TaxUnit.Invoice)
        {
            var taxLines = salesLines.Select(s => new TaxLine(s.TaxCategory, s.TaxRate, s.Amount));
            return ConsumptionTaxCalculator.CalculateExternalTax(taxLines, customer.RoundingType);
        }

        var perSlip = salesLines
            .GroupBy(s => s.SalesSlipNumber)
            .Select(g => g.Select(s => new TaxLine(s.TaxCategory, s.TaxRate, s.Amount)))
            .ToList();
        var summary = ConsumptionTaxCalculator.CalculateExternalTaxPerSlip(perSlip, customer.RoundingType);

        var storedTax = salesLines
            .GroupBy(s => s.SalesSlipNumber)
            .Sum(g => g.First().SlipTaxAmount ?? 0m);
        if (storedTax != summary.TaxAmount)
        {
            throw new BillingClosingException(
                $"伝票単位の税額が保存値と一致しません。CustomerCode={customer.CustomerCode} " +
                $"再計算={summary.TaxAmount} 保存値={storedTax}");
        }

        return summary;
    }

    /// <summary>得意先1件分の集計結果（DB外へは公開しない。<see cref="BillingClosingTarget"/>へ変換して返す）。</summary>
    private sealed record BillingClosingCandidate(
        Customer Customer,
        string ClosingYearMonth,
        decimal PreviousBalance,
        List<SalesEntity> SalesLines,
        decimal ReceiptAmount,
        TaxSummary TaxSummary,
        string? SkipReason)
    {
        public decimal CurrentBillingAmount => PreviousBalance - ReceiptAmount + TaxSummary.TaxableAmount + TaxSummary.TaxAmount;

        public BillingClosingTarget ToTarget(string? billingNumber = null) => new(
            Customer.CustomerCode,
            Customer.CustomerName,
            Customer.TaxUnit,
            PreviousBalance,
            ReceiptAmount,
            TaxSummary.TaxableAmount,
            TaxSummary.TaxAmount,
            CurrentBillingAmount,
            TaxSummary.StandardRateTaxableAmount,
            TaxSummary.StandardRateTaxAmount,
            TaxSummary.ReducedRateTaxableAmount,
            TaxSummary.ReducedRateTaxAmount,
            TaxSummary.TaxExemptAmount,
            SkipReason,
            billingNumber);
    }
}

/// <summary>請求締め処理の業務ルール違反（データ異常の検出）。</summary>
public sealed class BillingClosingException(string message, Exception? inner = null) : Exception(message, inner);
