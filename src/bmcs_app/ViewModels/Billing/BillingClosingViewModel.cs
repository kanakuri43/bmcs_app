using System.Collections.ObjectModel;
using bmcs_app.Application.Billing;
using bmcs_app.Domain.Calculations;
using bmcs_app.Reports;
using bmcs_app.Services;
using bmcs_app.ViewModels.Common;
using bmcs_app.Views.Common;
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
/// **確定後は結果一覧を含めてリセットしない（TODO.md 10-5、2026-09-16改訂）。** 確定直後の
/// 結果一覧（請求番号入り）から選択行を印刷できるようにするため。締め日区分・請求日を
/// 変更すれば次のバッチとして一覧が置き換わる（<see cref="OnSelectedClosingDayChanged"/>／
/// <see cref="OnClosingDateChanged"/>が従来どおり自動再取得する）。詳細は
/// <c>docs/design_document.md</c> 9章参照。
/// </summary>
public partial class BillingClosingViewModel(
    BillingClosingService billingClosingService,
    InvoiceService invoiceService,
    WindowService windowService) : ViewModelBase
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

    /// <summary>選択中の結果行。印刷(F11)の対象。確定済み（<c>BillingNumber</c>が非null）の行のみ印刷できる。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrintCommand))]
    public partial BillingClosingTarget? SelectedResult { get; set; }

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

            // 確定後も結果一覧（請求番号入り）はそのまま残す（TODO.md 10-5、2026-09-16改訂。
            // 従来は画面表示直後の状態へ即座にリセットしていたが、確定した請求書をその場で
            // 印刷できるようにするため一覧のクリアをやめた。次のバッチに進む場合は締め日区分・
            // 請求日を変更すればよく、その時点で一覧は自動的に置き換わる）。
            ApplyResults(results);

            StatusMessage = $"請求締めを確定しました（確定 {confirmedCount}件／スキップ {skippedCount}件）。" +
                (confirmedCount > 0 ? "印刷したい行を選択し「選択行を印刷」してください。" : "");
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

    /// <summary>
    /// 印刷（F11、TODO.md 10-5）。確定済み（<c>BillingNumber</c>が非null）の行のみ有効。
    /// 印刷履歴は記録しない。
    /// </summary>
    private bool CanPrint(BillingClosingTarget? target) => target?.BillingNumber is not null;

    [RelayCommand(CanExecute = nameof(CanPrint))]
    private async Task PrintAsync(BillingClosingTarget? target)
    {
        if (target?.BillingNumber is not string billingNumber)
        {
            return;
        }

        InvoiceData? data;
        try
        {
            data = await invoiceService.GetByNumberAsync(billingNumber);
        }
        catch (InvoiceException ex)
        {
            StatusMessage = $"印刷エラー: {ex.Message}";
            return;
        }

        if (data is null)
        {
            StatusMessage = $"請求番号「{billingNumber}」が見つかりません。";
            return;
        }

        windowService.ShowDialog<ReportPreviewDialog, ReportPreviewDialogViewModel, bool>(
            vm => vm.Initialize(ReportKind.Invoice, $"請求書 {billingNumber}", () => new InvoiceDocumentBuilder(data).Build()));
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
