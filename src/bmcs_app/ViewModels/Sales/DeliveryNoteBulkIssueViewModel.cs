using System.Collections.ObjectModel;
using System.Windows;
using bmcs_app.Application.Sales;
using bmcs_app.Reports;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bmcs_app.ViewModels.Sales;

/// <summary>一括発行画面の1行（売上伝票単位）。</summary>
public sealed partial class BulkIssueRowItem(UnissuedDeliveryNoteSlip slip) : ObservableObject
{
    public string SalesSlipNumber { get; } = slip.SalesSlipNumber;
    public DateOnly SlipDate { get; } = slip.SlipDate;
    public string CustomerCode { get; } = slip.CustomerCode;
    public string CustomerName { get; } = slip.CustomerName;
    public decimal TotalAmount { get; } = slip.TotalAmount;

    [ObservableProperty]
    public partial bool IsSelected { get; set; } = true;

    /// <summary>発行結果（成功は「発行済」、失敗は理由）。</summary>
    [ObservableProperty]
    public partial string Result { get; set; } = string.Empty;
}

/// <summary>
/// 納品書一括発行画面（docs/design_document.md 22章）。納品書未発行の売上伝票を一覧し、選択した伝票の
/// 納品書をプレビューなしで連続印刷する。発行記録は <see cref="DeliveryNoteService.MarkIssuedAsync"/> を再利用する。
/// </summary>
public partial class DeliveryNoteBulkIssueViewModel(
    DeliveryNoteService deliveryNoteService,
    ReportPrintService reportPrintService) : ViewModelBase
{
    public ObservableCollection<BulkIssueRowItem> Rows { get; } = [];

    [ObservableProperty]
    public partial DateTime? DateFrom { get; set; }

    [ObservableProperty]
    public partial DateTime? DateTo { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [RelayCommand]
    private Task SearchAsync() => RunBusyAsync(async () =>
    {
        try
        {
            var slips = await deliveryNoteService.GetUnissuedAsync(
                DateFrom is { } f ? DateOnly.FromDateTime(f) : null,
                DateTo is { } t ? DateOnly.FromDateTime(t) : null);
            Rows.Clear();
            foreach (var slip in slips)
            {
                Rows.Add(new BulkIssueRowItem(slip));
            }

            StatusMessage = $"未発行の売上 {Rows.Count}件";
        }
        catch (Exception ex)
        {
            StatusMessage = $"検索エラー: {ex.Message}";
        }
    });

    [RelayCommand]
    private void SelectAll() => SetAll(true);

    [RelayCommand]
    private void ClearAll() => SetAll(false);

    private void SetAll(bool selected)
    {
        foreach (var row in Rows)
        {
            row.IsSelected = selected;
        }
    }

    /// <summary>
    /// 選択した伝票の納品書を連続印刷する。印刷が成功した伝票だけ発行記録を付け、1枚失敗しても続行する
    /// （コピー機売上CSV取込の連続印刷と同じ方針。プリンタは <see cref="ReportPrintService.Print"/> の設定に従う）。
    /// </summary>
    [RelayCommand]
    private async Task IssueAsync()
    {
        var targets = Rows.Where(r => r.IsSelected && r.Result != "発行済").ToList();
        if (targets.Count == 0)
        {
            StatusMessage = "発行する伝票が選択されていません。";
            return;
        }

        if (MessageBox.Show($"{targets.Count}件の納品書を印刷します。よろしいですか？", "bmcs_app",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var ok = 0;
            foreach (var row in targets)
            {
                StatusMessage = $"印刷中... 売上No. {row.SalesSlipNumber}";
                try
                {
                    var data = await deliveryNoteService.GetAsync(row.SalesSlipNumber);
                    if (data is null)
                    {
                        row.Result = "売上が見つかりません";
                        continue;
                    }

                    var result = reportPrintService.Print(
                        new DeliveryNoteDocumentBuilder(data).Build(), ReportKind.DeliveryNote, $"納品書 {row.SalesSlipNumber}");
                    if (!result.Success)
                    {
                        row.Result = result.Message ?? "印刷に失敗しました";
                        continue;
                    }

                    await deliveryNoteService.MarkIssuedAsync(row.SalesSlipNumber);
                    row.IsSelected = false;
                    row.Result = "発行済";
                    ok++;
                }
                catch (Exception ex)
                {
                    row.Result = ex.Message;
                }
            }

            StatusMessage = $"納品書の印刷が完了しました（成功 {ok}件／失敗 {targets.Count - ok}件）。";
        });
    }
}
