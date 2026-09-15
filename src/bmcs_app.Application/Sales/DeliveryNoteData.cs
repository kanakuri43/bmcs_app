using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Application.Sales;

/// <summary>
/// 納品書1件分の表示用データ（TODO.md 10-4）。WPF 型を含まないプレーンな DTO とし、
/// レンダリング（<c>src/bmcs_app/Reports/</c>）は本レコードだけを見て組み立てる。
/// </summary>
/// <param name="Company">
/// 自社情報。登録番号（適格請求書発行事業者番号）を含む発行者情報のため必須とし、
/// 未登録の場合は <see cref="DeliveryNoteService.GetAsync"/> が例外にする（Companyがnullのまま
/// DeliveryNoteDataを組み立てることはない）。
/// </param>
/// <param name="TaxBreakdowns">
/// 税率別内訳。<see cref="TaxUnit.Invoice"/>（請求単位）は伝票時点で税額を持たないため常に空。
/// </param>
/// <param name="TaxTotal">
/// <see cref="TaxUnit.Slip"/>は保存済み<c>Sales.SlipTaxAmount</c>、
/// <see cref="TaxUnit.Line"/>は保存済み<c>Sales.TaxAmount</c>の合計、
/// <see cref="TaxUnit.Invoice"/>は常に0。
/// </param>
public sealed record DeliveryNoteData(
    string SalesSlipNumber,
    DateOnly SlipDate,
    TaxUnit TaxUnit,
    string CustomerName,
    string? CustomerPostalCode,
    string? CustomerAddress1,
    string? CustomerAddress2,
    CompanyInfo Company,
    string? SlipRemarks,
    short IssueCount,
    IReadOnlyList<DeliveryNoteLine> Lines,
    IReadOnlyList<TaxRateBucket> TaxBreakdowns,
    decimal TaxExcludedTotal,
    decimal TaxTotal,
    decimal GrandTotal);

/// <summary>納品書の明細行1行分。</summary>
public sealed record DeliveryNoteLine(
    short LineNumber,
    SlipType SlipType,
    string ProductCode,
    string ProductName,
    string? Specification,
    string? UnitName,
    decimal Quantity,
    decimal UnitPrice,
    decimal Amount,
    TaxCategory TaxCategory,
    decimal TaxRate,
    string? LineRemarks);
