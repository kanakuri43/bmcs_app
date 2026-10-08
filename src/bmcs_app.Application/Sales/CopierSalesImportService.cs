using bmcs_app.Application.Billing;
using bmcs_app.Application.Closing;
using bmcs_app.Application.Common;
using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Domain.Import;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SalesEntity = bmcs_app.Domain.Entities.Sales;

namespace bmcs_app.Application.Sales;

/// <summary>プレビュー行の判定状態。</summary>
public enum CopierImportStatus
{
    /// <summary>取込可。</summary>
    Importable,
    /// <summary>同一機番・締日が取込済み（紐づく売上が有効）。</summary>
    Imported,
    /// <summary>取込不可（<see cref="CopierImportPreviewRow.Reason"/> に理由）。</summary>
    Error,
    /// <summary>税別金額が0以下のため対象外。</summary>
    Excluded,
}

/// <summary>プレビュー1行（1機番・1締日）。取込時は <see cref="Source"/> をそのまま使う。</summary>
public sealed record CopierImportPreviewRow(
    CopierCsvRow Source,
    string? CustomerCode,
    string? CustomerName,
    CopierImportStatus Status,
    string? Reason)
{
    public int LineNumber => Source.LineNumber;
    public string MachineNo => Source.MachineNo;
    public DateOnly ClosingDate => Source.ClosingDate;
    public string ModelName => Source.ModelName;
    public decimal Amount => Source.AmountExcludingTax;
    public int MergedLineCount => Source.MergedLineCount;
}

/// <summary>取込結果1行。成功なら <see cref="SalesSlipNumber"/>、失敗なら <see cref="Error"/> が非null。</summary>
public sealed record CopierImportResultRow(CopierImportPreviewRow Row, string? SalesSlipNumber, string? Error)
{
    public bool IsSuccess => SalesSlipNumber is not null;
}

/// <summary>
/// コピー機売上CSVの取込（docs/design_document.md 30章）。1機番・1締日＝売上1伝票（明細1行）。
/// 売上と取込履歴は <see cref="SalesService.CreateAsync"/> の <c>beforeSave</c> で同一の
/// SaveChanges・トランザクションに載せ、二重取込防止を売上の登録と不可分にする。
/// </summary>
public class CopierSalesImportService(
    BmcsDbContext dbContext,
    SalesService salesService,
    BillingClosedDateService billingClosedDateService,
    MonthlyClosedService monthlyClosedService,
    ICurrentEmployeeContext currentEmployeeContext,
    ILogger<CopierSalesImportService> logger)
{
    /// <summary>取込専用の汎用商品コード（暫定。docs 30-2「商品」）。</summary>
    public const string ProductCode = "COPYCHG";

    /// <summary>汎用商品が使えるか。使えなければ理由、使えれば null（画面開始時の確認用）。</summary>
    public async Task<string?> CheckProductAsync(CancellationToken cancellationToken = default)
    {
        var product = await dbContext.Products.AsNoTracking()
            .SingleOrDefaultAsync(p => p.ProductCode == ProductCode, cancellationToken);
        return product is null || product.IsDeleted
            ? $"取込用の商品「{ProductCode}」が商品マスタに登録されていない、または無効です。"
            : null;
    }

    public async Task<IReadOnlyList<CopierImportPreviewRow>> PreviewAsync(
        IReadOnlyList<CopierCsvRow> rows, CancellationToken cancellationToken = default)
    {
        var productError = await CheckProductAsync(cancellationToken);
        var machineNos = rows.Select(r => r.MachineNo).Distinct().ToList();
        var machines = await dbContext.CopierMachines.AsNoTracking()
            .Where(m => machineNos.Contains(m.MachineNo))
            .ToDictionaryAsync(m => m.MachineNo, cancellationToken);
        var customerCodes = machines.Values.Select(m => m.CustomerCode).Distinct().ToList();
        var customers = await dbContext.Customers.AsNoTracking()
            .Where(c => customerCodes.Contains(c.CustomerCode))
            .ToDictionaryAsync(c => c.CustomerCode, cancellationToken);
        var taxRates = await dbContext.TaxRateMasters.AsNoTracking().Where(t => !t.IsDeleted).ToListAsync(cancellationToken);

        var result = new List<CopierImportPreviewRow>(rows.Count);
        foreach (var row in rows)
        {
            CopierImportPreviewRow Make(CopierImportStatus status, string? reason, Customer? c = null)
                => new(row, c?.CustomerCode, c?.CustomerName, status, reason);

            Customer? customer = null;
            if (productError is not null) { result.Add(Make(CopierImportStatus.Error, productError)); continue; }
            if (!machines.TryGetValue(row.MachineNo, out var machine) || machine.IsDeleted)
            { result.Add(Make(CopierImportStatus.Error, $"機番「{row.MachineNo}」がコピー機マスタに登録されていないか、無効です。")); continue; }
            if (!customers.TryGetValue(machine.CustomerCode, out customer) || customer.IsDeleted)
            { result.Add(Make(CopierImportStatus.Error, $"得意先「{machine.CustomerCode}」が登録されていないか、無効です。")); continue; }
            if (customer.TaxUnit == TaxUnit.Line)
            { result.Add(Make(CopierImportStatus.Error, "内税明細単位の得意先は取り込めません。", customer)); continue; }
            if (row.AmountExcludingTax <= 0m)
            { result.Add(Make(CopierImportStatus.Excluded, "税別金額が0円以下のため対象外です。", customer)); continue; }
            if (TaxRateResolver.FindApplicable(taxRates, row.ClosingDate) is null)
            { result.Add(Make(CopierImportStatus.Error, $"締日（{row.ClosingDate:yyyy/MM/dd}）に適用できる税率マスタがありません。", customer)); continue; }

            var dateCheck = await billingClosedDateService.CheckAsync(customer.CustomerCode, row.ClosingDate, "売上日付", cancellationToken);
            if (!dateCheck.IsAllowed)
            { result.Add(Make(CopierImportStatus.Error, dateCheck.Reason, customer)); continue; }
            var monthlyReason = await monthlyClosedService.CheckEntryAsync(customer.CustomerCode, row.ClosingDate, "売上日付", cancellationToken);
            if (monthlyReason is not null)
            { result.Add(Make(CopierImportStatus.Error, monthlyReason, customer)); continue; }

            if (await IsAlreadyImportedAsync(row.MachineNo, row.ClosingDate, cancellationToken))
            { result.Add(Make(CopierImportStatus.Imported, "取込済みです。", customer)); continue; }

            result.Add(Make(CopierImportStatus.Importable, null, customer));
        }

        return result;
    }

    /// <summary>取込可の行だけを1行ずつ登録する。失敗行があっても残りは続行する。</summary>
    public async Task<IReadOnlyList<CopierImportResultRow>> ImportAsync(
        IReadOnlyList<CopierImportPreviewRow> previewRows, CancellationToken cancellationToken = default)
    {
        var results = new List<CopierImportResultRow>();
        foreach (var row in previewRows.Where(r => r.Status == CopierImportStatus.Importable))
        {
            try
            {
                results.Add(new(row, await ImportOneAsync(row, cancellationToken), null));
            }
            catch (Exception ex) when (ex is SalesOperationException or InvalidOperationException or DbUpdateException)
            {
                dbContext.ChangeTracker.Clear(); // 失敗した保存の追跡状態を次の行へ持ち越さない
                var error = ex is DbUpdateException && await IsAlreadyImportedAsync(row.MachineNo, row.ClosingDate, cancellationToken)
                    ? "取込済みです（他の端末で同時に取り込まれた可能性があります）。"
                    : ex.Message;
                logger.LogWarning(ex, "コピー機売上の取込に失敗しました。MachineNo={MachineNo} ClosingDate={ClosingDate}", row.MachineNo, row.ClosingDate);
                results.Add(new(row, null, error));
            }
        }

        return results;
    }

    private async Task<string> ImportOneAsync(CopierImportPreviewRow row, CancellationToken cancellationToken)
    {
        var src = row.Source;
        var machine = await dbContext.CopierMachines.AsNoTracking().SingleAsync(m => m.MachineNo == src.MachineNo, cancellationToken);
        var customer = await dbContext.Customers.AsNoTracking().SingleAsync(c => c.CustomerCode == machine.CustomerCode, cancellationToken);
        var product = await dbContext.Products.AsNoTracking().SingleAsync(p => p.ProductCode == ProductCode && !p.IsDeleted, cancellationToken);
        var taxRates = await dbContext.TaxRateMasters.AsNoTracking().Where(t => !t.IsDeleted).ToListAsync(cancellationToken);

        // 取消済み売上の履歴なら更新、無ければ INSERT。有効な売上が紐づく履歴があれば取込済み。
        var history = await dbContext.CopierImportHistories
            .SingleOrDefaultAsync(h => h.MachineNo == src.MachineNo && h.ClosingDate == src.ClosingDate, cancellationToken);
        if (history is not null && await HasActiveSalesAsync(history.SalesSlipNumber, cancellationToken))
        {
            throw new InvalidOperationException("取込済みです。");
        }

        var employeeCode = currentEmployeeContext.EmployeeCode;
        var now = DateTime.Now;
        var line = new SalesEntity
        {
            SalesSlipNumber = string.Empty, // 採番後に CreateAsync が上書きする
            LineNumber = 1,
            SlipDate = src.ClosingDate,
            CustomerCode = customer.CustomerCode,
            TaxUnit = customer.TaxUnit,
            CustomerName = customer.CustomerName,
            SlipType = SlipType.Sales,
            ProductCode = product.ProductCode,
            ProductName = product.ProductName,
            Specification = product.Specification,
            UnitName = product.UnitName,
            Quantity = 1m,
            UnitPrice = src.AmountExcludingTax,
            Amount = ConsumptionTaxCalculator.CalculateLineAmount(1m, src.AmountExcludingTax, customer.RoundingType),
            CostPrice = SalesSlipTypeRules.NormalizeCostPrice(SlipType.Sales, product.StandardCostPrice),
            TaxCategory = product.TaxCategory,
            TaxRate = TaxRateResolver.ResolveRate(taxRates, src.ClosingDate, product.TaxCategory),
            DeliveryNoteIssueCount = 0,
            BillingStatus = BillingLinkStatus.Unbilled,
            SettlementStatus = SettlementStatus.Unsettled,
            SettledAmount = 0m,
            SlipRemarks = src.SlipRemarks,
            InternalRemarks = src.InternalRemarks,
            EmployeeCode = employeeCode,
            CreatedBy = string.Empty, CreatedAt = now, UpdatedBy = string.Empty, UpdatedAt = now,
        };

        var slipNumber = await salesService.CreateAsync([line], customer.RoundingType, cancellationToken, number =>
        {
            if (history is null)
            {
                dbContext.CopierImportHistories.Add(new CopierImportHistory
                {
                    MachineNo = src.MachineNo, ClosingDate = src.ClosingDate, SalesSlipNumber = number,
                    CreatedBy = employeeCode, CreatedAt = now, UpdatedBy = employeeCode, UpdatedAt = now,
                });
            }
            else
            {
                history.SalesSlipNumber = number;
                history.UpdatedBy = employeeCode;
                history.UpdatedAt = now;
            }
        });

        logger.LogInformation("コピー機売上を取り込みました。MachineNo={MachineNo} ClosingDate={ClosingDate} SalesSlipNumber={SalesSlipNumber}",
            src.MachineNo, src.ClosingDate, slipNumber);
        return slipNumber;
    }

    private async Task<bool> IsAlreadyImportedAsync(string machineNo, DateOnly closingDate, CancellationToken cancellationToken)
    {
        var slip = await dbContext.CopierImportHistories.AsNoTracking()
            .Where(h => h.MachineNo == machineNo && h.ClosingDate == closingDate)
            .Select(h => h.SalesSlipNumber)
            .SingleOrDefaultAsync(cancellationToken);
        return slip is not null && await HasActiveSalesAsync(slip, cancellationToken);
    }

    private Task<bool> HasActiveSalesAsync(string salesSlipNumber, CancellationToken cancellationToken)
        => dbContext.Sales.AsNoTracking().AnyAsync(s => s.SalesSlipNumber == salesSlipNumber && !s.IsDeleted, cancellationToken);
}
