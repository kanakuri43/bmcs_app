using System.Windows;
using bmcs_app.Application.Closing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bmcs_app.ViewModels.Closing;

/// <summary>月次締め確定ダイアログの実行結果（キャンセル・失敗時は結果なし）。</summary>
public sealed record MonthlyClosingOutcome(int Year, int Month, int ConfirmedCount, int SkippedCount);

/// <summary>
/// 月次締め確定ダイアログ。締める集計年月を、メイン画面と同じく年（4桁入力）と月（1〜12のコンボボックス）で指定し、
/// 全得意先の月次締めを実行する。既定は前月。当月より後の年月は締められない。
/// 実行に成功したらダイアログを閉じ、呼び出し元（<see cref="MonthlyClosingViewModel"/>）がその年月の一覧を表示する。
/// </summary>
public partial class MonthlyClosingConfirmDialogViewModel(MonthlyClosingService monthlyClosingService)
    : DialogViewModelBase<MonthlyClosingOutcome>
{
    public IReadOnlyList<int> MonthOptions { get; } = Enumerable.Range(1, 12).ToList();

    /// <summary>締める年（4桁入力）。</summary>
    [ObservableProperty]
    public partial string YearText { get; set; } = DateTime.Today.AddMonths(-1).Year.ToString();

    /// <summary>締める月（1〜12）。</summary>
    [ObservableProperty]
    public partial int SelectedMonth { get; set; } = DateTime.Today.AddMonths(-1).Month;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    /// <summary>締め実行中（確認後〜完了）。進捗バーの表示と、実行中の操作ロックに使う。</summary>
    [ObservableProperty]
    public partial bool IsExecuting { get; set; }

    protected override bool CanCancel => !IsExecuting;

    partial void OnIsExecutingChanged(bool value) => CancelCommand.NotifyCanExecuteChanged();

    [ObservableProperty]
    public partial int ProgressValue { get; set; }

    [ObservableProperty]
    public partial int ProgressMax { get; set; } = 1;

    [ObservableProperty]
    public partial string ProgressText { get; set; } = string.Empty;

    private void ReportProgress((int Processed, int Total) p)
    {
        ProgressMax = Math.Max(p.Total, 1);
        ProgressValue = p.Processed;
        ProgressText = $"{p.Processed:N0} / {p.Total:N0} 件";
    }

    [RelayCommand]
    private Task ExecuteAsync() => RunBusyAsync(async () =>
    {
        if (YearText.Length != 4 || !int.TryParse(YearText, out var year) || year < 1900)
        {
            StatusMessage = "年は4桁で入力してください。";
            return;
        }

        var today = DateTime.Today;
        if (year * 12 + SelectedMonth > today.Year * 12 + today.Month)
        {
            StatusMessage = "当月より後の年月は締められません。";
            return;
        }

        var month = new YearMonthOption(year, SelectedMonth);
        var confirm = MessageBox.Show(
            $"{month.Display}の月次締めを全得意先について確定しますか？\n" +
            "確定後は該当年月の伝票が編集できなくなります。",
            "bmcs_app", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        IsExecuting = true;
        ProgressValue = 0;
        ProgressText = string.Empty;
        try
        {
            var progress = new Progress<(int Processed, int Total)>(ReportProgress);
            var results = await monthlyClosingService.ConfirmAsync(month.Year, month.Month, progress: progress);
            var confirmedCount = results.Count(r => r.SkipReason is null);

            CloseWith(new MonthlyClosingOutcome(month.Year, month.Month, confirmedCount, results.Count - confirmedCount));
        }
        catch (Exception ex)
        {
            StatusMessage = $"確定エラー: {ex.Message}";
        }
        finally
        {
            IsExecuting = false;
        }
    });
}
