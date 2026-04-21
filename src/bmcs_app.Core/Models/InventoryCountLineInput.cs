namespace bmcs_app.Core.Models;

public record InventoryCountLineInput(int ProductId, decimal Quantity, string? Note);
