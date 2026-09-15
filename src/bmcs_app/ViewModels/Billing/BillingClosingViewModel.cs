using System.Collections.ObjectModel;
using bmcs_app.Application.Billing;
using bmcs_app.Domain.Calculations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bmcs_app.ViewModels.Billing;

/// <summary>
/// 請求締め処理画面（TODO.md 6-1）。「締め日を指定して一括」処理する専用画面
/// （2026-09-11ユーザー確認）。入力は締め日区分（対象得意先の絞り込み）と請求日
/// （締め切り日）の2つのみ。**「請求日でいつ締め切るか決まる」ため、対象年月を別入力に
/// しない**（2026-09-11ユーザー確認。当初あった対象年月入力は請求日から導出できる冗長な
/// 入力だったため撤去した）。行選択（チェックボックス）は持たない（一括処理の方針上不要。
/// docs/design_document.md 9章）。
/// **プレビューはボタン操作を挟まず、画面表示時（既定条件）と条件変更時に自動で
/// 再取得する（2026-09-11ユーザー確認）。** 保存を伴う確定（<see cref="ConfirmCommand"/>）
/// のみ明示操作にする。
/// </summary>
public partial class BillingClosingViewModel(BillingClosingService billingClosingService) : ViewModelBase
{
    public ObservableCollection<ClosingDayOption> ClosingDayOptions { get; } = [];

    [ObservableProperty]
    public partial ClosingDayOption? SelectedClosingDay { get; set; }

    /// <summary>
    /// 請求日（＝締め切り日）。売上・入金の集計対象範囲の上限であり、確定する
    /// <c>billing</c>の請求日としてもそのまま使う（<see cref="BillingClosingService.ConfirmAsync"/>参照）。
    /// </summary>
    [ObservableProperty]
    public partial DateTime? ClosingDate { get; set; } = DateTime.Today;

    public ObservableCollection<BillingClosingTarget> Results { get; } = [];

    [ObservableProperty]
    public partial decimal TotalCurrentBillingAmount { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    /// <summary>
    /// 画面表示時に締め日区分の選択肢を読み込み、既定条件でのプレビューを表示する
    /// （2026-09-11ユーザー確認。対象取得ボタンは持たない）。
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        await RunBusyAsync(async () =>
        {
            var closingDays = await billingClosingService.GetClosingDayOptionsAsync();

            ClosingDayOptions.Clear();
            foreach (var closingDay in closingDays)
            {
                ClosingDayOptions.Add(new ClosingDayOption(closingDay));
            }

            SelectedClosingDay ??= ClosingDayOptions.FirstOrDefault();
        });

        // 上記の SelectedClosingDay 設定は RunBusyAsync の実行中に行われるため、
        // OnSelectedClosingDayChanged からの自動再取得は IsBusy によって無視される
        // （多重実行の抑止）。そのため既定条件でのプレビューはここで明示的に行う。
        await PreviewAsync();
    }

    private bool _refreshRequested;

    /// <summary>
    /// プレビュー（保存しない）。対象取得ボタンは持たず、<see cref="LoadAsync"/>（画面表示時）と
    /// 条件変更時（<see cref="OnSelectedClosingDayChanged"/>／<see cref="OnClosingDateChanged"/>）
    /// から自動的に呼ばれる。<see cref="ViewModelBase.RunBusyAsync"/>は多重実行を単純に無視するため、
    /// 画面表示直後の初回読込がまだ進行中のうちに条件を変更されると、その変更が何の再取得も
    /// されないまま握りつぶされてしまう（2026-09-15、締め解除処理〈<c>BillingReleaseViewModel</c>〉
    /// で実機確認した不具合と同型。同じ「画面表示時に自動プレビュー」方式のため本画面にも同じ
    /// 潜在バグがある）。ここで「実行中に来た要求」を覚えておき、実行中の処理が終わった後に
    /// もう一度（その時点の最新の条件で）再取得することで取りこぼしを防ぐ。
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

    /// <summary>
    /// <see cref="PreviewAsync"/> の本体。<see cref="ConfirmAsync"/> の中からは
    /// （既に <c>IsBusy</c> 状態のため）<see cref="RunBusyAsync"/> を経由せず直接呼ぶ
    /// （確定後にリセットした既定条件で即座に再取得するため）。
    /// </summary>
    private async Task RefreshPreviewAsync()
    {
        if (!TryGetConditions(out var closingDay, out var closingDate))
        {
            return;
        }

        try
        {
            var results = await billingClosingService.PreviewAsync(closingDay, closingDate);
            ApplyResults(results);
            StatusMessage = $"{results.Count}件を取得しました（対象 {results.Count(r => r.SkipReason is null)}件）。";
        }
        catch (BillingClosingException ex)
        {
            StatusMessage = $"取得エラー: {ex.Message}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"取得エラー: {ex.Message}";
        }
    }

    /// <summary>
    /// 締め日区分の選択を変えたら、請求日の年月はそのまま・日だけをその締め日区分に合わせて
    /// 補正し、自動的にその条件で再取得する（2026-09-11ユーザー確認）。
    /// </summary>
    partial void OnSelectedClosingDayChanged(ClosingDayOption? value)
    {
        if (value is not null)
        {
            var reference = ClosingDate is { } current ? DateOnly.FromDateTime(current) : DateOnly.FromDateTime(DateTime.Today);
            ClosingDate = ClosingDateResolver.Resolve(reference.Year, reference.Month, value.Value).ToDateTime(TimeOnly.MinValue);
        }

        _ = PreviewAsync();
    }

    /// <summary>
    /// 請求日を変えたら自動的にその条件で再取得する（2026-09-11ユーザー確認）。
    /// <c>DatePicker.SelectedDate</c>は確定した瞬間（Enter／フォーカス離脱／カレンダー選択）にしか
    /// 変化しないため、入力中の1文字ごとにDB照会が走ることはない。
    /// </summary>
    partial void OnClosingDateChanged(DateTime? value) => _ = PreviewAsync();

    /// <summary>締め確定。同条件で再集計してから確定する（<see cref="PreviewAsync"/>の結果は使わない）。</summary>
    [RelayCommand]
    private Task ConfirmAsync() => RunBusyAsync(async () =>
    {
        if (!TryGetConditions(out var closingDay, out var closingDate))
        {
            return;
        }

        try
        {
            var results = await billingClosingService.ConfirmAsync(closingDay, closingDate);
            var confirmedCount = results.Count(r => r.SkipReason is null);
            var skippedCount = results.Count(r => r.SkipReason is not null);

            // 確定後は結果一覧も含めて画面表示直後の状態に戻す（docs/product-spec.md UI/UX節
            // 「登録後のリセット」。結果一覧は締め結果を確認できる唯一の証跡だが、確定後は
            // 再検索する運用のためクリアする。2026-09-11ユーザー確認）。
            Results.Clear();
            TotalCurrentBillingAmount = 0m;
            ResetConditionsToDefault();
            await RefreshPreviewAsync();

            StatusMessage = $"請求締めを確定しました（確定 {confirmedCount}件／スキップ {skippedCount}件）。";
            NotifyResetToInitialState();
        }
        catch (BillingClosingException ex)
        {
            StatusMessage = $"確定エラー: {ex.Message}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"確定エラー: {ex.Message}";
        }
    });

    /// <summary>画面表示直後（<see cref="LoadAsync"/>）と同じ既定条件に戻す。</summary>
    private void ResetConditionsToDefault()
    {
        SelectedClosingDay = ClosingDayOptions.FirstOrDefault();
        ClosingDate = DateTime.Today;
    }

    private bool TryGetConditions(out byte closingDay, out DateOnly closingDate)
    {
        closingDay = 0;
        closingDate = default;

        if (SelectedClosingDay is null)
        {
            StatusMessage = "締め日を選択してください。";
            return false;
        }

        if (ClosingDate is not { } value)
        {
            StatusMessage = "請求日を入力してください。";
            return false;
        }

        closingDate = DateOnly.FromDateTime(value);
        closingDay = SelectedClosingDay.Value;
        return true;
    }

    private void ApplyResults(IReadOnlyList<BillingClosingTarget> results)
    {
        Results.Clear();
        foreach (var result in results)
        {
            Results.Add(result);
        }

        TotalCurrentBillingAmount = results.Where(r => r.SkipReason is null).Sum(r => r.CurrentBillingAmount);
    }
}

/// <summary>締め日区分の選択肢（ComboBox表示用）。</summary>
public sealed record ClosingDayOption(byte Value)
{
    public string Display => Value == 99 ? "末日締め" : $"{Value}日締め";
}
