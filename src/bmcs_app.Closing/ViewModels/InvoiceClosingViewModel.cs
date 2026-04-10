using System.Collections.ObjectModel;
using Prism.Commands;
using Prism.Mvvm;
using bmcs_app.Closing.Services;
using bmcs_app.Core.Interfaces;
using bmcs_app.Core.Models;
using bmcs_app.Infrastructure;

namespace bmcs_app.Closing.ViewModels;

public record ClosingDayOption(byte Day)
{
    public string Label => Day is 0 or 99 ? "末日" : $"{Day}日";
}

public class InvoiceClosingViewModel : BindableBase
{
    private readonly IClosingRepository _repo;
    private readonly List<Customer>     _customers;
    private CompanyInfo                 _companyInfo = new();

    // ===== 締め日 =====

    public List<ClosingDayOption> ClosingDays { get; } = new();

    private ClosingDayOption? _selectedClosingDay;
    public ClosingDayOption? SelectedClosingDay
    {
        get => _selectedClosingDay;
        set => SetProperty(ref _selectedClosingDay, value);
    }

    // ===== 処理日付 =====

    private DateTime? _processDate = EndOfMonth(DateTime.Today);
    public DateTime? ProcessDate
    {
        get => _processDate;
        set => SetProperty(ref _processDate, value);
    }

    // ===== 得意先 =====

    private bool _isAllCustomers = true;
    public bool IsAllCustomers
    {
        get => _isAllCustomers;
        set { SetProperty(ref _isAllCustomers, value); RaisePropertyChanged(nameof(IsSpecificCustomer)); }
    }

    public bool IsSpecificCustomer
    {
        get => !_isAllCustomers;
        set { IsAllCustomers = !value; }
    }

    private string _customerCode = string.Empty;
    public string CustomerCode
    {
        get => _customerCode;
        set => SetProperty(ref _customerCode, value);
    }

    private string _customerName = string.Empty;
    public string CustomerName
    {
        get => _customerName;
        set => SetProperty(ref _customerName, value);
    }

    // ===== 集計履歴 =====

    public ObservableCollection<InvoiceHistorySummary> HistoryItems { get; } = new();

    private InvoiceHistorySummary? _selectedHistoryItem;
    public InvoiceHistorySummary? SelectedHistoryItem
    {
        get => _selectedHistoryItem;
        set
        {
            SetProperty(ref _selectedHistoryItem, value);
            PrintCommand.RaiseCanExecuteChanged();
            CancelAggregationCommand.RaiseCanExecuteChanged();
        }
    }

    // ===== ステータス =====

    private string _statusMessage = string.Empty;
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    // ===== 確認ダイアログ（View がセット） =====

    /// <summary>締め解除前の確認。true を返した場合のみ処理を続行する。</summary>
    public Func<string, bool>? ConfirmCancel { get; set; }

    // ===== コマンド =====

    public DelegateCommand AggregateCommand         { get; }
    public DelegateCommand CancelAggregationCommand { get; }
    public DelegateCommand PrintCommand             { get; }

    public InvoiceClosingViewModel(IEnumerable<byte> closingDays, IEnumerable<Customer> customers, IClosingRepository repo)
    {
        _repo      = repo;
        _customers = customers.ToList();

        foreach (var d in closingDays.Distinct().OrderBy(d => d))
            ClosingDays.Add(new ClosingDayOption(d));

        SelectedClosingDay = ClosingDays.FirstOrDefault();

        AggregateCommand         = new DelegateCommand(async () => await OnAggregateAsync());
        CancelAggregationCommand = new DelegateCommand(async () => await OnCancelAggregationAsync(),
                                                       () => SelectedHistoryItem is not null);
        PrintCommand             = new DelegateCommand(async () => await OnPrintAsync(),
                                                       () => SelectedHistoryItem is not null);

        _ = LoadHistoryAsync();
    }

    public void SetCompanyInfo(CompanyInfo info) => _companyInfo = info;

    private static DateTime EndOfMonth(DateTime d)
        => new DateTime(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month));

    private async Task LoadHistoryAsync()
    {
        try
        {
            var items = await _repo.GetInvoiceHistorySummariesAsync();
            HistoryItems.Clear();
            foreach (var item in items)
                HistoryItems.Add(item);
        }
        catch (Exception ex)
        {
            StatusMessage = $"履歴取得エラー: {ex.Message}";
        }
    }

    private async Task OnAggregateAsync()
    {
        if (SelectedClosingDay is null) { StatusMessage = "締め日を選択してください。"; return; }
        if (ProcessDate is null)        { StatusMessage = "処理日付を入力してください。"; return; }

        int? customerId = ResolveCustomerId();
        if (IsSpecificCustomer && customerId is null) { StatusMessage = "得意先コードが見つかりません。"; return; }

        try
        {
            StatusMessage = "請求集計中...";
            await _repo.InvoiceClosingAsync(
                SelectedClosingDay.Day,
                DateOnly.FromDateTime(ProcessDate.Value),
                customerId);
            StatusMessage = $"請求集計が完了しました。（{ProcessDate.Value:yyyy/MM/dd}）";
            await LoadHistoryAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"エラー: {ex.Message}";
        }
    }

    private async Task OnCancelAggregationAsync()
    {
        if (SelectedHistoryItem is null) return;

        var message = $"{SelectedHistoryItem.InvoiceDateLabel} の請求集計を解除します。よろしいですか？";
        if (ConfirmCancel is not null && !ConfirmCancel(message)) return;

        int? customerId = ResolveCustomerId();
        if (IsSpecificCustomer && customerId is null) { StatusMessage = "得意先コードが見つかりません。"; return; }

        try
        {
            StatusMessage = "締め解除中...";
            await _repo.InvoiceClosingCancelAsync(SelectedHistoryItem.InvoiceDate, customerId);
            StatusMessage = $"締め解除が完了しました。（{SelectedHistoryItem.InvoiceDateLabel}）";
            SelectedHistoryItem = null;
            await LoadHistoryAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"エラー: {ex.Message}";
        }
    }

    private async Task OnPrintAsync()
    {
        if (SelectedHistoryItem is null) return;

        int? customerId = ResolveCustomerId();
        if (IsSpecificCustomer && customerId is null) { StatusMessage = "得意先コードが見つかりません。"; return; }

        try
        {
            StatusMessage = "印刷データ取得中...";

            var invoiceDate = SelectedHistoryItem.InvoiceDate;
            var headers     = (await _repo.GetInvoiceHeadersAsync(invoiceDate, customerId)).ToList();

            if (headers.Count == 0)
            {
                StatusMessage = "印刷する請求データがありません。";
                return;
            }

            var printDataList = new List<InvoicePrintData>();
            foreach (var h in headers)
            {
                var slips     = (await _repo.GetInvoiceSlipDetailsAsync(invoiceDate, h.CustomerId)).ToList();
                var taxGroups = (await _repo.GetInvoiceTaxGroupsAsync(invoiceDate, h.CustomerId)).ToList();
                var receipts  = (await _repo.GetInvoiceReceiptDetailsAsync(invoiceDate, h.CustomerId)).ToList();

                printDataList.Add(BuildPrintData(h, slips, taxGroups, receipts));
            }

            StatusMessage = "印刷中...";
            InvoicePrintHelper.Print(printDataList);
            StatusMessage = $"印刷が完了しました。（{invoiceDate:yyyy/MM/dd}  {headers.Count} 件）";
        }
        catch (Exception ex)
        {
            StatusMessage = $"エラー: {ex.Message}";
        }
    }

    private InvoicePrintData BuildPrintData(
        InvoiceHeader header,
        List<bmcs_app.Core.Models.InvoiceSlipDetail> slips,
        List<bmcs_app.Core.Models.InvoiceTaxGroup> taxGroups,
        List<bmcs_app.Core.Models.InvoiceReceiptDetail> receipts)
    {
        static string Fmt(decimal v) => v.ToString("#,##0");

        var data = new InvoicePrintData
        {
            CustomerCode        = header.CustomerCode,
            CustomerName        = header.CustomerName,
            CustomerPostalCode  = header.CustomerPostalCode ?? "",
            CustomerAddress1    = header.CustomerAddress1   ?? "",
            CustomerAddress2    = header.CustomerAddress2   ?? "",
            InvoiceDate         = header.InvoiceDate.ToString("yyyy年MM月dd日"),
            PreviousAmountStr   = Fmt(header.PreviousInvoiceAmount),
            ReceiptAmountStr    = Fmt(header.ReceiptAmount),
            SalesStandardStr    = Fmt(header.SalesAmountStandard),
            SalesReducedStr     = Fmt(header.SalesAmountReduced),
            TaxStandardStr      = Fmt(header.TaxAmountStandard),
            TaxReducedStr       = Fmt(header.TaxAmountReduced),
            CurrentAmountStr    = Fmt(header.CurrentInvoiceAmount),
            SalesTotalStr       = Fmt(header.SalesAmountStandard + header.SalesAmountReduced),
            TaxTotalStr         = Fmt(header.TaxAmountStandard   + header.TaxAmountReduced),
            CompanyName         = _companyInfo.Name,
            CompanyAddress      = _companyInfo.Address,
            CompanyPhone        = _companyInfo.Phone,
            CompanyFax          = _companyInfo.Fax,
            CompanyInvoiceRegNo = _companyInfo.InvoiceRegistrationNo,
        };

        var saleLines = slips.Select(s =>
        {
            var baseName = s.TaxRateType == 2 ? $"* {s.ProductName}" : s.ProductName;
            var desc     = string.IsNullOrWhiteSpace(s.LineRemarks)
                ? baseName
                : $"{baseName}　{s.LineRemarks}";
            return new InvoiceMixedLine
            {
                SortDate     = s.SaleDate,
                SortSlipNo   = s.SaleNo,
                SortLineNo   = s.LineNo,
                DateStr      = s.SaleDate.ToString("yyyy/MM/dd"),
                SlipNo       = s.SaleNo,
                Description  = desc,
                QuantityStr  = s.Quantity.ToString("#,##0.##"),
                UnitPriceStr = Fmt(s.UnitPrice),
                AmountStr    = Fmt(s.LineAmount),
            };
        });

        var receiptLines = receipts.Select(r =>
        {
            var desc = string.IsNullOrWhiteSpace(r.LineRemarks)
                ? r.PaymentMethodName
                : $"{r.PaymentMethodName}　{r.LineRemarks}";
            return new InvoiceMixedLine
            {
                SortDate     = r.ReceiptDate,
                SortSlipNo   = r.ReceiptNo,
                SortLineNo   = r.LineNo,
                DateStr      = r.ReceiptDate.ToString("yyyy/MM/dd"),
                SlipNo       = r.ReceiptNo,
                Description  = desc,
                QuantityStr  = "",
                UnitPriceStr = "",
                AmountStr    = Fmt(r.Amount),
            };
        });

        data.MixedLines = saleLines
            .Concat(receiptLines)
            .OrderBy(l => l.SortDate)
            .ThenBy(l => l.SortSlipNo)
            .ThenBy(l => l.SortLineNo)
            .ToList();

        data.TaxBreakdowns = taxGroups.Select(g =>
        {
            var rateStr = (g.AppliedTaxRate * 100).ToString("0") + "%";
            var label   = g.TaxRateType == 2
                ? $"{rateStr}対象（軽減税率）"
                : g.TaxTypeId == 2
                    ? $"{rateStr}内税"
                    : $"{rateStr}対象";
            return new InvoiceTaxBreakdown
            {
                Label             = label,
                TaxExcludedAmount = Fmt(g.TaxExcluded),
                TaxAmount         = Fmt(g.TaxAmount),
            };
        }).ToList();

        return data;
    }

    private int? ResolveCustomerId()
    {
        if (IsAllCustomers) return null;
        return _customers.FirstOrDefault(c => c.CustomerCode == CustomerCode)?.CustomerId;
    }
}
