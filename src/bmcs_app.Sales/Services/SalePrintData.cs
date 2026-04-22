namespace bmcs_app.Sales.Services;

public class SalePrintData
{
    public string SaleNo         { get; set; } = "";
    public string SaleDate       { get; set; } = "";
    public string CustomerName       { get; set; } = "";
    public string CustomerPostalCode { get; set; } = "";
    public string CustomerAddress1   { get; set; } = "";
    public string CustomerAddress2   { get; set; } = "";
    public string EmployeeName       { get; set; } = "";
    public string SlipRemarks    { get; set; } = "";

    // 自社情報（company_info テーブル）
    public string CompanyName         { get; set; } = "";
    public string CompanyAddress      { get; set; } = "";
    public string CompanyPhone        { get; set; } = "";
    public string CompanyFax          { get; set; } = "";
    public string CompanyInvoiceRegNo { get; set; } = "";

    public List<SalePrintLine>      Lines         { get; set; } = new();
    public List<TaxRateBreakdown>   TaxBreakdowns { get; set; } = new();

    /// <summary>true = 請求単位消費税: 納品書には消費税を表示しない</summary>
    public bool IsInvoiceUnitTax { get; set; }

    public string TaxExcludedTotalStr { get; set; } = "";
    public string TaxTotalStr         { get; set; } = "";
    public string GrandTotalStr       { get; set; } = "";
}

public class SalePrintLine
{
    public int    LineNo        { get; set; }
    public string ProductCode   { get; set; } = "";
    public string ProductName   { get; set; } = "";
    public bool   IsReducedRate { get; set; }
    public string Quantity      { get; set; } = "";
    public string UnitPrice     { get; set; } = "";
    public string LineAmount    { get; set; } = "";
    public string TaxRate       { get; set; } = "";
    public string LineRemarks   { get; set; } = "";
}

/// <summary>インボイス制度：税率区分ごとの集計行</summary>
public class TaxRateBreakdown
{
    /// <summary>e.g. "10%対象", "8%対象（軽減税率）", "10%内税"</summary>
    public string Label              { get; set; } = "";
    public string TaxExcludedAmount  { get; set; } = "";
    public string TaxAmount          { get; set; } = "";
}
