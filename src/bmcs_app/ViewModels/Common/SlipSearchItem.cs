using System.Globalization;
using bmcs_app.Application.Order;
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

    private static string Display(object value) =>
        (string)EnumDisplay.Convert(value, typeof(string), null, CultureInfo.CurrentCulture);
}
