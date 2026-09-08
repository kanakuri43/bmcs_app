using bmcs_app.Domain.Entities;

namespace bmcs_app.ViewModels.Common;

/// <summary>得意先検索モーダルの一覧行の表示用。表示文字列の組み立ては Presentation 層の責務。</summary>
public sealed record CustomerSearchItem(
    string CustomerCode,
    string CustomerName,
    string? CustomerNameKana,
    string? Address1,
    string? ContactPersonName)
{
    public static CustomerSearchItem FromEntity(Customer customer) => new(
        customer.CustomerCode,
        customer.CustomerName,
        customer.CustomerNameKana,
        customer.Address1,
        customer.ContactPersonName);
}
