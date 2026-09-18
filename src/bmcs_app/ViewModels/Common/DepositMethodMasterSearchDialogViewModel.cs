using System.Collections.ObjectModel;
using bmcs_app.Application.Master;
using bmcs_app.Domain.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bmcs_app.ViewModels.Common;

/// <summary>
/// 入金方法マスタ画面向けの入金方法検索モーダル。
/// <see cref="BankAccountMasterSearchDialogViewModel"/>と同じく、マスタ全件ロード後にメモリで絞り込む。
/// </summary>
public partial class DepositMethodMasterSearchDialogViewModel(DepositMethodService depositMethodService)
    : DialogViewModelBase<DepositMethod>
{
    private List<DepositMethod> _allDepositMethods = [];

    public ObservableCollection<DepositMethodSearchItem> Results { get; } = [];

    [ObservableProperty]
    public partial string SearchKeyword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int SelectedIndex { get; set; } = -1;

    [RelayCommand]
    private Task LoadAsync() => RunBusyAsync(async () =>
    {
        _allDepositMethods = await depositMethodService.GetDepositMethodsAsync();
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
            ? _allDepositMethods
            : _allDepositMethods.Where(m => keywords.All(k => Matches(m, k)));

        Results.Clear();
        foreach (var depositMethod in filtered)
        {
            Results.Add(DepositMethodSearchItem.FromEntity(depositMethod));
        }

        SelectedIndex = Results.Count > 0 ? 0 : -1;
    }

    private static bool Matches(DepositMethod depositMethod, string keyword) =>
        Contains(depositMethod.DepositMethodCode, keyword)
        || Contains(depositMethod.DepositMethodName, keyword);

    private static bool Contains(string? source, string keyword) =>
        source is not null && source.Contains(keyword, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>一覧で選択した入金方法を確定する（RowActivationBehavior から Enter で呼ばれる）。</summary>
    [RelayCommand]
    private void Confirm(object? item)
    {
        if (item is not DepositMethodSearchItem searchItem)
        {
            return;
        }

        var depositMethod = _allDepositMethods.SingleOrDefault(m => m.DepositMethodCode == searchItem.DepositMethodCode);
        if (depositMethod is not null)
        {
            CloseWith(depositMethod);
        }
    }
}
