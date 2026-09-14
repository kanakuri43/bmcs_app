using System.Collections.ObjectModel;
using bmcs_app.Application.Master;
using bmcs_app.Domain.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bmcs_app.ViewModels.Common;

/// <summary>
/// 社員マスタ画面向けの社員検索モーダル。
/// <see cref="ProductMasterSearchDialogViewModel"/>と同じく、マスタ全件ロード後にメモリで絞り込む。
/// </summary>
public partial class EmployeeMasterSearchDialogViewModel(EmployeeService employeeService)
    : DialogViewModelBase<Employee>
{
    private List<Employee> _allEmployees = [];

    public ObservableCollection<EmployeeSearchItem> Results { get; } = [];

    [ObservableProperty]
    public partial string SearchKeyword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int SelectedIndex { get; set; } = -1;

    [RelayCommand]
    private Task LoadAsync() => RunBusyAsync(async () =>
    {
        _allEmployees = await employeeService.GetEmployeesAsync();
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
            ? _allEmployees
            : _allEmployees.Where(e => keywords.All(k => Matches(e, k)));

        Results.Clear();
        foreach (var employee in filtered)
        {
            Results.Add(EmployeeSearchItem.FromEntity(employee));
        }

        SelectedIndex = Results.Count > 0 ? 0 : -1;
    }

    private static bool Matches(Employee employee, string keyword) =>
        Contains(employee.EmployeeCode, keyword)
        || Contains(employee.EmployeeName, keyword)
        || Contains(employee.EmployeeNameKana, keyword);

    private static bool Contains(string? source, string keyword) =>
        source is not null && source.Contains(keyword, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>一覧で選択した社員を確定する（RowActivationBehavior から Enter で呼ばれる）。</summary>
    [RelayCommand]
    private void Confirm(object? item)
    {
        if (item is not EmployeeSearchItem searchItem)
        {
            return;
        }

        var employee = _allEmployees.SingleOrDefault(e => e.EmployeeCode == searchItem.EmployeeCode);
        if (employee is not null)
        {
            CloseWith(employee);
        }
    }
}
