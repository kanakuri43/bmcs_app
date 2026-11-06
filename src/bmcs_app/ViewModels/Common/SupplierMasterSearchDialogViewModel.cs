using System.Collections.ObjectModel;
using bmcs_app.Application.Master;
using bmcs_app.Domain.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bmcs_app.ViewModels.Common;

/// <summary>
/// 仕入先検索モーダル（仕入先マスタ・商品マスタの主仕入先・仕入先別原価マスタ・伝票明細の仕入先欄で共用）。
/// <see cref="BankAccountMasterSearchDialogViewModel"/>と同じく、マスタ全件ロード後にメモリで絞り込む。
/// </summary>
public partial class SupplierMasterSearchDialogViewModel(SupplierService supplierService)
    : DialogViewModelBase<Supplier>
{
    private List<Supplier> _allSuppliers = [];

    public ObservableCollection<SupplierSearchItem> Results { get; } = [];

    [ObservableProperty]
    public partial string SearchKeyword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int SelectedIndex { get; set; } = -1;

    [RelayCommand]
    private Task LoadAsync() => RunBusyAsync(async () =>
    {
        _allSuppliers = await supplierService.GetSuppliersAsync();
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
            ? _allSuppliers
            : _allSuppliers.Where(s => keywords.All(k => Matches(s, k)));

        Results.Clear();
        foreach (var supplier in filtered)
        {
            Results.Add(SupplierSearchItem.FromEntity(supplier));
        }

        SelectedIndex = Results.Count > 0 ? 0 : -1;
    }

    private static bool Matches(Supplier supplier, string keyword) =>
        Contains(supplier.SupplierCode, keyword)
        || Contains(supplier.SupplierName, keyword)
        || Contains(supplier.SupplierNameKana, keyword)
        || Contains(supplier.ContactPersonName, keyword);

    private static bool Contains(string? source, string keyword) =>
        source is not null && source.Contains(keyword, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>一覧で選択した仕入先を確定する（RowActivationBehavior から Enter で呼ばれる）。</summary>
    [RelayCommand]
    private void Confirm(object? item)
    {
        if (item is not SupplierSearchItem searchItem)
        {
            return;
        }

        var supplier = _allSuppliers.SingleOrDefault(s => s.SupplierCode == searchItem.SupplierCode);
        if (supplier is not null)
        {
            CloseWith(supplier);
        }
    }
}
