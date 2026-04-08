namespace bmcs_app.Closing.Services;

public class InvoicePrintData
{
    public string CustomerCode        { get; set; } = "";
    public string CustomerName        { get; set; } = "";
    public string CustomerPostalCode  { get; set; } = "";
    public string CustomerAddress1    { get; set; } = "";
    public string CustomerAddress2    { get; set; } = "";
    public string InvoiceDate         { get; set; } = "";

    // 集計（invoice_headers）
    public string PreviousAmountStr   { get; set; } = "";
    public string ReceiptAmountStr    { get; set; } = "";
    public string SalesStandardStr    { get; set; } = "";
    public string SalesReducedStr     { get; set; } = "";
    public string TaxStandardStr      { get; set; } = "";
    public string TaxReducedStr       { get; set; } = "";
    public string CurrentAmountStr    { get; set; } = "";
    // 合計（標準＋軽減）
    public string SalesTotalStr       { get; set; } = "";
    public string TaxTotalStr         { get; set; } = "";

    // 自社情報
    public string CompanyName         { get; set; } = "";
    public string CompanyAddress      { get; set; } = "";
    public string CompanyPhone        { get; set; } = "";
    public string CompanyFax          { get; set; } = "";
    public string CompanyInvoiceRegNo { get; set; } = "";

    // 明細（売上・入金混合ソート済み）
    public List<InvoiceMixedLine>     MixedLines    { get; set; } = new();

    // 税率別集計（インボイス制度）
    public List<InvoiceTaxBreakdown>  TaxBreakdowns { get; set; } = new();
}

public class InvoiceMixedLine
{
    // ソートキー（表示には使用しない）
    public DateOnly SortDate   { get; set; }
    public string   SortSlipNo { get; set; } = "";
    public int      SortLineNo { get; set; }
    // 表示用
    public string DateStr      { get; set; } = "";
    public string SlipNo       { get; set; } = "";
    public string Description  { get; set; } = "";   // 商品名 or 支払方法
    public string QuantityStr  { get; set; } = "";   // 売上のみ（入金は空）
    public string UnitPriceStr { get; set; } = "";   // 売上のみ（入金は空）
    public string AmountStr    { get; set; } = "";
}

public class InvoiceTaxBreakdown
{
    public string Label             { get; set; } = "";
    public string TaxExcludedAmount { get; set; } = "";
    public string TaxAmount         { get; set; } = "";
}
