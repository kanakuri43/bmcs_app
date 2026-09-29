using System.Collections.ObjectModel;
using bmcs_app.Application.Ledger;
using bmcs_app.Application.Master;
using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Services;
using bmcs_app.ViewModels.Common;
using bmcs_app.ViewModels.Receipt;
using bmcs_app.ViewModels.Sales;
using bmcs_app.Views.Common;
using bmcs_app.Views.Receipt;
using bmcs_app.Views.Sales;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bmcs_app.ViewModels.Ledger;

/// <summary>
/// 得意先元帳画面（TODO.md 8-1）。得意先ごとの売上・入金明細と残高推移を時系列表示する。
/// 画面レイアウト・操作方法は旧デモ（bmcs_app.CustomerLedger）を踏襲しつつ、部分消込・複数入金を
/// 扱うため入金額・残高の2列を追加した（項目に過不足がある場合は本プロジェクトを優先）。
///
/// 締め・都度の両区分を同じ画面で扱う（デモは内税明細単位専用だったが、本プロジェクトは
/// 全税単位を対象にする）。残高キャッシュ列は持たず都度集計する（M-11）。
///
/// <see cref="CurrentBalance"/>（TODO.md 8-2、本日時点の残高）は、検索期間（<see cref="PeriodFrom"/>／
/// <see cref="PeriodTo"/>）とは独立に、得意先確定時と表示（<see cref="SearchAsync"/>）実行時に毎回
/// <see cref="CustomerLedgerQueryService.GetBalanceAsOfAsync"/> を呼んで都度計算する
/// （M-11「残高キャッシュ列を持たない」。ウィンドウを開いたまま他画面の更新を自動検知する
/// 仕組みは持たない。再検索・再オープンのたびに必ず最新値になることが「常時表示」の意味。
/// docs/design_document.md 21章）。得意先・<see cref="PeriodFrom"/>・<see cref="PeriodTo"/>の
/// いずれかを変更すると<see cref="TriggerAutoRefresh"/>経由で表示が自動実行される
/// （2026-09-17。専用ボタンは撤去済み。F5キーは手動再表示用に残す）。
///
/// 伝票プレビュー（TODO.md 8-3、<see cref="OpenSlipPreview"/>）は行を <c>Enter</c>／ダブルクリック
/// （<see cref="Behaviors.RowActivationBehavior"/>）で活性化すると、対応する売上入力・入金入力・
/// 明細入金画面を <see cref="Services.WindowService.Show{TWindow, TViewModel}"/> の <c>configure</c>
/// 経由で読み取り専用（プレビュー）表示する。印刷（Phase 10、元帳自体の帳票プレビュー）は
/// 別物で本タスクのスコープ外（ボタンは枠のみ用意し無効化する）。
///
/// 請求集約元（TODO.md 12-E、docs/design_document.md 28-2節 #6）は取引履歴のみモードになる
/// （<see cref="IsTransactionHistoryOnly"/>）。残高・繰越・現在残高は表示せず（<see cref="IsBalanceVisible"/>）、
/// 案内文（<see cref="AggregationNotice"/>）で請求集約先を案内する。請求集約先は従来どおりの
/// 表示だが、グループ内の請求集約元の伝票も含めて合算表示する（<see cref="CustomerLedgerQueryService"/>
/// がグループ展開して渡す）。
/// </summary>
public partial class CustomerLedgerViewModel(
    CustomerLedgerQueryService ledgerQueryService,
    CustomerService customerService,
    WindowService windowService) : ViewModelBase
{
    private Customer? _customer;

    public ObservableCollection<CustomerLedgerLineViewModel> Lines { get; } = [];

    [ObservableProperty]
    public partial string CustomerCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CustomerName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TaxUnitDisplay { get; set; } = string.Empty;

    /// <summary>本日時点の残高（TODO.md 8-2）。検索期間を変えても変化しない。</summary>
    [ObservableProperty]
    public partial decimal CurrentBalance { get; set; }

    [ObservableProperty]
    public partial DateTime? PeriodFrom { get; set; } = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

    partial void OnPeriodFromChanged(DateTime? value) => TriggerAutoRefresh();

    [ObservableProperty]
    public partial DateTime? PeriodTo { get; set; } = DateTime.Today;

    partial void OnPeriodToChanged(DateTime? value) => TriggerAutoRefresh();

    [ObservableProperty]
    public partial decimal OpeningBalance { get; set; }

    [ObservableProperty]
    public partial decimal SalesTotal { get; set; }

    [ObservableProperty]
    public partial decimal TaxTotal { get; set; }

    [ObservableProperty]
    public partial decimal ReceiptTotal { get; set; }

    [ObservableProperty]
    public partial decimal ClosingBalance { get; set; }

    /// <summary>請求集約元の取引履歴のみモード（TODO.md 12-E）。</summary>
    [ObservableProperty]
    public partial bool IsTransactionHistoryOnly { get; set; }

    /// <summary>残高・繰越関連の表示切り替え用（<see cref="IsTransactionHistoryOnly"/> の反転）。</summary>
    [ObservableProperty]
    public partial bool IsBalanceVisible { get; set; } = true;

    /// <summary>請求集約元のとき表示する案内文（docs/design_document.md 28-2節 #6）。</summary>
    [ObservableProperty]
    public partial string AggregationNotice { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "得意先を指定してください（条件を変更すると自動的に表示します）。";

    [RelayCommand]
    private void OpenCustomerSearch()
    {
        var customer = windowService.ShowDialog<CustomerSearchDialog, CustomerSearchDialogViewModel, Customer>();
        if (customer is not null)
        {
            _ = RunBusyAsync(() => ApplyCustomerAsync(customer));
        }
    }

    [RelayCommand]
    private Task LookupCustomerByCodeAsync() => RunBusyAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(CustomerCode))
        {
            return;
        }

        var customer = await customerService.GetByCodeAsync(CustomerCode);
        if (customer is null)
        {
            StatusMessage = $"得意先コード「{CustomerCode}」が見つかりません。";
            return;
        }

        await ApplyCustomerAsync(customer);
    });

    private async Task ApplyCustomerAsync(Customer customer)
    {
        // 得意先を切り替えたら、別得意先の元帳が画面に残らないようにクリアする。
        _customer = customer;
        CustomerCode = customer.CustomerCode;
        CustomerName = customer.CustomerName;
        TaxUnitDisplay = BuildTaxUnitDisplay(customer);
        Lines.Clear();
        ResetTotals();

        // 得意先確定はすでに呼び出し元の RunBusyAsync 内なので、RefreshLedgerAsync を直接呼ぶ
        // （SearchCommand 経由だと IsBusy の多重実行抑止に引っかかり無視されてしまう）。
        await RefreshLedgerAsync();
    }

    private async Task RefreshCurrentBalanceAsync(string customerCode)
        => CurrentBalance = await ledgerQueryService.GetBalanceAsOfAsync(customerCode, DateOnly.FromDateTime(DateTime.Today)) ?? 0m;

    /// <summary>
    /// 得意先・開始日付・終了日付のいずれかを変更すると自動的に実行される（TODO.md 8-1、
    /// 2026-09-17変更）。専用の「表示」ボタンは、自動実行により不要になったため撤去した。
    /// F5キーバインドは、他画面での更新後に手動で再表示したい場合に備えて残す。
    /// </summary>
    [RelayCommand]
    private Task SearchAsync() => RunBusyAsync(RefreshLedgerAsync);

    private void TriggerAutoRefresh()
    {
        if (_customer is null)
        {
            return;
        }

        _ = SearchAsync();
    }

    private async Task RefreshLedgerAsync()
    {
        if (_customer is null)
        {
            StatusMessage = "得意先を指定してください。";
            return;
        }

        if (PeriodFrom is not { } periodFromValue || PeriodTo is not { } periodToValue)
        {
            StatusMessage = "期間を入力してください。";
            return;
        }

        var periodFrom = DateOnly.FromDateTime(periodFromValue);
        var periodTo = DateOnly.FromDateTime(periodToValue);

        if (periodFrom > periodTo)
        {
            StatusMessage = "期間の開始日は終了日以前にしてください。";
            return;
        }

        var result = await ledgerQueryService.GetAsync(_customer.CustomerCode, periodFrom, periodTo);
        if (result is null)
        {
            StatusMessage = "得意先が見つかりません。";
            return;
        }

        IsTransactionHistoryOnly = result.IsTransactionHistoryOnly;
        IsBalanceVisible = !result.IsTransactionHistoryOnly;
        AggregationNotice = result.IsTransactionHistoryOnly
            ? $"「{_customer.CustomerName}」は請求集約元です。請求・消費税・残高は請求集約先「{_customer.BillingCustomerCode}」に集約されています。この画面には売上（税抜）のみを表示します。"
            : string.Empty;

        Lines.Clear();
        foreach (var entry in result.Entries)
        {
            Lines.Add(new CustomerLedgerLineViewModel(entry, result.IsTransactionHistoryOnly));
        }

        OpeningBalance = result.OpeningBalance;
        SalesTotal = result.SalesTotal;
        TaxTotal = result.TaxTotal;
        ReceiptTotal = result.ReceiptTotal;
        ClosingBalance = result.ClosingBalance;

        // 表示のたびに本日時点の残高も再計算する（都度集計。M-11）。取引履歴のみモード
        // （請求集約元）は残高を管理しないため呼ばない（GetBalanceAsOfAsyncはnullを返す）。
        if (!result.IsTransactionHistoryOnly)
        {
            await RefreshCurrentBalanceAsync(_customer.CustomerCode);
        }
        else
        {
            CurrentBalance = 0m;
        }

        StatusMessage = $"{Lines.Count}行を表示しました。";
    }

    private void ResetTotals()
    {
        OpeningBalance = 0m;
        SalesTotal = 0m;
        TaxTotal = 0m;
        ReceiptTotal = 0m;
        ClosingBalance = 0m;
        CurrentBalance = 0m;
        IsTransactionHistoryOnly = false;
        IsBalanceVisible = true;
        AggregationNotice = string.Empty;
    }

    private static string BuildTaxUnitDisplay(Customer customer) => customer.TaxUnit switch
    {
        TaxUnit.Invoice => $"請求単位・{ClosingDayLabel(customer.ClosingDay)}",
        TaxUnit.Slip => $"伝票単位・{ClosingDayLabel(customer.ClosingDay)}",
        TaxUnit.Line => "内税明細単位（都度）",
        _ => string.Empty,
    };

    private static string ClosingDayLabel(byte closingDay) => closingDay switch
    {
        99 => "末日締め",
        0 => "都度",
        _ => $"{closingDay}日締め",
    };

    /// <summary>
    /// 行の活性化（<c>Enter</c>／ダブルクリック。<see cref="Behaviors.RowActivationBehavior"/>）から
    /// 伝票プレビュー（TODO.md 8-3）を開く。開く画面は行の種別で決まる:
    /// <list type="bullet">
    /// <item><see cref="LedgerEntryKind.Sales"/>で<see cref="CustomerLedgerEntry.SalesSlipNumber"/>が
    /// あれば売上入力。無ければ消込証跡の継続行（D-3。都度得意先のみ）で、実体は
    /// <see cref="CustomerLedgerEntry.ReceiptSlipNumber"/>（detail_receiptの番号）なので明細入金。</item>
    /// <item><see cref="LedgerEntryKind.Receipt"/>は得意先の税区分で分岐（<c>ReceiptSlipNumber</c>は
    /// <c>receipt</c>／<c>detail_receipt</c>のどちらの番号かを区別する情報を持たないため）。</item>
    /// <item><see cref="LedgerEntryKind.ConsumptionTax"/>は伝票単位（<c>SalesSlipNumber</c>あり）のみ
    /// 売上入力。請求単位の確定額・未締め仮計算・<see cref="LedgerEntryKind.OpeningBalance"/>は
    /// 辿れる伝票が無いため開かない。</item>
    /// </list>
    /// </summary>
    [RelayCommand]
    private void OpenSlipPreview(CustomerLedgerLineViewModel? line)
    {
        if (line is null)
        {
            return;
        }

        var entry = line.Entry;
        switch (entry.Kind)
        {
            case LedgerEntryKind.Sales when entry.SalesSlipNumber is { } salesSlipNumber:
                OpenSalesPreview(salesSlipNumber);
                break;

            case LedgerEntryKind.Sales when entry.ReceiptSlipNumber is { } detailReceiptNumber:
                // 消込証跡の継続行（D-3）。SalesSlipNumberがnullで実体はdetail_receiptへのポインタ。
                OpenDetailReceiptPreview(detailReceiptNumber);
                break;

            case LedgerEntryKind.Receipt when entry.ReceiptSlipNumber is { } receiptSlipNumber:
                if (_customer?.TaxUnit == TaxUnit.Line)
                {
                    OpenDetailReceiptPreview(receiptSlipNumber);
                }
                else
                {
                    OpenReceiptPreview(receiptSlipNumber);
                }
                break;

            case LedgerEntryKind.ConsumptionTax when entry.SalesSlipNumber is { } taxSalesSlipNumber:
                OpenSalesPreview(taxSalesSlipNumber);
                break;

            default:
                StatusMessage = "この行に対応する伝票はありません。";
                break;
        }
    }

    private void OpenSalesPreview(string salesSlipNumber)
        => windowService.Show<SalesEntryWindow, SalesEntryViewModel>(vm => vm.PreviewSlipNumber = salesSlipNumber);

    private void OpenReceiptPreview(string receiptSlipNumber)
        => windowService.Show<ReceiptEntryWindow, ReceiptEntryViewModel>(vm => vm.PreviewSlipNumber = receiptSlipNumber);

    private void OpenDetailReceiptPreview(string detailReceiptNumber)
        => windowService.Show<DetailReceiptEntryWindow, DetailReceiptEntryViewModel>(vm => vm.PreviewSlipNumber = detailReceiptNumber);
}
