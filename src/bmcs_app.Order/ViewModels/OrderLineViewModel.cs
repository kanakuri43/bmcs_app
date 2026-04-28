using Prism.Commands;
using Prism.Mvvm;

namespace bmcs_app.Order.ViewModels;

/// <summary>受注明細の1行分（UserControl で表示）</summary>
public class OrderLineViewModel : BindableBase
{
    private readonly Action<OrderLineViewModel> _onOpenProductLookup;
    private readonly Action<OrderLineViewModel> _onLookupProductByCode;
    private readonly Action<OrderLineViewModel> _onDelete;
    private readonly Action<OrderLineViewModel> _onLineRemarksEnter;

    private int     _lineNo;
    private int     _productId;
    private string  _productCode    = "";
    private string  _productName    = "";
    private decimal _quantity;
    private decimal _unitPrice;
    private decimal _costPrice;
    private byte    _taxRateType;
    private decimal _appliedTaxRate;
    private string  _lineRemarks    = "";
    private bool    _isProductNameReadOnly = true;

    public DelegateCommand OpenProductLookupCommand   { get; }
    public DelegateCommand LookupProductByCodeCommand { get; }
    public DelegateCommand DeleteCommand              { get; }
    public DelegateCommand LineRemarksEnterCommand    { get; }

    /// <summary>商品確定後に数量欄へフォーカスを移動するよう要求するイベント</summary>
    public event Action? MoveToQuantityRequested;

    public OrderLineViewModel(
        Action<OrderLineViewModel> onOpenProductLookup,
        Action<OrderLineViewModel> onLookupProductByCode,
        Action<OrderLineViewModel> onDelete,
        Action<OrderLineViewModel> onLineRemarksEnter)
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

    public bool IsProductNameReadOnly
    {
        get => _isProductNameReadOnly;
        set => SetProperty(ref _isProductNameReadOnly, value);
    }

    public decimal Quantity
    {
        get => _quantity;
        set
        {
            if (SetProperty(ref _quantity, value))
            {
                RaisePropertyChanged(nameof(LineAmount));
                RaisePropertyChanged(nameof(LineCostTotal));
            }
        }
    }

    /// <summary>原価（商品マスタから取得、修正不可）</summary>
    public decimal CostPrice
    {
        get => _costPrice;
        set
        {
            if (SetProperty(ref _costPrice, value))
                RaisePropertyChanged(nameof(LineCostTotal));
        }
    }

    /// <summary>行原価合計 = 原価 × 数量</summary>
    public decimal LineCostTotal => CostPrice * Quantity;

    public decimal UnitPrice
    {
        get => _unitPrice;
        set
        {
            if (SetProperty(ref _unitPrice, value))
                RaisePropertyChanged(nameof(LineAmount));
        }
    }

    /// <summary>数量 × 単価（税抜金額）</summary>
    public decimal LineAmount => Quantity * UnitPrice;

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
                RaisePropertyChanged(nameof(AppliedTaxRateDisplay));
        }
    }

    public string AppliedTaxRateDisplay => _appliedTaxRate == 0
        ? "—"
        : $"{_appliedTaxRate * 100:0.##}%";

    public string LineRemarks
    {
        get => _lineRemarks;
        set => SetProperty(ref _lineRemarks, value);
    }
}
