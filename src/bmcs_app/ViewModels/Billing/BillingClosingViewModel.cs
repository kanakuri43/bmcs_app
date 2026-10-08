using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Documents;
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
/// 請求締め処理画面。「締め日を指定して一括」処理する専用画面。入力は締め日区分（対象得意先の絞り込み）と請求日
/// （締め切り日）の2つのみ。**「請求日でいつ締め切るか決まる」ため、対象年月を別入力に
/// しない**。
///
/// **一覧は「これから締めたらどうなるか」の集計プレビューではなく、`billings`に実在する
/// 確定済み請求データを請求日（<c>billing_date</c>）だけで抽出したもの。** 締め日区分は抽出条件に使わない（<c>billings</c>に締め日区分を保持する
/// 列が無いため）。未確定の請求日を選んだときは1件も表示しない。**確定前の集計プレビュー
/// 機能はこの画面から廃止した**（過去に締めた請求書をいつでも再照会・再印刷できることを
/// 優先した）。これに伴い、締め確定（<see cref="ConfirmCommand"/>）は
/// 実行前に確認ダイアログを挟む（<see cref="BillingReleaseViewModel"/>と同型）。
///
/// 一覧は拡張選択（Ctrl/Shiftクリック。チェックボックス列は持たない）
/// で複数行を選べる。選択した行はまとめて1つのプレビューダイアログで印刷でき、これが
/// 請求書の再発行手段になる（<see cref="PrintCommand"/>）。
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
    /// 一覧（<see cref="Results"/>）の抽出条件（<c>billing_date</c>一致）にもそのまま使う。
    /// </summary>
    [ObservableProperty]
    public partial DateTime? ClosingDate { get; set; } = DateTime.Today;

    /// <summary>指定した請求日の確定済み請求データ一覧（<see cref="InvoiceService.GetByBillingDateAsync"/>）。</summary>
    public ObservableCollection<InvoiceListItem> Results { get; } = [];

    /// <summary>選択中の行（拡張選択、複数可）。印刷(F11)の対象。</summary>
    public ObservableCollection<InvoiceListItem> SelectedResults { get; } = [];

    [ObservableProperty]
    public partial decimal TotalCurrentBillingAmount { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    /// <summary>
    /// 画面表示時に締め日区分の選択肢を読み込み、既定条件での一覧を表示する
    /// （対象取得ボタンは持たない）。
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        SelectedResults.CollectionChanged -= OnSelectedResultsChanged;
        SelectedResults.CollectionChanged += OnSelectedResultsChanged;

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
        // （多重実行の抑止）。そのため既定条件での一覧取得はここで明示的に行う。
        await RefreshAsync();
    }

    private void OnSelectedResultsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => PrintCommand.NotifyCanExecuteChanged();

    private bool _refreshRequested;

    /// <summary>
    /// 一覧の再取得（保存しない）。対象取得ボタンは持たず、<see cref="LoadAsync"/>（画面表示時）と
    /// 条件変更時（<see cref="OnClosingDateChanged"/>）から自動的に呼ばれる。
    /// <see cref="ViewModelBase.RunBusyAsync"/>は多重実行を単純に無視するため、画面表示直後の
    /// 初回読込がまだ進行中のうちに条件を変更されると、その変更が何の再取得もされないまま
    /// 握りつぶされてしまう（締め解除処理〈<c>BillingReleaseViewModel</c>〉でも実機確認した
    /// 不具合と同型）。ここで「実行中に来た要求」を覚えておき、実行中の処理が終わった後にもう一度
    /// （その時点の最新の条件で）再取得することで取りこぼしを防ぐ。
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

    /// <summary>
    /// <see cref="RefreshAsync"/> の本体。<see cref="ConfirmAsync"/> の中からは
    /// （既に <c>IsBusy</c> 状態のため）<see cref="RunBusyAsync"/> を経由せず直接呼ぶ
    /// （確定直後に確定済みの一覧を即座に反映するため）。
    /// </summary>
    private async Task RefreshListAsync()
    {
        if (!TryGetClosingDate(out var closingDate))
        {
            return;
        }

        try
        {
            var results = await invoiceService.GetByBillingDateAsync(closingDate);
            ApplyResults(results);
            StatusMessage = results.Count == 0
                ? "この請求日に確定済みの請求データはありません。"
                : $"{results.Count}件の確定済み請求データを表示しています。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"取得エラー: {ex.Message}";
        }
    }

    /// <summary>
    /// 締め日区分の選択を変えたら、請求日の年月はそのまま・日だけをその締め日区分に合わせて
    /// 補正する。一覧の抽出条件は請求日のみなので、補正の結果
    /// 請求日が変わった場合に限り<see cref="OnClosingDateChanged"/>が自動的に再取得する
    /// （締め日区分自体は一覧の抽出条件に使わないため、請求日が変わらないなら再取得は不要）。
    /// </summary>
    partial void OnSelectedClosingDayChanged(ClosingDayOption? value)
    {
        if (value is not null)
        {
            var reference = ClosingDate is { } current ? DateOnly.FromDateTime(current) : DateOnly.FromDateTime(DateTime.Today);
            ClosingDate = ClosingDateResolver.Resolve(reference.Year, reference.Month, value.Value).ToDateTime(TimeOnly.MinValue);
        }
    }

    /// <summary>
    /// 請求日を変えたら自動的にその条件で再取得する。
    /// <c>DatePicker.SelectedDate</c>は確定した瞬間（Enter／フォーカス離脱／カレンダー選択）にしか
    /// 変化しないため、入力中の1文字ごとにDB照会が走ることはない。
    /// </summary>
    partial void OnClosingDateChanged(DateTime? value) => _ = RefreshAsync();

    /// <summary>
    /// 締め確定。一覧は「これから締める予定」のプレビューではなくなったため、
    /// 実行前に対象件数を提示する確認ダイアログを挟む（<see cref="BillingReleaseViewModel.ReleaseAsync"/>と
    /// 同型）。確定後は<see cref="RefreshListAsync"/>で<c>billings</c>から取り直し、確定した行を
    /// 一覧へ即座に反映する。
    /// </summary>
    [RelayCommand]
    private Task ConfirmAsync() => RunBusyAsync(async () =>
    {
        if (!TryGetConditions(out var closingDay, out var closingDate))
        {
            return;
        }

        var confirm = MessageBox.Show(
            $"締め日区分 {SelectedClosingDay!.Display}・請求日 {closingDate:yyyy/MM/dd} で請求締めを確定しますか？\n" +
            "確定前のプレビューはありません。実行すると対象得意先の売上・入金がまとめて請求データとして確定します。",
            "bmcs_app", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var results = await billingClosingService.ConfirmAsync(closingDay, closingDate);
            var confirmedCount = results.Count(r => r.SkipReason is null);
            var skippedCount = results.Count(r => r.SkipReason is not null);

            await RefreshListAsync();

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
    /// 印刷（F11）。選択中の全行（複数可）をまとめて1つのプレビューに連結して
    /// 印刷する。一覧に載る行は常に確定済みのため、選択件数以外の実行条件は無い。
    /// 印刷履歴は記録しない（<c>docs/report-spec.md</c> 2-2節の方針を維持）。
    /// </summary>
    private bool CanPrint() => SelectedResults.Count > 0;

    [RelayCommand(CanExecute = nameof(CanPrint))]
    private async Task PrintAsync()
    {
        if (SelectedResults.Count == 0)
        {
            return;
        }

        var billingNumbers = SelectedResults.Select(r => r.BillingNumber).ToList();
        var invoices = new List<InvoiceData>();

        foreach (var billingNumber in billingNumbers)
        {
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

            invoices.Add(data);
        }

        var jobName = invoices.Count == 1 ? $"請求書 {invoices[0].BillingNumber}" : $"請求書 {invoices.Count}件";

        windowService.ShowDialog<ReportPreviewDialog, ReportPreviewDialogViewModel, bool>(
            vm => vm.Initialize(ReportKind.Invoice, jobName, () =>
            {
                var document = new FixedDocument();
                foreach (var invoice in invoices)
                {
                    new InvoiceDocumentBuilder(invoice).BuildInto(document);
                }

                return document;
            }));
    }

    private bool TryGetClosingDate(out DateOnly closingDate)
    {
        closingDate = default;

        if (ClosingDate is not { } value)
        {
            StatusMessage = "請求日を入力してください。";
            return false;
        }

        closingDate = DateOnly.FromDateTime(value);
        return true;
    }

    private bool TryGetConditions(out byte closingDay, out DateOnly closingDate)
    {
        closingDay = 0;

        if (!TryGetClosingDate(out closingDate))
        {
            return false;
        }

        if (SelectedClosingDay is null)
        {
            StatusMessage = "締め日を選択してください。";
            return false;
        }

        closingDay = SelectedClosingDay.Value;
        return true;
    }

    private void ApplyResults(IReadOnlyList<InvoiceListItem> results)
    {
        Results.Clear();
        foreach (var result in results)
        {
            Results.Add(result);
        }

        TotalCurrentBillingAmount = results.Sum(r => r.CurrentBillingAmount);
    }
}

/// <summary>締め日区分の選択肢（ComboBox表示用）。</summary>
public sealed record ClosingDayOption(byte Value)
{
    public string Display => Value == 99 ? "末日締め" : $"{Value}日締め";
}
