using System.Collections.ObjectModel;
using bmcs_app.Application.Master;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bmcs_app.ViewModels.Common;

/// <summary>
/// 得意先検索モーダル。得意先名・カナ・住所・担当者名を検索対象にする
/// （docs/database-schema.md 2.1 が検索対象と明記する列＋タスク要件の担当者名・住所）。
/// 検索はマスタ全件をロードしたうえでのメモリ絞り込み（得意先マスタ画面と同じ全件ロード方針に揃える）。
/// <see cref="RequiredTaxUnit"/> を呼び出し元（<c>windowService.ShowDialog</c>の<c>configure</c>）で
/// 設定すると、その税区分の得意先のみに絞り込む（明細請求書発行・明細入金は内税明細単位専用のため）。
/// </summary>
public partial class CustomerSearchDialogViewModel(CustomerService customerService)
    : DialogViewModelBase<Customer>
{
    private List<Customer> _allCustomers = [];

    public ObservableCollection<CustomerSearchItem> Results { get; } = [];

    /// <summary>設定すると、この税区分の得意先のみを検索対象にする（未設定なら全件）。</summary>
    public TaxUnit? RequiredTaxUnit { get; set; }

    /// <summary>true を設定すると、請求集約先（自分自身に請求得意先コードを設定している得意先）のみに
    /// 絞り込む。得意先マスタ画面の「請求得意先コード」欄からの呼び出しで使う。</summary>
    public bool BillingRootOnly { get; set; }

    [ObservableProperty]
    public partial string SearchKeyword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int SelectedIndex { get; set; } = -1;

    [RelayCommand]
    private Task LoadAsync() => RunBusyAsync(async () =>
    {
        var customers = await customerService.GetCustomersAsync();
        if (RequiredTaxUnit is not null)
        {
            customers = customers.Where(c => c.TaxUnit == RequiredTaxUnit).ToList();
        }

        if (BillingRootOnly)
        {
            customers = customers.Where(c => c.IsBillingRoot).ToList();
        }

        _allCustomers = customers;
        ApplyFilter();
    });

    partial void OnSearchKeywordChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var keywords = SearchKeyword
            .Split(' ', '　')
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .ToArray();

        var filtered = keywords.Length == 0
            ? _allCustomers
            : _allCustomers.Where(c => keywords.All(k => Matches(c, k)));

        Results.Clear();
        foreach (var customer in filtered)
        {
            Results.Add(CustomerSearchItem.FromEntity(customer));
        }

        SelectedIndex = Results.Count > 0 ? 0 : -1;
    }

    private static bool Matches(Customer customer, string keyword) =>
        Contains(customer.CustomerCode, keyword)
        || Contains(customer.CustomerName, keyword)
        || Contains(customer.CustomerNameKana, keyword)
        || Contains(customer.Address1, keyword)
        || Contains(customer.ContactPersonName, keyword);

    private static bool Contains(string? source, string keyword) =>
        source is not null && source.Contains(keyword, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>一覧で選択した得意先を確定する（RowActivationBehavior から Enter で呼ばれる）。</summary>
    [RelayCommand]
    private void Confirm(object? item)
    {
        if (item is not CustomerSearchItem searchItem)
        {
            return;
        }

        var customer = _allCustomers.SingleOrDefault(c => c.CustomerCode == searchItem.CustomerCode);
        if (customer is not null)
        {
            CloseWith(customer);
        }
    }
}
