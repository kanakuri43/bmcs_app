namespace bmcs_app.Closing.Services;

public class InvoicePrintData
{
    public string CustomerName        { get; set; } = "";
    public string CustomerPostalCode  { get; set; } = "";
    public string CustomerAddress1    { get; set; } = "";
    public string CustomerAddress2    { get; set; } = "";
    public string InvoiceDate         { get; set; } = "";
    public string ClosingDayLabel     { get; set; } = "";

    // 集計（invoice_headers）
    public string PreviousAmountStr   { get; set; } = "";
    public string ReceiptAmountStr    { get; set; } = "";
    public string SalesStandardStr    { get; set; } = "";
    public string SalesReducedStr     { get; set; } = "";
    public string TaxStandardStr      { get; set; } = "";
    public string TaxReducedStr       { get; set; } = "";
    public string CurrentAmountStr    { get; set; } = "";

    // 自社情報
    public string CompanyName         { get; set; } = "";
    public string CompanyAddress      { get; set; } = "";
    public string CompanyPhone        { get; set; } = "";
    public string CompanyFax          { get; set; } = "";
    public string CompanyInvoiceRegNo { get; set; } = "";

    // 明細（今期売上伝票）
    public List<InvoiceSlipLine>     Lines         { get; set; } = new();

    // 明細（今期入金伝票）
    public List<InvoiceReceiptLine>  ReceiptLines  { get; set; } = new();

    // 税率別集計（インボイス制度）
    public List<InvoiceTaxBreakdown> TaxBreakdowns { get; set; } = new();
}

public class InvoiceSlipLine
{
    public string SaleDate    { get; set; } = "";
    public string SaleNo      { get; set; } = "";
    public string Remarks     { get; set; } = "";
    public string TaxExcluded { get; set; } = "";
    public string TaxAmount   { get; set; } = "";
}

public class InvoiceReceiptLine
{
    public string ReceiptDate { get; set; } = "";
    public string ReceiptNo   { get; set; } = "";
    public string Remarks     { get; set; } = "";
    public string AmountStr   { get; set; } = "";
}

public class InvoiceTaxBreakdown
{
    public string Label             { get; set; } = "";
    public string TaxExcludedAmount { get; set; } = "";
    public string TaxAmount         { get; set; } = "";
}
