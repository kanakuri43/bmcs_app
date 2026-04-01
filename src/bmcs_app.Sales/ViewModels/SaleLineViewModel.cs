using bmcs_app.Core.Models;
using Prism.Mvvm;

namespace bmcs_app.Sales.ViewModels;

/// <summary>売上明細DataGrid の1行分（編集可能）</summary>
public class SaleLineViewModel : BindableBase
{
    private int    _lineNo;
    private string _productCode = "";
    private string _productName = "";
    private int    _productId;
    private decimal _quantity;
    private decimal _unitPrice;
    private TaxTypeClassification? _taxType;
    private decimal _appliedTaxRate;
    private decimal _lineTaxAmount;
    private string  _lineRemarks = "";

    public int LineNo
    {
        get => _lineNo;
        set => SetProperty(ref _lineNo, value);
    }

    public int ProductId
    {
        get => _productId;
        set => SetProperty(ref _productId, value);
    }

    public string ProductCode
    {
        get => _productCode;
        set => SetProperty(ref _productCode, value);
    }

    public string ProductName
    {
        get => _productName;
        set => SetProperty(ref _productName, value);
    }

    public decimal Quantity
    {
        get => _quantity;
        set
        {
            if (SetProperty(ref _quantity, value))
            {
                RaisePropertyChanged(nameof(LineAmount));
            }
        }
    }

    public decimal UnitPrice
    {
        get => _unitPrice;
        set
        {
            if (SetProperty(ref _unitPrice, value))
            {
                RaisePropertyChanged(nameof(LineAmount));
            }
        }
    }

    /// <summary>数量 × 単価（税抜金額）</summary>
    public decimal LineAmount => Quantity * UnitPrice;

    public TaxTypeClassification? TaxType
    {
        get => _taxType;
        set => SetProperty(ref _taxType, value);
    }

    public decimal AppliedTaxRate
    {
        get => _appliedTaxRate;
        set
        {
            if (SetProperty(ref _appliedTaxRate, value))
                RaisePropertyChanged(nameof(AppliedTaxRateDisplay));
        }
    }

    /// <summary>表示用税率文字列（例: "10%"）</summary>
    public string AppliedTaxRateDisplay => _appliedTaxRate == 0
        ? "—"
        : $"{_appliedTaxRate * 100:0.##}%";

    public decimal LineTaxAmount
    {
        get => _lineTaxAmount;
        set => SetProperty(ref _lineTaxAmount, value);
    }

    public string LineRemarks
    {
        get => _lineRemarks;
        set => SetProperty(ref _lineRemarks, value);
    }
}
