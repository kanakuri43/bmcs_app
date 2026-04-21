using bmcs_app.Core.Models;

namespace bmcs_app.Core.Interfaces;

public class InventoryCurrentStock
{
    public int       ProductId       { get; set; }
    public string    ProductCode     { get; set; } = string.Empty;
    public string    ProductName     { get; set; } = string.Empty;
    public DateOnly? LastCountDate   { get; set; }
    public decimal?  LastCountQty    { get; set; }
    public decimal   PurchaseQty     { get; set; }
    public decimal   SaleQty         { get; set; }
    public decimal?  CurrentStock    { get; set; }
}

public interface IInventoryCurrentRepository
{
    Task<IEnumerable<InventoryCurrentStock>> GetAllAsync();
}
