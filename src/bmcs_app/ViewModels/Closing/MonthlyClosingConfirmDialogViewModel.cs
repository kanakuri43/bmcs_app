using System.Collections.ObjectModel;
using System.Windows;
using bmcs_app.Application.Closing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bmcs_app.ViewModels.Closing;

/// <summary>月次締め確定ダイアログの実行結果（キャンセル・失敗時は結果なし）。</summary>
public sealed record MonthlyClosingOutcome(int Year, int Month, int ConfirmedCount, int SkippedCount);

/// <summary>
/// 月次締め確定ダイアログ。締める集計年月（当月から過去25か月、既定は前月）を選んで全得意先の月次締めを実行する。
/// 実行に成功したらダイアログを閉じ、呼び出し元（<see cref="MonthlyClosingViewModel"/>）がその年月の一覧を表示する。
/// </summary>
public partial class MonthlyClosingConfirmDialogViewModel(MonthlyClosingService monthlyClosingService)
    : DialogViewModelBase<MonthlyClosingOutcome>
{
    /// <summary>集計年月として選べる期間（当月から過去へ）。</summary>
    private const int SelectableMonthCount = 25;

    public ObservableCollection<YearMonthOption> MonthOptions { get; } = [];

    [ObservableProperty]
    public partial YearMonthOption? SelectedMonth { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [RelayCommand]
    private void Load()
    {
        var thisMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        for (var i = 0; i < SelectableMonthCount; i++)
        {
            var month = thisMonth.AddMonths(-i);
            MonthOptions.Add(new YearMonthOption(month.Year, month.Month));
        }

        SelectedMonth = MonthOptions[1];
    }

    [RelayCommand]
    private Task ExecuteAsync() => RunBusyAsync(async () =>
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

            CloseWith(new MonthlyClosingOutcome(month.Year, month.Month, confirmedCount, results.Count - confirmedCount));
        }
        catch (Exception ex)
        {
            StatusMessage = $"確定エラー: {ex.Message}";
        }
    });
}
