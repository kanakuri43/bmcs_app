using System.Collections.ObjectModel;
using bmcs_app.Application.Master;
using bmcs_app.Domain.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bmcs_app.ViewModels.Common;

/// <summary>
/// 銀行マスタ画面向けの銀行口座検索モーダル。
/// <see cref="ProductMasterSearchDialogViewModel"/>と同じく、マスタ全件ロード後にメモリで絞り込む。
/// </summary>
public partial class BankAccountMasterSearchDialogViewModel(BankAccountService bankAccountService)
    : DialogViewModelBase<BankAccount>
{
    private List<BankAccount> _allBankAccounts = [];

    public ObservableCollection<BankAccountSearchItem> Results { get; } = [];

    [ObservableProperty]
    public partial string SearchKeyword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int SelectedIndex { get; set; } = -1;

    [RelayCommand]
    private Task LoadAsync() => RunBusyAsync(async () =>
    {
        _allBankAccounts = await bankAccountService.GetBankAccountsAsync();
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
            ? _allBankAccounts
            : _allBankAccounts.Where(b => keywords.All(k => Matches(b, k)));

        Results.Clear();
        foreach (var bankAccount in filtered)
        {
            Results.Add(BankAccountSearchItem.FromEntity(bankAccount));
        }

        SelectedIndex = Results.Count > 0 ? 0 : -1;
    }

    private static bool Matches(BankAccount bankAccount, string keyword) =>
        Contains(bankAccount.BankAccountCode, keyword)
        || Contains(bankAccount.BankName, keyword)
        || Contains(bankAccount.BranchName, keyword)
        || Contains(bankAccount.AccountHolderName, keyword);

    private static bool Contains(string? source, string keyword) =>
        source is not null && source.Contains(keyword, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>一覧で選択した銀行口座を確定する（RowActivationBehavior から Enter で呼ばれる）。</summary>
    [RelayCommand]
    private void Confirm(object? item)
    {
        if (item is not BankAccountSearchItem searchItem)
        {
            return;
        }

        var bankAccount = _allBankAccounts.SingleOrDefault(b => b.BankAccountCode == searchItem.BankAccountCode);
        if (bankAccount is not null)
        {
            CloseWith(bankAccount);
        }
    }
}
