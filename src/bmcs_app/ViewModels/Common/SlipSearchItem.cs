using System.Globalization;
using bmcs_app.Application.Billing;
using bmcs_app.Application.Order;
using bmcs_app.Application.Receipt;
using bmcs_app.Application.Sales;
using bmcs_app.Converters;

namespace bmcs_app.ViewModels.Common;

/// <summary>伝票検索モーダルの一覧行の表示用。表示文字列の組み立ては Presentation 層の責務。</summary>
public sealed record SlipSearchItem(
    string SlipNumber,
    string SlipDateDisplay,
    string CustomerCode,
    string CustomerName,
    decimal TotalAmount,
    string StatusDisplay)
{
    private static readonly EnumDisplayConverter EnumDisplay = new();

    public static SlipSearchItem FromSalesHit(SalesSlipHit hit) => new(
        hit.SalesSlipNumber,
        hit.SlipDate.ToString("yyyy/MM/dd"),
        hit.CustomerCode,
        hit.CustomerName,
        hit.TotalAmount,
        $"{Display(hit.BillingStatus)}/{Display(hit.SettlementStatus)}");

    public static SlipSearchItem FromOrderHit(OrderSlipHit hit) => new(
        hit.OrderSlipNumber,
        hit.OrderDate.ToString("yyyy/MM/dd"),
        hit.CustomerCode,
        hit.CustomerName,
        hit.TotalAmount,
        Display(hit.OrderStatus));

    /// <summary>
    /// 宛名（都度書き換え。C-9・2026-09-10確定）が得意先名と異なる場合はキーワード検索できるよう
    /// 得意先名欄に併記する（学校のクラス・先生単位などの宛名で探せるようにするため）。
    /// </summary>
    public static SlipSearchItem FromDetailInvoiceHit(DetailInvoiceHit hit) => new(
        hit.DetailInvoiceNumber,
        hit.IssueDate.ToString("yyyy/MM/dd"),
        hit.CustomerCode,
        hit.AddresseeName == hit.CustomerName ? hit.CustomerName : $"{hit.CustomerName}（{hit.AddresseeName}）",
        hit.TotalAmount,
        Display(hit.InvoiceStatus));

    public static SlipSearchItem FromReceiptHit(ReceiptHit hit) => new(
        hit.ReceiptSlipNumber,
        hit.ReceiptDate.ToString("yyyy/MM/dd"),
        hit.CustomerCode,
        hit.CustomerName,
        hit.ReceiptAmount,
        Display(hit.AllocationStatus));

    public static SlipSearchItem FromDetailReceiptHit(DetailReceiptHit hit) => new(
        hit.DetailReceiptNumber,
        hit.ReceiptDate.ToString("yyyy/MM/dd"),
        hit.CustomerCode,
        hit.CustomerName,
        hit.ReceiptAmount,
        Display(hit.AllocationStatus));

    private static string Display(object value) =>
        (string)EnumDisplay.Convert(value, typeof(string), null, CultureInfo.CurrentCulture);
}
