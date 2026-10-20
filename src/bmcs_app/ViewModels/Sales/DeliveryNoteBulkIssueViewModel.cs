using System.Collections.ObjectModel;
using System.Windows;
using bmcs_app.Application.Sales;
using bmcs_app.Domain.Entities;
using bmcs_app.Reports;
using bmcs_app.Services;
using bmcs_app.ViewModels.Common;
using bmcs_app.Views.Common;
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
    ReportPrintService reportPrintService,
    WindowService windowService) : ViewModelBase
{
    /// <summary>未発行の全件。条件変更のたびにここからメモリで <see cref="Rows"/> を絞る。</summary>
    private List<BulkIssueRowItem> _allRows = [];

    /// <summary>絞り込み後の表示行。発行の対象はこの行だけ。</summary>
    public ObservableCollection<BulkIssueRowItem> Rows { get; } = [];

    [ObservableProperty]
    public partial DateTime? DateFrom { get; set; }

    [ObservableProperty]
    public partial DateTime? DateTo { get; set; }

    /// <summary>得意先コード（1社、完全一致）。空欄は全得意先。</summary>
    [ObservableProperty]
    public partial string CustomerCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    partial void OnDateFromChanged(DateTime? value) => ApplyFilter();

    partial void OnDateToChanged(DateTime? value) => ApplyFilter();

    /// <summary>検索ダイアログで選んだ得意先の名称表示（コードを手入力で変えたら消す）。</summary>
    [ObservableProperty]
    public partial string CustomerName { get; set; } = string.Empty;

    partial void OnCustomerCodeChanged(string value)
    {
        CustomerName = string.Empty;
        ApplyFilter();
    }

    [RelayCommand]
    private void OpenCustomerSearch()
    {
        var customer = windowService.ShowDialog<CustomerSearchDialog, CustomerSearchDialogViewModel, Customer>(_ => { });
        if (customer is not null)
        {
            CustomerCode = customer.CustomerCode;
            CustomerName = customer.CustomerName;
        }
    }

    /// <summary>DBから未発行の全件を読み直し、現在の条件で絞り込む。</summary>
    [RelayCommand]
    private Task ReloadAsync() => RunBusyAsync(async () =>
    {
        try
        {
            var slips = await deliveryNoteService.GetUnissuedAsync();
            _allRows = slips.Select(s => new BulkIssueRowItem(s)).ToList();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            StatusMessage = $"検索エラー: {ex.Message}";
        }
    });

    private void ApplyFilter()
    {
        var code = CustomerCode.Trim();
        var filtered = _allRows.Where(r =>
            (DateFrom is not { } f || r.SlipDate >= DateOnly.FromDateTime(f))
            && (DateTo is not { } t || r.SlipDate <= DateOnly.FromDateTime(t))
            && (code.Length == 0 || string.Equals(r.CustomerCode, code, StringComparison.OrdinalIgnoreCase)));

        Rows.Clear();
        foreach (var row in filtered)
        {
            Rows.Add(row);
        }

        StatusMessage = $"未発行の売上 {Rows.Count}件（全{_allRows.Count}件中）";
    }

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
