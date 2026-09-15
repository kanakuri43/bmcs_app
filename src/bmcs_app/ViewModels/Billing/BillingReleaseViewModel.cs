using System.Collections.ObjectModel;
using System.Windows;
using bmcs_app.Application.Billing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bmcs_app.ViewModels.Billing;

/// <summary>
/// 締め解除処理画面（TODO.md 6-2、2026-09-15改訂）。請求締め処理（<see cref="BillingClosingViewModel"/>）
/// とは別画面（別ウィンドウ）とする（C-8・2026-09-10確定。管理者権限のみの操作を画面分離で表現する。
/// 権限判定自体は0-7未着手のため、本画面では行わない）。
/// 入力は請求日のみ（<see cref="BillingClosingViewModel"/>と同様、画面表示時・条件変更時に
/// 自動でプレビューを再取得し、保存を伴う解除実行のみ明示操作にする）。対象は請求日単位で
/// まとめて表示し、解除も一括で行う（All-or-nothing。1件でも解除できない対象が含まれる場合は
/// 実行不可）。
/// </summary>
public partial class BillingReleaseViewModel(BillingReleaseService billingReleaseService) : ViewModelBase
{
    /// <summary>請求日。<see cref="BillingClosingService.ConfirmAsync"/>が確定する<c>billing_date</c>と同じ値。</summary>
    [ObservableProperty]
    public partial DateTime? BillingDate { get; set; } = DateTime.Today;

    public ObservableCollection<BillingReleaseTarget> Results { get; } = [];

    [ObservableProperty]
    public partial decimal TotalCurrentBillingAmount { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReleaseCommand))]
    public partial bool IsReleasable { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    /// <summary>画面表示時に、既定条件（当日）でのプレビューを表示する。</summary>
    [RelayCommand]
    private Task LoadAsync() => PreviewAsync();

    private bool _refreshRequested;

    /// <summary>
    /// プレビュー（保存しない）。<see cref="LoadAsync"/>と請求日変更時に自動的に呼ばれる。
    /// <see cref="ViewModelBase.RunBusyAsync"/>は多重実行を単純に無視するため、
    /// 画面表示直後の初回読込がまだ進行中のうちに請求日を変更されると、その変更が
    /// 何の再取得もされないまま握りつぶされてしまう（2026-09-15、実機で確認した不具合）。
    /// ここで「実行中に来た要求」を覚えておき、実行中の処理が終わった後にもう一度
    /// （その時点の最新の請求日で）再取得することで取りこぼしを防ぐ。
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
        if (!TryGetBillingDate(out var billingDate))
        {
            return;
        }

        try
        {
            var results = await billingReleaseService.PreviewAsync(billingDate);
            ApplyResults(results);
            StatusMessage = results.Count == 0
                ? "該当する確定済み請求データがありません。"
                : $"{results.Count}件を取得しました。";
        }
        catch (BillingReleaseException ex)
        {
            StatusMessage = $"取得エラー: {ex.Message}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"取得エラー: {ex.Message}";
        }
    }

    /// <summary>
    /// 請求日を変えたら自動的にその条件で再取得する（<see cref="BillingClosingViewModel"/>と同様）。
    /// <c>DatePicker.SelectedDate</c>は確定した瞬間（Enter／フォーカス離脱／カレンダー選択）にしか
    /// 変化しないため、入力中の1文字ごとにDB照会が走ることはない。
    /// </summary>
    partial void OnBillingDateChanged(DateTime? value) => _ = PreviewAsync();

    [RelayCommand(CanExecute = nameof(IsReleasable))]
    private Task ReleaseAsync() => RunBusyAsync(async () =>
    {
        if (!TryGetBillingDate(out var billingDate))
        {
            return;
        }

        var confirm = MessageBox.Show(
            $"請求日 {billingDate:yyyy/MM/dd} の請求締め {Results.Count}件（今回請求額合計 {TotalCurrentBillingAmount:N0}円）を解除しますか？\n" +
            "解除すると、これらの請求に集計済みの売上がすべて未請求に戻ります。",
            "bmcs_app", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var released = await billingReleaseService.ReleaseByBillingDateAsync(billingDate);

            // 解除後は画面を起動直後の状態へ戻す（docs/product-spec.md UI/UX節「登録後のリセット」）。
            ResetConditionToDefault();
            await RefreshPreviewAsync();
            StatusMessage = $"請求日 {billingDate:yyyy/MM/dd} の請求締め {released.Count}件を解除しました。";
            NotifyResetToInitialState();
        }
        catch (BillingReleaseException ex)
        {
            StatusMessage = $"解除エラー: {ex.Message}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"解除エラー: {ex.Message}";
        }
    });

    private void ResetConditionToDefault()
    {
        BillingDate = DateTime.Today;
    }

    private bool TryGetBillingDate(out DateOnly billingDate)
    {
        if (BillingDate is not { } value)
        {
            StatusMessage = "請求日を入力してください。";
            billingDate = default;
            return false;
        }

        billingDate = DateOnly.FromDateTime(value);
        return true;
    }

    private void ApplyResults(IReadOnlyList<BillingReleaseTarget> results)
    {
        Results.Clear();
        foreach (var result in results)
        {
            Results.Add(result);
        }

        TotalCurrentBillingAmount = results.Sum(r => r.CurrentBillingAmount);
        IsReleasable = results.Count > 0 && results.All(r => r.BlockReason is null);
    }
}
