using System.Collections.ObjectModel;
using System.Windows;
using bmcs_app.Application.Billing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bmcs_app.ViewModels.Billing;

/// <summary>
/// 締め解除処理画面。請求締め処理（<see cref="BillingClosingViewModel"/>）
/// とは別画面（別ウィンドウ）とする（管理者権限のみの操作を画面分離で表現する。
/// 権限判定自体は0-7未着手のため、本画面では行わない）。
/// 入力は請求日のみ（<see cref="BillingClosingViewModel"/>と同様、年を4桁で入力し、その年の
/// 請求締め済み日付をコンボボックスから選ぶ。画面表示時・条件変更時に
/// 自動でプレビューを再取得し、保存を伴う解除実行のみ明示操作にする）。対象は請求日単位で
/// まとめて表示し、解除も一括で行う（All-or-nothing。1件でも解除できない対象が含まれる場合は
/// 実行不可）。
/// </summary>
public partial class BillingReleaseViewModel(
    BillingReleaseService billingReleaseService,
    InvoiceService invoiceService) : ViewModelBase
{
    /// <summary>請求日の選択対象とする年（4桁入力、初期値は今年）。</summary>
    [ObservableProperty]
    public partial string YearText { get; set; } = DateTime.Today.Year.ToString();

    /// <summary>選択中の年に請求締め済みの請求日（コンボボックスの選択肢）。</summary>
    public ObservableCollection<DateTime> BillingDates { get; } = [];

    /// <summary>
    /// 請求日。<see cref="BillingClosingService.ConfirmAsync"/>が確定する<c>billing_date</c>と同じ値。
    /// 初期値は今日に一番近い日付。該当日が無い年は null。
    /// </summary>
    [ObservableProperty]
    public partial DateTime? BillingDate { get; set; }

    public ObservableCollection<BillingReleaseTarget> Results { get; } = [];

    [ObservableProperty]
    public partial decimal TotalCurrentBillingAmount { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReleaseCommand))]
    public partial bool IsReleasable { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    /// <summary>画面表示時に、今年の請求日を読み込み、既定条件（今日に一番近い日付）でのプレビューを表示する。</summary>
    [RelayCommand]
    private Task LoadAsync() => ReloadDatesAsync();

    partial void OnYearTextChanged(string value)
    {
        if (value.Length == 4 && int.TryParse(value, out var year) && year >= 1900)
        {
            _ = ReloadDatesAsync();
        }
    }

    /// <summary>
    /// 選択中の年の請求日を読み込み直し、今日に一番近い日付を選択する。
    /// 選択が変わると<see cref="OnBillingDateChanged"/>がプレビューを再取得する
    /// （変わらない場合に備え、ここでも明示的に再取得する）。
    /// </summary>
    private async Task ReloadDatesAsync()
    {
        if (!int.TryParse(YearText, out var year) || YearText.Length != 4)
        {
            StatusMessage = "年は4桁で入力してください。";
            return;
        }

        try
        {
            var dates = (await invoiceService.GetBillingDatesAsync(year)).Select(d => d.ToDateTime(TimeOnly.MinValue)).ToList();

            BillingDates.Clear();
            foreach (var date in dates)
            {
                BillingDates.Add(date);
            }

            BillingDate = dates.Count == 0 ? null : dates.MinBy(d => Math.Abs((d - DateTime.Today).Days));
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
    /// プレビュー（保存しない）。<see cref="LoadAsync"/>と請求日変更時に自動的に呼ばれる。
    /// <see cref="ViewModelBase.RunBusyAsync"/>は多重実行を単純に無視するため、
    /// 画面表示直後の初回読込がまだ進行中のうちに請求日を変更されると、その変更が
    /// 何の再取得もされないまま握りつぶされてしまう（実機で確認した不具合）。
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
        if (BillingDate is not { } billingDateValue)
        {
            ApplyResults([]);
            StatusMessage = "この年に請求締め済みの日付はありません。";
            return;
        }

        try
        {
            var billingDate = DateOnly.FromDateTime(billingDateValue);
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
    /// </summary>
    partial void OnBillingDateChanged(DateTime? value) => _ = PreviewAsync();

    [RelayCommand(CanExecute = nameof(IsReleasable))]
    private async Task ReleaseAsync()
    {
        string? releasedMessage = null;

        await RunBusyAsync(async () =>
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

                releasedMessage = $"請求日 {billingDate:yyyy/MM/dd} の請求締め {released.Count}件を解除しました。";
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

        if (releasedMessage is null)
        {
            return;
        }

        // 解除後は画面を起動直後の状態へ戻す（docs/product-spec.md UI/UX節「登録後のリセット」）。
        // 再読込は処理中状態を抜けてから行う（処理中は再取得要求が無視されるため）。
        YearText = DateTime.Today.Year.ToString();
        await ReloadDatesAsync();
        StatusMessage = releasedMessage;
        NotifyResetToInitialState();
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
