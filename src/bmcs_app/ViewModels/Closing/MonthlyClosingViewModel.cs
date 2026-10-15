using System.Collections.ObjectModel;
using System.Windows;
using bmcs_app.Application.Closing;
using bmcs_app.Reports;
using bmcs_app.Services;
using bmcs_app.ViewModels.Common;
using bmcs_app.Views.Closing;
using bmcs_app.Views.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bmcs_app.ViewModels.Closing;

/// <summary>
/// 月次締め処理画面。構成は請求締め処理画面（<c>BillingClosingViewModel</c>）と同じで、
/// 年を4桁で入力し、その年に月次締め済みの年月をコンボボックスから選ぶ。
/// 一覧は選んだ年月の確定済み <c>monthly_closings</c>（解除済みを除く）で、年月を変えると自動で再取得する。
/// 締め確定は月次締め確定ダイアログ（<see cref="MonthlyClosingConfirmDialogViewModel"/>）で行い、全得意先を対象とする。
/// 売掛金残高一覧表は、請求締め処理の印刷と違って行選択を持たず、表示中の年月の一覧を全件印刷する
/// （<see cref="PrintCommand"/>）。
/// </summary>
public partial class MonthlyClosingViewModel(
    MonthlyClosingService monthlyClosingService,
    MonthlyClosingQueryService monthlyClosingQueryService,
    WindowService windowService) : ViewModelBase
{
    /// <summary>選択中の年（4桁入力、初期値は今年）。</summary>
    [ObservableProperty]
    public partial string YearText { get; set; } = DateTime.Today.Year.ToString();

    /// <summary>選択中の年に月次締め済みの年月（コンボボックスの選択肢）。</summary>
    public ObservableCollection<YearMonthOption> MonthOptions { get; } = [];

    /// <summary>集計年月（暦月）。初期値は今月に一番近い月。該当月が無い年は null。</summary>
    [ObservableProperty]
    public partial YearMonthOption? SelectedMonth { get; set; }

    public ObservableCollection<MonthlyClosingListItem> Results { get; } = [];

    [ObservableProperty]
    public partial decimal TotalClosingBalance { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    /// <summary>画面表示時に今年の月次締め済み年月を読み込み、今月に一番近い月の一覧を表示する。</summary>
    [RelayCommand]
    private Task LoadAsync() => ReloadMonthsAsync();

    partial void OnYearTextChanged(string value)
    {
        if (value.Length == 4 && int.TryParse(value, out var year) && year >= 1900)
        {
            _ = ReloadMonthsAsync();
        }
    }

    /// <summary>
    /// 選択中の年の月次締め済み年月を読み込み直し、<paramref name="preferred"/>（無ければ今月に一番近い月）を
    /// 選択して一覧を再取得する。処理中状態の外から呼ぶこと（処理中は再取得要求が無視されるため）。
    /// </summary>
    private async Task ReloadMonthsAsync(YearMonthOption? preferred = null)
    {
        if (!int.TryParse(YearText, out var year) || YearText.Length != 4)
        {
            StatusMessage = "年は4桁で入力してください。";
            return;
        }

        try
        {
            var months = (await monthlyClosingQueryService.GetClosedMonthsAsync(year)).Select(m => new YearMonthOption(year, m)).ToList();

            MonthOptions.Clear();
            foreach (var month in months)
            {
                MonthOptions.Add(month);
            }

            var today = DateTime.Today;
            SelectedMonth = preferred is not null && months.Contains(preferred)
                ? preferred
                : months.MinBy(m => Math.Abs((m.Year - today.Year) * 12 + m.Month - today.Month));
        }
        catch (Exception ex)
        {
            StatusMessage = $"取得エラー: {ex.Message}";
            return;
        }

        await RefreshAsync();
    }

    private bool _refreshRequested;

    /// <summary>
    /// 一覧の再取得。実行中に来た要求を覚えておき、終了後に最新の条件でもう一度取得する
    /// （<c>BillingClosingViewModel.RefreshAsync</c>と同じ理由）。
    /// </summary>
    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            _refreshRequested = true;
            return;
        }

        await RunBusyAsync(async () =>
        {
            do
            {
                _refreshRequested = false;
                await RefreshListAsync();
            }
            while (_refreshRequested);
        });
    }

    private async Task RefreshListAsync()
    {
        if (SelectedMonth is not { } month)
        {
            ApplyResults([]);
            StatusMessage = "この年に月次締め済みの年月はありません。";
            return;
        }

        try
        {
            var results = await monthlyClosingQueryService.GetByMonthAsync(month.Year, month.Month);
            ApplyResults(results);
            StatusMessage = results.Count == 0
                ? "この年月に確定済みの月次締めはありません。"
                : $"{results.Count}件の確定済み月次締めを表示しています。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"取得エラー: {ex.Message}";
        }
    }

    partial void OnSelectedMonthChanged(YearMonthOption? value) => _ = RefreshAsync();

    /// <summary>
    /// 締め確定。月次締め確定ダイアログ（締める年月の選択と実行確認を含む）を開き、実行されたら
    /// 締めた年月の一覧へ切り替えて今回の結果を表示する。
    /// </summary>
    [RelayCommand]
    private async Task ConfirmAsync()
    {
        var outcome = windowService.ShowDialog<MonthlyClosingConfirmDialog, MonthlyClosingConfirmDialogViewModel, MonthlyClosingOutcome>();
        if (outcome is null)
        {
            return;
        }

        YearText = outcome.Year.ToString();
        await ReloadMonthsAsync(new YearMonthOption(outcome.Year, outcome.Month));

        StatusMessage = $"月次締めを確定しました（確定 {outcome.ConfirmedCount}件／スキップ {outcome.SkippedCount}件）。";
    }

    /// <summary>一覧に1件以上あるときだけ印刷できる（0件の年月は未確定か、確定対象が無い）。</summary>
    private bool CanPrint() => Results.Count > 0;

    /// <summary>
    /// 選んだ年月の売掛金残高一覧表を、一覧の全件でプレビューする。画面の一覧ではなく
    /// 印刷用に取り直すのは、請求集約元の判定（得意先マスタ）を含むデータを使うため。
    /// 印刷履歴は記録しない（<c>docs/report-spec.md</c> 2-2節の方針と同じ）。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPrint))]
    private async Task PrintAsync()
    {
        if (SelectedMonth is not { } month)
        {
            StatusMessage = "集計年月を選択してください。";
            return;
        }

        ReceivablesBalanceReportData data;
        try
        {
            data = await monthlyClosingQueryService.GetReceivablesBalanceReportAsync(month.Year, month.Month);
        }
        catch (Exception ex)
        {
            StatusMessage = $"印刷エラー: {ex.Message}";
            return;
        }

        if (data.Rows.Count == 0)
        {
            StatusMessage = "この年月に確定済みの月次締めはありません。";
            return;
        }

        windowService.ShowDialog<ReportPreviewDialog, ReportPreviewDialogViewModel, bool>(
            vm => vm.Initialize(
                ReportKind.ReceivablesBalance,
                $"売掛金残高一覧表 {month.Display}",
                () => new ReceivablesBalanceDocumentBuilder(data).Build()));
    }

    private void ApplyResults(IReadOnlyList<MonthlyClosingListItem> results)
    {
        Results.Clear();
        foreach (var result in results)
        {
            Results.Add(result);
        }

        TotalClosingBalance = results.Sum(r => r.ClosingBalance);
        PrintCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>集計年月の選択肢（ComboBox表示用）。</summary>
public sealed record YearMonthOption(int Year, int Month)
{
    public string Display => $"{Year}年{Month:00}月";
}
