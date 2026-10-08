using System.Collections.ObjectModel;
using bmcs_app.Application.Master;
using bmcs_app.Domain.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bmcs_app.ViewModels.Common;

/// <summary>
/// コピー機マスタ画面向けの機番検索モーダル。
/// <see cref="BankAccountMasterSearchDialogViewModel"/>と同じく、マスタ全件ロード後にメモリで絞り込む。
/// </summary>
public partial class CopierMachineMasterSearchDialogViewModel(CopierMachineService copierMachineService)
    : DialogViewModelBase<CopierMachine>
{
    private List<CopierMachine> _allMachines = [];

    public ObservableCollection<CopierMachineSearchItem> Results { get; } = [];

    [ObservableProperty]
    public partial string SearchKeyword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int SelectedIndex { get; set; } = -1;

    [RelayCommand]
    private Task LoadAsync() => RunBusyAsync(async () =>
    {
        _allMachines = await copierMachineService.GetCopierMachinesAsync();
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
            ? _allMachines
            : _allMachines.Where(m => keywords.All(k => Matches(m, k)));

        Results.Clear();
        foreach (var machine in filtered)
        {
            Results.Add(CopierMachineSearchItem.FromEntity(machine));
        }

        SelectedIndex = Results.Count > 0 ? 0 : -1;
    }

    private static bool Matches(CopierMachine machine, string keyword) =>
        Contains(machine.MachineNo, keyword)
        || Contains(machine.CustomerCode, keyword)
        || Contains(machine.MachineModel, keyword)
        || Contains(machine.Remarks, keyword);

    private static bool Contains(string? source, string keyword) =>
        source is not null && source.Contains(keyword, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>一覧で選択した機番を確定する（RowActivationBehavior から Enter で呼ばれる）。</summary>
    [RelayCommand]
    private void Confirm(object? item)
    {
        if (item is not CopierMachineSearchItem searchItem)
        {
            return;
        }

        var machine = _allMachines.SingleOrDefault(m => m.MachineNo == searchItem.MachineNo);
        if (machine is not null)
        {
            CloseWith(machine);
        }
    }
}
