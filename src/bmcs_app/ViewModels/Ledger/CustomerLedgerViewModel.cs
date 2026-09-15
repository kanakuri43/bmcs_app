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
/// <see cref="CurrentBalance"/>（TODO.md 8-2、本日時点の残高）は、検索期間（<see cref="PeriodFromText"/>／
/// <see cref="PeriodToText"/>）とは独立に、得意先確定時と表示(F5)実行時に毎回
/// <see cref="CustomerLedgerQueryService.GetBalanceAsOfAsync"/> を呼んで都度計算する
/// （M-11「残高キャッシュ列を持たない」。ウィンドウを開いたまま他画面の更新を自動検知する
/// 仕組みは持たない。再検索・再オープンのたびに必ず最新値になることが「常時表示」の意味。
/// docs/design_document.md 21章）。
///
/// 伝票プレビュー（TODO.md 8-3、<see cref="OpenSlipPreview"/>）は行を <c>Enter</c>／ダブルクリック
/// （<see cref="Behaviors.RowActivationBehavior"/>）で活性化すると、対応する売上入力・入金入力・
/// 明細入金画面を <see cref="Services.WindowService.Show{TWindow, TViewModel}"/> の <c>configure</c>
/// 経由で読み取り専用（プレビュー）表示する。印刷（Phase 10、元帳自体の帳票プレビュー）は
/// 別物で本タスクのスコープ外（ボタンは枠のみ用意し無効化する）。
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
    public partial string PeriodFromText { get; set; } =
        new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1).ToString("yyyy/MM/dd");

    [ObservableProperty]
    public partial string PeriodToText { get; set; } = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy/MM/dd");

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

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "得意先と期間を指定して表示(F5)してください。";

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
        await RefreshCurrentBalanceAsync(customer.CustomerCode);
        StatusMessage = $"得意先: {customer.CustomerName}（表示(F5)で元帳を表示します）";
    }

    private async Task RefreshCurrentBalanceAsync(string customerCode)
        => CurrentBalance = await ledgerQueryService.GetBalanceAsOfAsync(customerCode, DateOnly.FromDateTime(DateTime.Today)) ?? 0m;

    /// <summary>表示（F5）。</summary>
    [RelayCommand]
    private Task SearchAsync() => RunBusyAsync(async () =>
    {
        if (_customer is null)
        {
            StatusMessage = "得意先を指定してください。";
            return;
        }

        if (!DateOnly.TryParseExact(PeriodFromText, "yyyy/MM/dd", out var periodFrom)
            || !DateOnly.TryParseExact(PeriodToText, "yyyy/MM/dd", out var periodTo))
        {
            StatusMessage = "期間の日付を確認してください。";
            return;
        }

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

        Lines.Clear();
        foreach (var entry in result.Entries)
        {
            Lines.Add(new CustomerLedgerLineViewModel(entry));
        }

        OpeningBalance = result.OpeningBalance;
        SalesTotal = result.SalesTotal;
        TaxTotal = result.TaxTotal;
        ReceiptTotal = result.ReceiptTotal;
        ClosingBalance = result.ClosingBalance;

        // 表示(F5)のたびに本日時点の残高も再計算する（都度集計。M-11）。
        await RefreshCurrentBalanceAsync(_customer.CustomerCode);

        StatusMessage = $"{Lines.Count}行を表示しました。";
    });

    private void ResetTotals()
    {
        OpeningBalance = 0m;
        SalesTotal = 0m;
        TaxTotal = 0m;
        ReceiptTotal = 0m;
        ClosingBalance = 0m;
        CurrentBalance = 0m;
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
