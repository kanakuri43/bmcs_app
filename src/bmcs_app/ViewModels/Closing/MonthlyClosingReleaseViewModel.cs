using System.Collections.ObjectModel;
using System.Windows;
using bmcs_app.Application.Closing;
using bmcs_app.ViewModels.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bmcs_app.ViewModels.Closing;

/// <summary>
/// 月次締め解除処理画面（TODO.md 9-3）。月次締め処理（<see cref="MonthlyClosingViewModel"/>）とは
/// 別画面（別ウィンドウ）とする（C-8・2026-09-10確定。管理者権限のみの操作を画面分離で表現する。
/// 権限はメニュー単位の判定のみで、本画面では行わない）。構成は締め解除処理（請求）と同じで、
/// 入力は集計年月のみ。画面表示時・年月変更時に自動でプレビューを取得し、保存を伴う解除実行のみ
/// 明示操作にする。対象は年月単位でまとめて表示し、解除も一括で行う（All-or-nothing。
/// 1件でも解除できない対象が含まれる場合は実行不可）。
/// </summary>
public partial class MonthlyClosingReleaseViewModel(MonthlyClosingReleaseService releaseService) : ViewModelBase
{
    /// <summary>集計年月として選べる期間（当月から過去へ）。</summary>
    private const int SelectableMonthCount = 25;

    public ObservableCollection<YearMonthOption> MonthOptions { get; } = [];

    [ObservableProperty]
    public partial YearMonthOption? SelectedMonth { get; set; }

    public ObservableCollection<MonthlyClosingReleaseTarget> Results { get; } = [];

    [ObservableProperty]
    public partial decimal TotalClosingBalance { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReleaseCommand))]
    public partial bool IsReleasable { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    /// <summary>画面表示時に年月の選択肢を作り、前月の確定済み月次締めを表示する。</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        await RunBusyAsync(() =>
        {
            FillMonthOptions();
            SelectedMonth ??= MonthOptions[1];
            return Task.CompletedTask;
        });

        // SelectedMonth の設定は RunBusyAsync の実行中に行うため、OnSelectedMonthChanged からの
        // 自動再取得は IsBusy によって無視される。既定条件でのプレビューはここで明示的に行う。
        await PreviewAsync();
    }

    private void FillMonthOptions()
    {
        var thisMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

        MonthOptions.Clear();
        for (var i = 0; i < SelectableMonthCount; i++)
        {
            var month = thisMonth.AddMonths(-i);
            MonthOptions.Add(new YearMonthOption(month.Year, month.Month));
        }
    }

    private bool _refreshRequested;

    /// <summary>
    /// プレビュー（保存しない）。実行中に来た要求を覚えておき、終了後に最新の条件でもう一度取得する
    /// （<c>BillingReleaseViewModel.PreviewAsync</c>と同じ理由）。
    /// </summary>
    private async Task PreviewAsync()
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
                await RefreshPreviewAsync();
            }
            while (_refreshRequested);
        });
    }

    private async Task RefreshPreviewAsync()
    {
        if (SelectedMonth is not { } month)
        {
            StatusMessage = "集計年月を選択してください。";
            return;
        }

        try
        {
            var results = await releaseService.PreviewAsync(month.Year, month.Month);
            ApplyResults(results);
            StatusMessage = results.Count == 0
                ? "該当する確定済み月次締めがありません。"
                : $"{results.Count}件を取得しました。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"取得エラー: {ex.Message}";
        }
    }

    partial void OnSelectedMonthChanged(YearMonthOption? value) => _ = PreviewAsync();

    [RelayCommand(CanExecute = nameof(IsReleasable))]
    private Task ReleaseAsync() => RunBusyAsync(async () =>
    {
        if (SelectedMonth is not { } month)
        {
            StatusMessage = "集計年月を選択してください。";
            return;
        }

        var confirm = MessageBox.Show(
            $"{month.Display}の月次締め {Results.Count}件（当月残高合計 {TotalClosingBalance:N0}円）を解除しますか？\n" +
            "解除すると、該当年月の伝票が再び登録・訂正・取消できるようになります。",
            "bmcs_app", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var released = await releaseService.ReleaseAsync(month.Year, month.Month);

            // 解除後は画面を起動直後の状態へ戻す（docs/product-spec.md UI/UX節「登録後のリセット」）。
            SelectedMonth = MonthOptions[1];
            await RefreshPreviewAsync();
            StatusMessage = $"{month.Display}の月次締め {released.Count}件を解除しました。";
            NotifyResetToInitialState();
        }
        catch (MonthlyClosingException ex)
        {
            StatusMessage = $"解除エラー: {ex.Message}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"解除エラー: {ex.Message}";
        }
    });

    private void ApplyResults(IReadOnlyList<MonthlyClosingReleaseTarget> results)
    {
        Results.Clear();
        foreach (var result in results)
        {
            Results.Add(result);
        }

        TotalClosingBalance = results.Sum(r => r.ClosingBalance);
        IsReleasable = results.Count > 0 && results.All(r => r.BlockReason is null);
    }
}
