using bmcs_app.Domain.Entities;

namespace bmcs_app.ViewModels.Common;

/// <summary>仕入先検索モーダルの一覧行の表示用。</summary>
public sealed record SupplierSearchItem(
    string SupplierCode,
    string SupplierName,
    string? SupplierNameKana,
    string? PhoneNumber,
    string? ContactPersonName)
{
    public static SupplierSearchItem FromEntity(Supplier supplier) => new(
        supplier.SupplierCode,
        supplier.SupplierName,
        supplier.SupplierNameKana,
        supplier.PhoneNumber,
        supplier.ContactPersonName);
}
