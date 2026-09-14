using bmcs_app.Application.Common;
using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SalesEntity = bmcs_app.Domain.Entities.Sales;

namespace bmcs_app.Application.Billing;

/// <summary>
/// 明細請求書発行のユースケース（TODO.md 6-3）。都度得意先（<c>tax_unit = 3</c>内税明細単位）の
/// 未請求かつ消込完了でない売上明細行を数件選び、<c>detail_invoice</c>／
/// <c>detail_invoice_sales_line</c>へ確定する。締め請求（<see cref="BillingClosingService"/>）とは
/// 異なり、締め得意先の<c>billing_number</c>は使わず連携テーブルのみで紐付ける
/// （<c>CK_sales_billing_number_by_tax_unit</c>）。詳細は docs/design_document.md 11章。
/// </summary>
public class DetailInvoiceService(
    BmcsDbContext dbContext,
    SlipNumberService slipNumberService,
    ICurrentEmployeeContext currentEmployeeContext,
    ILogger<DetailInvoiceService> logger)
{
    /// <summary>
    /// 指定した得意先の取込候補（未請求かつ消込完了でない売上明細行）を返す（画面表示用、保存しない）。
    /// </summary>
    public async Task<IReadOnlyList<DetailInvoiceSalesLineItem>> GetCandidatesAsync(
        string customerCode, CancellationToken cancellationToken = default)
    {
        var lines = await BuildCandidateQuery(customerCode, tracking: false)
            .OrderBy(s => s.SlipDate).ThenBy(s => s.SalesSlipNumber).ThenBy(s => s.LineNumber)
            .ToListAsync(cancellationToken);

        return lines.Select(ToItem).ToList();
    }

    /// <summary>画面での表示用に、明細請求書番号で1件取得する（保存しない）。明細は連携テーブル経由で組み立てる。</summary>
    public async Task<DetailInvoiceView?> GetByNumberAsync(
        string detailInvoiceNumber, CancellationToken cancellationToken = default)
    {
        var header = await dbContext.DetailInvoices
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.DetailInvoiceNumber == detailInvoiceNumber && !d.IsDeleted, cancellationToken);
        if (header is null)
        {
            return null;
        }

        var links = await dbContext.DetailInvoiceSalesLines
            .AsNoTracking()
            .Where(l => l.DetailInvoiceNumber == detailInvoiceNumber)
            .ToListAsync(cancellationToken);

        var slipNumbers = links.Select(l => l.SalesSlipNumber).Distinct().ToList();
        var salesLines = await dbContext.Sales
            .AsNoTracking()
            .Where(s => slipNumbers.Contains(s.SalesSlipNumber))
            .ToListAsync(cancellationToken);
        var salesByKey = salesLines.ToDictionary(s => (s.SalesSlipNumber, s.LineNumber));

        var items = links
            .Select(l => salesByKey.TryGetValue((l.SalesSlipNumber, l.SalesLineNumber), out var s) ? s : null)
            .Where(s => s is not null)
            .Select(s => ToItem(s!))
            .OrderBy(i => i.SlipDate).ThenBy(i => i.SalesSlipNumber).ThenBy(i => i.SalesLineNumber)
            .ToList();

        return new DetailInvoiceView(header, items);
    }

    /// <summary>
    /// 明細請求書を発行する。<paramref name="lines"/>は画面で選択された売上明細行のキー。
    /// </summary>
    /// <exception cref="DetailInvoiceException">
    /// 明細が0件、宛名が未入力、得意先が対象外（内税明細単位以外）、指定した行が対象条件
    /// （未請求かつ消込完了でない）を満たさない、または再計算した消費税額が保存値と一致しない場合。
    /// </exception>
    public async Task<DetailInvoice> IssueAsync(
        string customerCode,
        string addresseeName,
        DateOnly issueDate,
        IReadOnlyList<(string SalesSlipNumber, short LineNumber)> lines,
        CancellationToken cancellationToken = default)
    {
        if (lines.Count == 0)
        {
            throw new DetailInvoiceException("明細を1件以上選択してください。");
        }

        if (string.IsNullOrWhiteSpace(addresseeName))
        {
            throw new DetailInvoiceException("宛名を入力してください。");
        }

        var requestedKeys = lines.Select(l => (l.SalesSlipNumber, l.LineNumber)).ToHashSet();
        if (requestedKeys.Count != lines.Count)
        {
            throw new DetailInvoiceException("同じ売上明細行が重複して指定されています。");
        }

        var employeeCode = currentEmployeeContext.EmployeeCode;
        var now = DateTime.Now;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var customer = await dbContext.Customers
            .FirstOrDefaultAsync(c => c.CustomerCode == customerCode && !c.IsDeleted, cancellationToken);
        if (customer is null)
        {
            throw new DetailInvoiceException($"得意先が見つかりません。CustomerCode={customerCode}");
        }

        if (customer.TaxUnit != TaxUnit.Line)
        {
            throw new DetailInvoiceException("内税明細単位（都度得意先）以外は明細請求書を発行できません。");
        }

        // 画面表示時の候補条件（BuildCandidateQuery）をトランザクション内で再実行し、
        // 選択された行が今も対象条件を満たすことを確認する（docs/architecture.md 9章。
        // detail_invoice_sales_line は rowversion を持たないため、この再確認とDB側のUNIQUE制約が
        // 排他制御の担保になる）。
        var candidateLines = await BuildCandidateQuery(customerCode, tracking: true).ToListAsync(cancellationToken);
        var candidateByKey = candidateLines.ToDictionary(s => (s.SalesSlipNumber, s.LineNumber));

        var salesLines = new List<SalesEntity>(requestedKeys.Count);
        foreach (var key in requestedKeys)
        {
            if (!candidateByKey.TryGetValue(key, out var line))
            {
                throw new DetailInvoiceException(
                    "対象条件を満たさない売上明細行が含まれています（既に請求済み・消込完了・削除済みのいずれか）。" +
                    $" SalesSlipNumber={key.SalesSlipNumber} LineNumber={key.LineNumber}");
            }

            salesLines.Add(line);
        }

        var taxLines = salesLines.Select(s => new TaxLine(s.TaxCategory, s.TaxRate, s.Amount));
        var taxSummary = ConsumptionTaxCalculator.CalculateInternalTaxPerLine(taxLines, customer.RoundingType);

        var storedTax = salesLines.Sum(s => s.TaxAmount ?? 0m);
        if (taxSummary.TaxAmount != storedTax)
        {
            throw new DetailInvoiceException(
                $"消費税額が保存値と一致しません。CustomerCode={customerCode} " +
                $"再計算={taxSummary.TaxAmount} 保存値={storedTax}");
        }

        var detailInvoiceNumber = await slipNumberService.NextAsync(SlipNumberKind.DetailInvoice, cancellationToken);

        var detailInvoice = new DetailInvoice
        {
            DetailInvoiceNumber = detailInvoiceNumber,
            CustomerCode = customer.CustomerCode,
            CustomerName = customer.CustomerName,
            AddresseeName = addresseeName.Trim(),
            IssueDate = issueDate,
            SalesAmount = taxSummary.TaxableAmount,
            TaxAmount = taxSummary.TaxAmount,
            TotalAmount = taxSummary.TotalAmount,
            StandardRateTaxableAmount = taxSummary.StandardRateTaxableAmount,
            StandardRateTaxAmount = taxSummary.StandardRateTaxAmount,
            ReducedRateTaxableAmount = taxSummary.ReducedRateTaxableAmount,
            ReducedRateTaxAmount = taxSummary.ReducedRateTaxAmount,
            TaxExemptAmount = taxSummary.TaxExemptAmount,
            InvoiceStatus = DetailInvoiceStatus.Issued,
            IssuedAt = now,
            IssuedBy = employeeCode,
            CreatedBy = employeeCode,
            CreatedAt = now,
            UpdatedBy = employeeCode,
            UpdatedAt = now,
        };
        dbContext.DetailInvoices.Add(detailInvoice);

        foreach (var line in salesLines)
        {
            dbContext.DetailInvoiceSalesLines.Add(new DetailInvoiceSalesLine
            {
                DetailInvoiceNumber = detailInvoiceNumber,
                SalesSlipNumber = line.SalesSlipNumber,
                SalesLineNumber = line.LineNumber,
                CreatedBy = employeeCode,
                CreatedAt = now,
                UpdatedBy = employeeCode,
                UpdatedAt = now,
            });

            // tax_unit=3 の締め請求データ（billing_number）は常にNULLのまま
            // （CK_sales_billing_number_by_tax_unit）。連携テーブルのみで紐付ける。
            line.BillingStatus = BillingLinkStatus.Billed;
            line.UpdatedBy = employeeCode;
            line.UpdatedAt = now;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            throw new DetailInvoiceException(
                "他のユーザーが同じ売上明細行を請求済みにしました。再読み込みしてください。", ex);
        }

        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "明細請求書を発行しました。DetailInvoiceNumber={DetailInvoiceNumber} CustomerCode={CustomerCode} 明細行数={LineCount}",
            detailInvoiceNumber, customerCode, salesLines.Count);

        return detailInvoice;
    }

    /// <summary>
    /// 明細請求の対象条件（共通業務ルール2・product-spec.md）: 内税明細単位・未削除・消込完了でない・
    /// どの明細請求書にも連携していない。<see cref="GetCandidatesAsync"/>（表示）と
    /// <see cref="IssueAsync"/>（発行時の再確認）の両方から使い、条件を1本にする。
    /// </summary>
    private IQueryable<SalesEntity> BuildCandidateQuery(string customerCode, bool tracking)
    {
        var query = dbContext.Sales
            .Where(s => s.CustomerCode == customerCode
                && s.TaxUnit == TaxUnit.Line
                && !s.IsDeleted
                && s.SettlementStatus != SettlementStatus.FullySettled
                && !dbContext.DetailInvoiceSalesLines.Any(l =>
                    l.SalesSlipNumber == s.SalesSlipNumber && l.SalesLineNumber == s.LineNumber));

        return tracking ? query : query.AsNoTracking();
    }

    private static DetailInvoiceSalesLineItem ToItem(SalesEntity s) => new(
        s.SalesSlipNumber,
        s.LineNumber,
        s.SlipDate,
        s.SlipType,
        s.ProductCode,
        s.ProductName,
        s.Quantity,
        s.UnitPrice,
        s.Amount,
        s.TaxAmount ?? 0m,
        s.TaxCategory,
        s.TaxRate,
        s.LineRemarks);
}

/// <summary>明細請求書発行処理の業務ルール違反。</summary>
public sealed class DetailInvoiceException(string message, Exception? inner = null) : Exception(message, inner);
