using bmcs_app.Core.Models;
using bmcs_app.Inventory.Services;
using Prism.Commands;
using Prism.Mvvm;

namespace bmcs_app.Inventory.ViewModels;

public class InventoryCountLineViewModel : BindableBase
{
    private readonly LookupService _lookup;
    private readonly Action<InventoryCountLineViewModel> _onDelete;
    private readonly Action<InventoryCountLineViewModel> _onNoteEnter;

    /// <summary>View (InventoryCountLineControl) が購読 → 数量欄へフォーカス移動</summary>
    public event Action? MoveToQuantityRequested;

    public int  LineNo    { get; set; }
    public int? ProductId { get; private set; }

    private string _editProductCode = string.Empty;
    public string EditProductCode
    {
        get => _editProductCode;
        set => SetProperty(ref _editProductCode, value);
    }

    private string _editProductName = string.Empty;
    public string EditProductName
    {
        get => _editProductName;
        set => SetProperty(ref _editProductName, value);
    }

    private decimal? _editQuantity;
    public decimal? EditQuantity
    {
        get => _editQuantity;
        set => SetProperty(ref _editQuantity, value);
    }

    private string _editNote = string.Empty;
    public string EditNote
    {
        get => _editNote;
        set => SetProperty(ref _editNote, value);
    }

    public DelegateCommand DeleteLineCommand          { get; }
    public DelegateCommand OpenProductLookupCommand   { get; }
    public DelegateCommand LookupProductByCodeCommand { get; }
    public DelegateCommand NoteEnterCommand           { get; }

    public InventoryCountLineViewModel(
        LookupService lookup,
        Action<InventoryCountLineViewModel> onDelete,
        Action<InventoryCountLineViewModel> onNoteEnter)
    {
        _lookup       = lookup;
        _onDelete     = onDelete;
        _onNoteEnter  = onNoteEnter;

        DeleteLineCommand = new DelegateCommand(() => _onDelete(this));

        OpenProductLookupCommand = new DelegateCommand(() =>
        {
            var product = _lookup.OpenProductSearch(EditProductCode);
            if (product is not null)
            {
                ApplyProduct(product);
                MoveToQuantityRequested?.Invoke();
            }
        });

        LookupProductByCodeCommand = new DelegateCommand(() =>
        {
            var product = _lookup.FindProductByCode(EditProductCode);
            if (product is not null)
            {
                ApplyProduct(product);
                MoveToQuantityRequested?.Invoke();
            }
            else
            {
                EditProductName = string.Empty;
                ProductId = null;
            }
        });

        NoteEnterCommand = new DelegateCommand(() => _onNoteEnter(this));
    }

    public void ApplyProduct(Product product)
    {
        ProductId       = product.ProductId;
        EditProductCode = product.ProductCode;
        EditProductName = product.ProductName;
    }

    public void Load(InventoryCountLine line)
    {
        ProductId       = line.ProductId;
        EditProductCode = line.ProductCode;
        EditProductName = line.ProductName;
        EditQuantity    = line.Quantity;
        EditNote        = line.Note ?? string.Empty;
    }
}
