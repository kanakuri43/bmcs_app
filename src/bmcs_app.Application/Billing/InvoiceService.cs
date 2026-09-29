using bmcs_app.Domain.Calculations;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using SalesEntity = bmcs_app.Domain.Entities.Sales;

namespace bmcs_app.Application.Billing;

/// <summary>
/// 請求書（締め得意先向け）の印刷データ取得（TODO.md 10-5）。<see cref="BillingClosingService"/>は
/// 締め処理（採番・確定）のユースケースのため、読み取り専用の印刷データ組み立てはこちらへ分離する
/// （<see cref="DetailInvoiceQueryService"/>と対称）。帳票のレンダリング・印刷は Presentation 層
/// （<c>src/bmcs_app/Reports/</c>）が担うため、本クラスは WPF 型を一切含まないプレーンな DTO
/// （<see cref="InvoiceData"/>）を返すことに責務を絞る。
/// </summary>
public class InvoiceService(BmcsDbContext dbContext)
{
    /// <summary>
    /// 指定した請求番号の請求書データを組み立てる（画面表示・印刷用、保存しない）。
    /// 確定済み・解除済みのどちらも取得対象とする（過去の参照用の印刷を禁止する理由がないため）。
    /// 締め解除後は<c>sales.billing_number</c>が<c>NULL</c>に戻るため、解除済みの請求番号を
    /// 指定した場合は明細0件・ヘッダーの確定金額のみが返る
    /// （明細請求書の取消と同じ非破壊ヘッダー方式。`docs/design_document.md` 12-1節と対称）。
    /// </summary>
    public async Task<InvoiceData?> GetByNumberAsync(
        string billingNumber, CancellationToken cancellationToken = default)
    {
        var header = await dbContext.Billings
            .AsNoTracking()
            .SingleOrDefaultAsync(b => b.BillingNumber == billingNumber && !b.IsDeleted, cancellationToken);
        if (header is null)
        {
            return null;
        }

        // 得意先コード順を最優先にする。請求集約先の請求書では、この billing_number に
        // 請求集約元（支店等）の売上も合算されるため（Phase 12-C）、得意先ごとに固めて並べる
        // ことで見出し行・小計行（InvoiceReportRowBuilder、Presentation層）が正しく組み立てられる。
        // 単独得意先ではCustomerCodeが全行同じ値のため、この並び順の変更自体は無害
        // （既存の単独得意先の帳票をバイト単位で不変に保つ）。
        var lines = await dbContext.Sales
            .AsNoTracking()
            .Where(s => s.BillingNumber == billingNumber && !s.IsDeleted)
            .OrderBy(s => s.CustomerCode).ThenBy(s => s.SlipDate).ThenBy(s => s.SalesSlipNumber).ThenBy(s => s.LineNumber)
            .ToListAsync(cancellationToken);

        // 過去伝票の再発行に対応するため、論理削除された得意先も取得できるようにする
        // （DeliveryNoteService.GetAsync と同じ方針。IsDeleted で絞らない）。
        var customer = await dbContext.Customers
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.CustomerCode == header.CustomerCode, cancellationToken);

        var company = await dbContext.CompanyInfos
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvoiceException("自社情報が登録されていません。マスタ管理＞自社情報から登録してください。");

        var bankAccounts = await dbContext.BankAccounts
            .AsNoTracking()
            .Where(b => b.IsPrintOnInvoice && !b.IsDeleted)
            .OrderBy(b => b.DisplayOrder)
            .ToListAsync(cancellationToken);

        var summary = new TaxSummary(
            header.StandardRateTaxableAmount, header.StandardRateTaxAmount,
            header.ReducedRateTaxableAmount, header.ReducedRateTaxAmount,
            header.TaxExemptAmount);
        var taxBreakdowns = ConsumptionTaxCalculator.ResolveConfirmedBuckets(summary, lines.Select(ToTaxLine));

        return new InvoiceData(
            BillingNumber: header.BillingNumber,
            BillingDate: header.BillingDate,
            ClosingYearMonth: header.ClosingYearMonth,
            TaxUnit: header.TaxUnit,
            CustomerCode: header.CustomerCode,
            CustomerName: header.CustomerName,
            CustomerPostalCode: customer?.PostalCode,
            CustomerAddress1: customer?.Address1,
            CustomerAddress2: customer?.Address2,
            Company: company,
            PrintRepresentative: customer?.PrintRepresentativeFlag ?? false,
            PrintBankAccounts: bankAccounts,
            PreviousBalance: header.PreviousBalance,
            ReceiptAmount: header.ReceiptAmount,
            SalesAmount: header.SalesAmount,
            TaxBreakdowns: taxBreakdowns,
            TaxTotal: header.TaxAmount,
            CurrentBillingAmount: header.CurrentBillingAmount,
            Lines: lines.Select(ToLine).ToList());
    }

    private static TaxLine ToTaxLine(SalesEntity line) => new(line.TaxCategory, line.TaxRate, line.Amount);

    private static InvoiceLine ToLine(SalesEntity line) => new(
        SalesSlipNumber: line.SalesSlipNumber,
        LineNumber: line.LineNumber,
        SlipDate: line.SlipDate,
        CustomerCode: line.CustomerCode,
        CustomerName: line.CustomerName,
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

/// <summary>請求書データの取得に失敗した場合の業務例外。</summary>
public sealed class InvoiceException(string message) : Exception(message);
