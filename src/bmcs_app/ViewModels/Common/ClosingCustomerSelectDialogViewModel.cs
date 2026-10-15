using System.Collections.ObjectModel;
using bmcs_app.Application.Master;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Services;
using bmcs_app.Views.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bmcs_app.ViewModels.Common;

/// <summary>
/// 請求締めの対象得意先を選ぶモーダル。一覧には選ばれた得意先だけを表示し、
/// 「得意先検索」ボタンで得意先検索ダイアログ（<see cref="CustomerSearchDialogViewModel"/>）を開いて1件ずつ追加する。
/// 追加できるのは締め得意先（請求単位／伝票単位）の請求集約先のみ。結果は得意先コードの一覧（キャンセル時は null）。
/// </summary>
public partial class ClosingCustomerSelectDialogViewModel(
    CustomerService customerService,
    WindowService windowService) : DialogViewModelBase<List<string>>
{
    /// <summary>呼び出し元が<c>configure</c>で渡す、既に選択済みの得意先コード。</summary>
    public IReadOnlyCollection<string> InitialCodes { get; set; } = [];

    /// <summary>選ばれた得意先。</summary>
    public ObservableCollection<CustomerSearchItem> Results { get; } = [];

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [RelayCommand]
    private Task LoadAsync() => RunBusyAsync(async () =>
    {
        if (InitialCodes.Count == 0)
        {
            return;
        }

        var customers = await customerService.GetCustomersAsync();
        foreach (var customer in customers.Where(c => InitialCodes.Contains(c.CustomerCode)))
        {
            Results.Add(CustomerSearchItem.FromEntity(customer));
        }
    });

    [RelayCommand]
    private void Search()
    {
        var customer = windowService.ShowDialog<CustomerSearchDialog, CustomerSearchDialogViewModel, Customer>();
        if (customer is null)
        {
            return;
        }

        if (customer.TaxUnit is not (TaxUnit.Invoice or TaxUnit.Slip) || !customer.IsBillingRoot)
        {
            StatusMessage = $"{customer.CustomerCode} {customer.CustomerName} は請求締めの対象外です（締め得意先の請求集約先のみ選択できます）。";
            return;
        }

        if (Results.Any(r => r.CustomerCode == customer.CustomerCode))
        {
            StatusMessage = $"{customer.CustomerCode} {customer.CustomerName} は選択済みです。";
            return;
        }

        Results.Add(CustomerSearchItem.FromEntity(customer));
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private void Remove(CustomerSearchItem item) => Results.Remove(item);

    [RelayCommand]
    private void Confirm() => CloseWith(Results.Select(r => r.CustomerCode).ToList());
}
