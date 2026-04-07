using bmcs_app.Core.Models;

namespace bmcs_app.Core.Interfaces;

public interface IClosingRepository
{
    Task InvoiceClosingAsync(byte closingDay, DateOnly processDate, int? customerId = null);
    Task InvoiceClosingCancelAsync(DateOnly processDate, int? customerId = null);
    Task ArClosingAsync(DateOnly processDate, int? customerId = null);
    Task ArClosingCancelAsync(DateOnly processDate, int? customerId = null);

    Task<IEnumerable<InvoiceHistorySummary>> GetInvoiceHistorySummariesAsync();
    Task<IEnumerable<ArHistorySummary>>      GetArHistorySummariesAsync();
    Task<IEnumerable<InvoiceHeader>>        GetInvoiceHeadersAsync(DateOnly invoiceDate, byte closingDay, int? customerId = null);
    Task<IEnumerable<InvoiceSlipDetail>>    GetInvoiceSlipDetailsAsync(DateOnly invoiceDate, int customerId);
    Task<IEnumerable<InvoiceTaxGroup>>      GetInvoiceTaxGroupsAsync(DateOnly invoiceDate, int customerId);
    Task<IEnumerable<InvoiceReceiptDetail>> GetInvoiceReceiptDetailsAsync(DateOnly invoiceDate, int customerId);
}
