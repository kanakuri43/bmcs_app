using bmcs_app.Core.Models;
using Prism.Commands;
using Prism.Mvvm;

namespace bmcs_app.Sales.ViewModels;

/// <summary>売上明細の1行分（UserControl で表示）</summary>
public class SaleLineViewModel : BindableBase
{
    private readonly Action<SaleLineViewModel> _onOpenProductLookup;
    private readonly Action<SaleLineViewModel> _onLookupProductByCode;
    private readonly Action<SaleLineViewModel> _onDelete;
    private readonly Action<SaleLineViewModel> _onLineRemarksEnter;

    private int    _lineNo;
    private string _productCode = "";
    private string _productName = "";
    private int    _productId;
    private decimal _quantity;
    private decimal _unitPrice;
    private TaxTypeClassification? _taxType;
    private byte    _taxRateType;
    private decimal _appliedTaxRate;
    private bool    _isLineTaxCalc = true;
    private string  _lineRemarks = "";

    public DelegateCommand OpenProductLookupCommand   { get; }
    public DelegateCommand LookupProductByCodeCommand { get; }
    public DelegateCommand DeleteCommand              { get; }
    public DelegateCommand LineRemarksEnterCommand    { get; }

    /// <summary>商品確定後に数量欄へフォーカスを移動するよう要求するイベント（Viewコードビハインドがハンドル）</summary>
    public event Action? MoveToQuantityRequested;

    public SaleLineViewModel(
        Action<SaleLineViewModel> onOpenProductLookup,
        Action<SaleLineViewModel> onLookupProductByCode,
        Action<SaleLineViewModel> onDelete,
        Action<SaleLineViewModel> onLineRemarksEnter)
    {
        _onOpenProductLookup   = onOpenProductLookup;
        _onLookupProductByCode = onLookupProductByCode;
        _onDelete              = onDelete;
        _onLineRemarksEnter    = onLineRemarksEnter;

        OpenProductLookupCommand   = new DelegateCommand(() => _onOpenProductLookup(this));
        LookupProductByCodeCommand = new DelegateCommand(() => _onLookupProductByCode(this));
        DeleteCommand              = new DelegateCommand(() => _onDelete(this));
        LineRemarksEnterCommand    = new DelegateCommand(() => _onLineRemarksEnter(this));
    }

    /// <summary>商品確定後、View に数量欄へのフォーカス移動を依頼する</summary>
    public void RequestMoveToQuantity() => MoveToQuantityRequested?.Invoke();

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
                RaisePropertyChanged(nameof(LineTaxAmount));
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
                RaisePropertyChanged(nameof(LineTaxAmount));
            }
        }
    }

    /// <summary>数量 × 単価（税抜金額）</summary>
    public decimal LineAmount => Quantity * UnitPrice;

    public TaxTypeClassification? TaxType
    {
        get => _taxType;
        set
        {
            if (SetProperty(ref _taxType, value))
                RaisePropertyChanged(nameof(LineTaxAmount));
        }
    }

    /// <summary>true = 明細単位で税額計算、false = 伝票単位（明細税額は 0）</summary>
    public bool IsLineTaxCalc
    {
        get => _isLineTaxCalc;
        set
        {
            if (SetProperty(ref _isLineTaxCalc, value))
                RaisePropertyChanged(nameof(LineTaxAmount));
        }
    }

    public byte TaxRateType
    {
        get => _taxRateType;
        set => SetProperty(ref _taxRateType, value);
    }

    public decimal AppliedTaxRate
    {
        get => _appliedTaxRate;
        set
        {
            if (SetProperty(ref _appliedTaxRate, value))
            {
                RaisePropertyChanged(nameof(AppliedTaxRateDisplay));
                RaisePropertyChanged(nameof(LineTaxAmount));
            }
        }
    }

    /// <summary>表示用税率文字列（例: "10%"）</summary>
    public string AppliedTaxRateDisplay => _appliedTaxRate == 0
        ? "—"
        : $"{_appliedTaxRate * 100:0.##}%";

    /// <summary>
    /// 行税額（自動計算）。
    /// 伝票単位（IsLineTaxCalc=false）の場合は 0。
    /// 明細単位の場合: 外税=金額×税率, 内税=金額×税率÷(1+税率)、切り捨て。
    /// </summary>
    public decimal LineTaxAmount
    {
        get
        {
            if (!_isLineTaxCalc) return 0m;
            if (_appliedTaxRate == 0) return 0m;
            if (_taxType?.TaxTypeId == 2)
                return Math.Floor(LineAmount * _appliedTaxRate / (1 + _appliedTaxRate));
            return Math.Floor(LineAmount * _appliedTaxRate);
        }
    }

    public string LineRemarks
    {
        get => _lineRemarks;
        set => SetProperty(ref _lineRemarks, value);
    }
}
