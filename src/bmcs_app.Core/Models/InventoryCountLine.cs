namespace bmcs_app.Core.Models;

public class InventoryCountLine
{
    public int       InventoryCountId { get; set; }
    public DateOnly  CountDate        { get; set; }
    public int       ProductId        { get; set; }
    public string    ProductCode      { get; set; } = string.Empty;
    public string    ProductName      { get; set; } = string.Empty;
    public decimal   Quantity         { get; set; }
    public string?   Note             { get; set; }
}
