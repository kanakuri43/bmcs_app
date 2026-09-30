using System.Collections.ObjectModel;
using System.Windows;
using bmcs_app.Application.Closing;
using bmcs_app.ViewModels.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bmcs_app.ViewModels.Closing;

/// <summary>
/// 月次締め処理画面（TODO.md 9-1）。構成は請求締め処理画面（<c>BillingClosingViewModel</c>）と同じで、
/// 違いは「締め日のコンボボックスがない」「請求日の代わりに集計年月（暦月）を選ぶ」の2点。
/// 一覧は選んだ年月の確定済み <c>monthly_closings</c>（解除済みを除く）で、年月を変えると自動で再取得する。
/// 締め確定は全得意先を対象とし、実行前に確認ダイアログを挟む。
/// </summary>
public partial class MonthlyClosingViewModel(
    MonthlyClosingService monthlyClosingService,
    MonthlyClosingQueryService monthlyClosingQueryService) : ViewModelBase
{
    /// <summary>集計年月として選べる期間（当月から過去へ）。</summary>
    private const int SelectableMonthCount = 25;

    public ObservableCollection<YearMonthOption> MonthOptions { get; } = [];

    /// <summary>集計年月（暦月）。集計期間は月初〜月末で、月末日が <c>monthly_closings.closing_date</c> になる。</summary>
    [ObservableProperty]
    public partial YearMonthOption? SelectedMonth { get; set; }

    public ObservableCollection<MonthlyClosingListItem> Results { get; } = [];

    [ObservableProperty]
    public partial decimal TotalClosingBalance { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    /// <summary>画面表示時に年月の選択肢を作り、前月（月次締めは月が明けてから行う想定）の一覧を表示する。</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        await RunBusyAsync(() =>
        {
            var thisMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

            MonthOptions.Clear();
            for (var i = 0; i < SelectableMonthCount; i++)
            {
                var month = thisMonth.AddMonths(-i);
                MonthOptions.Add(new YearMonthOption(month.Year, month.Month));
            }

            SelectedMonth ??= MonthOptions[1];
            return Task.CompletedTask;
        });

        // SelectedMonth の設定は RunBusyAsync の実行中に行うため、OnSelectedMonthChanged からの
        // 自動再取得は IsBusy によって無視される。既定条件での一覧取得はここで明示的に行う。
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
            StatusMessage = "集計年月を選択してください。";
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

    [RelayCommand]
    private Task ConfirmAsync() => RunBusyAsync(async () =>
    {
        if (SelectedMonth is not { } month)
        {
            StatusMessage = "集計年月を選択してください。";
            return;
        }

        var confirm = MessageBox.Show(
            $"{month.Display}の月次締めを全得意先について確定しますか？\n" +
            "確定後は該当年月の伝票が編集できなくなります。",
            "bmcs_app", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var results = await monthlyClosingService.ConfirmAsync(month.Year, month.Month);
            var confirmedCount = results.Count(r => r.SkipReason is null);
            var skippedCount = results.Count - confirmedCount;

            await RefreshListAsync();

            StatusMessage = $"月次締めを確定しました（確定 {confirmedCount}件／スキップ {skippedCount}件）。";
        }
        catch (MonthlyClosingException ex)
        {
            StatusMessage = $"確定エラー: {ex.Message}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"確定エラー: {ex.Message}";
        }
    });

    private void ApplyResults(IReadOnlyList<MonthlyClosingListItem> results)
    {
        Results.Clear();
        foreach (var result in results)
        {
            Results.Add(result);
        }

        TotalClosingBalance = results.Sum(r => r.ClosingBalance);
    }
}

/// <summary>集計年月の選択肢（ComboBox表示用）。</summary>
public sealed record YearMonthOption(int Year, int Month)
{
    public string Display => $"{Year}年{Month:00}月";
}
