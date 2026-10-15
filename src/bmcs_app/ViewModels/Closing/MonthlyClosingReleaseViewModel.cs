using System.Collections.ObjectModel;
using System.Windows;
using bmcs_app.Application.Closing;
using bmcs_app.ViewModels.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bmcs_app.ViewModels.Closing;

/// <summary>
/// 月次締め解除処理画面。月次締め処理（<see cref="MonthlyClosingViewModel"/>）とは
/// 別画面（別ウィンドウ）とする（管理者権限のみの操作を画面分離で表現する。
/// 権限はメニュー単位の判定のみで、本画面では行わない）。構成は締め解除処理（請求）と同じで、
/// 入力は集計年月のみ（年を4桁入力→その年に月次締め済みの年月をコンボボックスで選択）。画面表示時・年月変更時に自動でプレビューを取得し、保存を伴う解除実行のみ
/// 明示操作にする。対象は年月単位でまとめて表示し、解除も一括で行う（All-or-nothing。
/// 1件でも解除できない対象が含まれる場合は実行不可）。
/// </summary>
public partial class MonthlyClosingReleaseViewModel(
    MonthlyClosingReleaseService releaseService,
    MonthlyClosingQueryService queryService) : ViewModelBase
{
    /// <summary>選択中の年（4桁入力、初期値は今年）。</summary>
    [ObservableProperty]
    public partial string YearText { get; set; } = DateTime.Today.Year.ToString();

    /// <summary>選択中の年に月次締め済みの年月（コンボボックスの選択肢）。</summary>
    public ObservableCollection<YearMonthOption> MonthOptions { get; } = [];

    /// <summary>集計年月（暦月）。初期値は今月に一番近い月。該当月が無い年は null。</summary>
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

    /// <summary>画面表示時に今年の月次締め済み年月を読み込み、今月に一番近い月のプレビューを表示する。</summary>
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
            var months = (await queryService.GetClosedMonthsAsync(year)).Select(m => new YearMonthOption(year, m)).ToList();

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

        await PreviewAsync();
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
            ApplyResults([]);
            StatusMessage = "この年に月次締め済みの年月はありません。";
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
    private async Task ReleaseAsync()
    {
        string? releasedMessage = null;

        await RunBusyAsync(async () =>
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
                releasedMessage = $"{month.Display}の月次締め {released.Count}件を解除しました。";
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

        if (releasedMessage is null)
        {
            return;
        }

        // 解除後は画面を起動直後の状態へ戻す（docs/product-spec.md UI/UX節「登録後のリセット」）。
        // 再読込は処理中状態を抜けてから行う（処理中は再取得要求が無視されるため）。
        YearText = DateTime.Today.Year.ToString();
        await ReloadMonthsAsync();
        StatusMessage = releasedMessage;
        NotifyResetToInitialState();
    }

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
