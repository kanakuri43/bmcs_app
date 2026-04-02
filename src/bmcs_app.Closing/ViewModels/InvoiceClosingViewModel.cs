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

    // ===== ステータス =====

    private string _statusMessage = string.Empty;
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

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
        CancelAggregationCommand = new DelegateCommand(async () => await OnCancelAggregationAsync());
        PrintCommand             = new DelegateCommand(async () => await OnPrintAsync());
    }

    public void SetCompanyInfo(CompanyInfo info) => _companyInfo = info;

    private static DateTime EndOfMonth(DateTime d)
        => new DateTime(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month));

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
        }
        catch (Exception ex)
        {
            StatusMessage = $"エラー: {ex.Message}";
        }
    }

    private async Task OnCancelAggregationAsync()
    {
        if (ProcessDate is null) { StatusMessage = "処理日付を入力してください。"; return; }

        int? customerId = ResolveCustomerId();
        if (IsSpecificCustomer && customerId is null) { StatusMessage = "得意先コードが見つかりません。"; return; }

        try
        {
            StatusMessage = "締め解除中...";
            await _repo.InvoiceClosingCancelAsync(
                DateOnly.FromDateTime(ProcessDate.Value),
                customerId);
            StatusMessage = $"締め解除が完了しました。（{ProcessDate.Value:yyyy/MM/dd}）";
        }
        catch (Exception ex)
        {
            StatusMessage = $"エラー: {ex.Message}";
        }
    }

    private async Task OnPrintAsync()
    {
        if (SelectedClosingDay is null) { StatusMessage = "締め日を選択してください。"; return; }
        if (ProcessDate is null)        { StatusMessage = "処理日付を入力してください。"; return; }

        int? customerId = ResolveCustomerId();
        if (IsSpecificCustomer && customerId is null) { StatusMessage = "得意先コードが見つかりません。"; return; }

        try
        {
            StatusMessage = "印刷データ取得中...";

            var invoiceDate = DateOnly.FromDateTime(ProcessDate.Value);
            var headers     = (await _repo.GetInvoiceHeadersAsync(
                invoiceDate, SelectedClosingDay.Day, customerId)).ToList();

            if (headers.Count == 0)
            {
                StatusMessage = "印刷する請求データがありません。先に請求集計を実行してください。";
                return;
            }

            var printDataList = new List<InvoicePrintData>();
            foreach (var h in headers)
            {
                var slips     = (await _repo.GetInvoiceSlipDetailsAsync(invoiceDate, h.CustomerId)).ToList();
                var taxGroups = (await _repo.GetInvoiceTaxGroupsAsync(invoiceDate, h.CustomerId)).ToList();

                printDataList.Add(BuildPrintData(h, slips, taxGroups));
            }

            StatusMessage = "印刷中...";
            InvoicePrintHelper.Print(printDataList);
            StatusMessage = $"印刷が完了しました。（{ProcessDate.Value:yyyy/MM/dd}  {headers.Count} 件）";
        }
        catch (Exception ex)
        {
            StatusMessage = $"エラー: {ex.Message}";
        }
    }

    private InvoicePrintData BuildPrintData(
        InvoiceHeader header,
        List<bmcs_app.Core.Models.InvoiceSlipDetail> slips,
        List<bmcs_app.Core.Models.InvoiceTaxGroup> taxGroups)
    {
        static string Fmt(decimal v) => v.ToString("#,##0");

        var data = new InvoicePrintData
        {
            CustomerName        = header.CustomerName,
            InvoiceDate         = header.InvoiceDate.ToString("yyyy年MM月dd日"),
            ClosingDayLabel     = SelectedClosingDay!.Label,
            PreviousAmountStr   = Fmt(header.PreviousInvoiceAmount),
            ReceiptAmountStr    = Fmt(header.ReceiptAmount),
            SalesStandardStr    = Fmt(header.SalesAmountStandard),
            SalesReducedStr     = Fmt(header.SalesAmountReduced),
            TaxStandardStr      = Fmt(header.TaxAmountStandard),
            TaxReducedStr       = Fmt(header.TaxAmountReduced),
            CurrentAmountStr    = Fmt(header.CurrentInvoiceAmount),
            CompanyName         = _companyInfo.Name,
            CompanyAddress      = _companyInfo.Address,
            CompanyPhone        = _companyInfo.Phone,
            CompanyFax          = _companyInfo.Fax,
            CompanyInvoiceRegNo = _companyInfo.InvoiceRegistrationNo,
        };

        data.Lines = slips.Select(s => new InvoiceSlipLine
        {
            SaleDate    = s.SaleDate.ToString("yyyy/MM/dd"),
            SaleNo      = s.SaleNo,
            Remarks     = s.Remarks ?? "",
            TaxExcluded = Fmt(s.TaxExcluded),
            TaxAmount   = Fmt(s.TaxAmount),
        }).ToList();

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
