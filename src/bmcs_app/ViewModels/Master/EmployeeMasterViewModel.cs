using System.Windows;
using bmcs_app.Application.Master;
using bmcs_app.Domain.Entities;
using bmcs_app.Services;
using bmcs_app.ViewModels.Common;
using bmcs_app.Views.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bmcs_app.ViewModels.Master;

/// <summary>
/// 社員マスタ画面。一覧は持たず、社員コードを直接入力するか、
/// コード欄で Space を押して検索モーダル（<see cref="EmployeeMasterSearchDialog"/>）を呼び出して対象を選ぶ
/// （得意先マスタ・商品マスタ画面と同じ SPACEで検索／Enter読込のパターンに揃える）。
/// </summary>
public partial class EmployeeMasterViewModel(EmployeeService employeeService, WindowService windowService) : ViewModelBase
{
    [ObservableProperty]
    public partial string EmployeeCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EmployeeName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EmployeeNameKana { get; set; } = string.Empty;

    /// <summary>権限レベル（`menu.required_permission_level` と比較する数値。docs/database-schema.md 2.4）。文字列で保持し保存時に検証する。</summary>
    [ObservableProperty]
    public partial string PermissionLevelText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsNew { get; set; } = true;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    private byte[]? _loadedRowVersion;

    [RelayCommand]
    private void NewEmployee()
    {
        ClearForm();
        IsNew = true;
        StatusMessage = string.Empty;
    }

    /// <summary>コード欄で Space を押したときに検索モーダルを開く。</summary>
    [RelayCommand]
    private void OpenEmployeeSearch()
    {
        var employee = windowService.ShowDialog<EmployeeMasterSearchDialog, EmployeeMasterSearchDialogViewModel, Employee>();
        if (employee is not null)
        {
            ApplyEmployee(employee);
            RequestFocus("Name");
        }
    }

    /// <summary>コード欄で Enter を押したときに、入力済みコードで直接読み込む。</summary>
    [RelayCommand]
    private Task LookupEmployeeByCodeAsync() => RunBusyAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(EmployeeCode))
        {
            return;
        }

        var employee = await employeeService.GetByCodeAsync(EmployeeCode);
        if (employee is null)
        {
            var enteredCode = EmployeeCode;
            ClearForm();
            EmployeeCode = enteredCode;
            IsNew = true;
            StatusMessage = $"社員コード「{enteredCode}」は未登録です。新規登録として入力してください。";
            return;
        }

        ApplyEmployee(employee);
    });

    private void ApplyEmployee(Employee employee)
    {
        EmployeeCode = employee.EmployeeCode;
        EmployeeName = employee.EmployeeName;
        EmployeeNameKana = employee.EmployeeNameKana ?? string.Empty;
        PermissionLevelText = employee.PermissionLevel.ToString();

        _loadedRowVersion = employee.RowVersion;
        IsNew = false;
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private Task SaveAsync() => RunBusyAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(EmployeeCode) || string.IsNullOrWhiteSpace(EmployeeName))
        {
            MessageBox.Show("社員コードと社員名は必須です。", "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (EmployeeCode.Length > 10)
        {
            MessageBox.Show("社員コードは10文字以内で入力してください。", "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!byte.TryParse(PermissionLevelText, out var permissionLevel))
        {
            MessageBox.Show("権限レベルは0〜255の数値で入力してください。", "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var employee = new Employee
        {
            EmployeeCode = EmployeeCode,
            EmployeeName = EmployeeName,
            EmployeeNameKana = NullIfEmpty(EmployeeNameKana),
            PermissionLevel = permissionLevel,
            RowVersion = _loadedRowVersion,
            CreatedBy = string.Empty,
            CreatedAt = default,
            UpdatedBy = string.Empty,
            UpdatedAt = default,
        };

        try
        {
            var saved = IsNew
                ? await employeeService.CreateAsync(employee)
                : await employeeService.UpdateAsync(employee);

            // 登録後は画面を起動直後の状態へ戻す（docs/product-spec.md UI/UX節「登録後のリセット」）。
            var savedEmployeeCode = saved.EmployeeCode;
            ClearForm();
            IsNew = true;
            StatusMessage = $"{savedEmployeeCode} を保存しました。";
            NotifyResetToInitialState();
        }
        catch (EmployeeValidationException ex)
        {
            MessageBox.Show(ex.Message, "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        catch (EmployeeConcurrencyException ex)
        {
            MessageBox.Show(ex.Message, "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
    });

    [RelayCommand]
    private Task DeactivateAsync() => RunBusyAsync(async () =>
    {
        if (IsNew)
        {
            return;
        }

        var confirm = MessageBox.Show(
            $"{EmployeeCode} を無効化しますか？",
            "bmcs_app",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        var employee = new Employee
        {
            EmployeeCode = EmployeeCode,
            EmployeeName = EmployeeName,
            PermissionLevel = byte.TryParse(PermissionLevelText, out var permissionLevel) ? permissionLevel : (byte)0,
            RowVersion = _loadedRowVersion,
            CreatedBy = string.Empty,
            CreatedAt = default,
            UpdatedBy = string.Empty,
            UpdatedAt = default,
        };

        try
        {
            await employeeService.DeactivateAsync(employee);
        }
        catch (EmployeeConcurrencyException ex)
        {
            MessageBox.Show(ex.Message, "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        catch (EmployeeValidationException ex)
        {
            MessageBox.Show(ex.Message, "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ClearForm();
        IsNew = true;
        StatusMessage = $"{employee.EmployeeCode} を無効化しました。";
    });

    private void ClearForm()
    {
        EmployeeCode = string.Empty;
        EmployeeName = string.Empty;
        EmployeeNameKana = string.Empty;
        PermissionLevelText = string.Empty;
        _loadedRowVersion = null;
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
