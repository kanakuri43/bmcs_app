using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Documents;
using bmcs_app.Application.Billing;
using bmcs_app.Reports;
using bmcs_app.Services;
using bmcs_app.ViewModels.Common;
using bmcs_app.Views.Billing;
using bmcs_app.Views.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bmcs_app.ViewModels.Billing;

/// <summary>
/// 請求締め処理画面。確定済み請求データの一覧・再印刷が主役で、締めの実行は
/// 締確定ダイアログ（<see cref="BillingClosingConfirmDialogViewModel"/>。締日／個別指定の選択と請求日の入力）に分けた。
/// この画面の入力は一覧の抽出条件である請求日のみ（年を4桁で入力し、その年の請求締め済み日付から選ぶ）。
///
/// **一覧は「これから締めたらどうなるか」の集計プレビューではなく、`billings`に実在する
/// 確定済み請求データを請求日（<c>billing_date</c>）だけで抽出したもの。** 締め日区分は抽出条件に使わない（<c>billings</c>に締め日区分を保持する
/// 列が無いため）。未確定の請求日を選んだときは1件も表示しない。**確定前の集計プレビュー
/// 機能はこの画面から廃止した**（過去に締めた請求書をいつでも再照会・再印刷できることを
/// 優先した）。これに伴い、締め確定（<see cref="ConfirmCommand"/>）は
/// 実行前の確認ダイアログはダイアログ側で挟む（<see cref="BillingReleaseViewModel"/>と同型）。
///
/// 一覧は拡張選択（Ctrl/Shiftクリック。チェックボックス列は持たない）
/// で複数行を選べる。選択した行はまとめて1つのプレビューダイアログで印刷でき、これが
/// 請求書の再発行手段になる（<see cref="PrintCommand"/>）。
/// </summary>
public partial class BillingClosingViewModel(
    InvoiceService invoiceService,
    WindowService windowService) : ViewModelBase
{
    /// <summary>
    /// 請求日の選択対象とする年（4桁入力、初期値は今年）。4桁の年として有効になるたびに
    /// その年の請求締め済み日付を<see cref="BillingDates"/>へ読み込み直す。
    /// </summary>
    [ObservableProperty]
    public partial string YearText { get; set; } = DateTime.Today.Year.ToString();

    /// <summary>選択中の年に請求締め済みの請求日（コンボボックスの選択肢）。</summary>
    public ObservableCollection<DateTime> BillingDates { get; } = [];

    /// <summary>
    /// 一覧（<see cref="Results"/>）の抽出条件（<c>billing_date</c>一致）。<see cref="BillingDates"/>から選ぶ
    /// （初期値は今日に一番近い日付）。該当日が無い年は null（一覧は空）。
    /// </summary>
    [ObservableProperty]
    public partial DateTime? ClosingDate { get; set; }

    /// <summary>指定した請求日の確定済み請求データ一覧（<see cref="InvoiceService.GetByBillingDateAsync"/>）。</summary>
    public ObservableCollection<InvoiceListItem> Results { get; } = [];

    /// <summary>選択中の行（拡張選択、複数可）。印刷(F11)の対象。</summary>
    public ObservableCollection<InvoiceListItem> SelectedResults { get; } = [];

    [ObservableProperty]
    public partial decimal TotalCurrentBillingAmount { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    /// <summary>画面表示時に既定条件での一覧を表示する（対象取得ボタンは持たない）。</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        SelectedResults.CollectionChanged -= OnSelectedResultsChanged;
        SelectedResults.CollectionChanged += OnSelectedResultsChanged;

        await ReloadDatesAsync();
    }

    partial void OnYearTextChanged(string value)
    {
        if (value.Length == 4 && int.TryParse(value, out var year) && year >= 1900)
        {
            _ = ReloadDatesAsync();
        }
    }

    /// <summary>
    /// 選択中の年の請求日を読み込み直し、<paramref name="preferred"/>（無ければ今日に一番近い日付）を選択する。
    /// 選択が変わると<see cref="OnClosingDateChanged"/>が一覧を再取得する。
    /// </summary>
    private async Task ReloadDatesAsync(DateTime? preferred = null)
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

            ClosingDate = preferred is { } p && dates.Contains(p)
                ? p
                : dates.Count == 0 ? null : dates.MinBy(d => Math.Abs((d - DateTime.Today).Days));
        }
        catch (Exception ex)
        {
            StatusMessage = $"取得エラー: {ex.Message}";
        }
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

    /// <summary><see cref="RefreshAsync"/> の本体。</summary>
    private async Task RefreshListAsync()
    {
        if (ClosingDate is not { } closingDateValue)
        {
            ApplyResults([]);
            StatusMessage = "この年に請求締め済みの日付はありません。";
            return;
        }

        try
        {
            var closingDate = DateOnly.FromDateTime(closingDateValue);
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
    /// 請求日を変えたら自動的にその条件で再取得する。
    /// </summary>
    partial void OnClosingDateChanged(DateTime? value) => _ = RefreshAsync();

    /// <summary>
    /// 締め確定。締確定ダイアログ（締日／個別指定の選択と実行確認を含む）を開き、実行されたら
    /// ダイアログで指定した請求日の確定済み一覧へ切り替えて今回の結果を表示する。
    /// </summary>
    [RelayCommand]
    private async Task ConfirmAsync()
    {
        var outcome = windowService.ShowDialog<BillingClosingConfirmDialog, BillingClosingConfirmDialogViewModel, BillingClosingOutcome>(
            vm => vm.ClosingDate = ClosingDate ?? DateTime.Today);
        if (outcome is null)
        {
            return;
        }

        YearText = outcome.ClosingDate.Year.ToString();
        await ReloadDatesAsync(outcome.ClosingDate);
        await RefreshAsync(); // 選択が変わらない場合も確定直後の一覧へ更新する

        StatusMessage = $"請求締めを確定しました（確定 {outcome.ConfirmedCount}件／スキップ {outcome.SkippedCount}件）。" +
            (outcome.ConfirmedCount > 0 ? "印刷したい行を選択し「選択行を印刷」してください。" : "");
    }

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
