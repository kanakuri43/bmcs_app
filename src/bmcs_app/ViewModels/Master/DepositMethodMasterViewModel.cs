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
/// 入金方法マスタ画面。一覧は持たず、入金方法コードを直接入力するか、
/// コード欄で Space を押して検索モーダル（<see cref="DepositMethodMasterSearchDialog"/>）を呼び出して対象を選ぶ
/// （銀行マスタ等、既存マスタ画面と同じ SPACEで検索／Enter読込のパターンに揃える）。
/// </summary>
public partial class DepositMethodMasterViewModel(DepositMethodService depositMethodService, WindowService windowService) : ViewModelBase
{
    [ObservableProperty]
    public partial string DepositMethodCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DepositMethodName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool RequiresBankAccount { get; set; }

    [ObservableProperty]
    public partial bool RequiresBillDueDate { get; set; }

    /// <summary>表示順。文字列で保持し保存時に数値として検証する（社員マスタの権限レベルと同じ方式）。</summary>
    [ObservableProperty]
    public partial string DisplayOrderText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsNew { get; set; } = true;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    private byte[]? _loadedRowVersion;

    [RelayCommand]
    private void NewDepositMethod()
    {
        ClearForm();
        IsNew = true;
        StatusMessage = string.Empty;
    }

    /// <summary>コード欄で Space を押したときに検索モーダルを開く。</summary>
    [RelayCommand]
    private void OpenDepositMethodSearch()
    {
        var depositMethod = windowService.ShowDialog<DepositMethodMasterSearchDialog, DepositMethodMasterSearchDialogViewModel, DepositMethod>();
        if (depositMethod is not null)
        {
            ApplyDepositMethod(depositMethod);
            RequestFocus("Name");
        }
    }

    /// <summary>コード欄で Enter を押したときに、入力済みコードで直接読み込む。</summary>
    [RelayCommand]
    private Task LookupDepositMethodByCodeAsync() => RunBusyAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(DepositMethodCode))
        {
            return;
        }

        var depositMethod = await depositMethodService.GetByCodeAsync(DepositMethodCode);
        if (depositMethod is null)
        {
            var enteredCode = DepositMethodCode;
            ClearForm();
            DepositMethodCode = enteredCode;
            IsNew = true;
            StatusMessage = $"入金方法コード「{enteredCode}」は未登録です。新規登録として入力してください。";
            return;
        }

        ApplyDepositMethod(depositMethod);
    });

    private void ApplyDepositMethod(DepositMethod depositMethod)
    {
        DepositMethodCode = depositMethod.DepositMethodCode;
        DepositMethodName = depositMethod.DepositMethodName;
        RequiresBankAccount = depositMethod.RequiresBankAccount;
        RequiresBillDueDate = depositMethod.RequiresBillDueDate;
        DisplayOrderText = depositMethod.DisplayOrder.ToString();

        _loadedRowVersion = depositMethod.RowVersion;
        IsNew = false;
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private Task SaveAsync() => RunBusyAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(DepositMethodCode) || string.IsNullOrWhiteSpace(DepositMethodName))
        {
            MessageBox.Show("入金方法コード・名称は必須です。", "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (DepositMethodCode.Length > 10)
        {
            MessageBox.Show("入金方法コードは10文字以内で入力してください。", "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (RequiresBankAccount && RequiresBillDueDate)
        {
            MessageBox.Show("入金先口座と手形期日の両方を必須にすることはできません。", "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!short.TryParse(DisplayOrderText, out var displayOrder))
        {
            MessageBox.Show("表示順は数値で入力してください。", "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var depositMethod = new DepositMethod
        {
            DepositMethodCode = DepositMethodCode,
            DepositMethodName = DepositMethodName,
            RequiresBankAccount = RequiresBankAccount,
            RequiresBillDueDate = RequiresBillDueDate,
            DisplayOrder = displayOrder,
            RowVersion = _loadedRowVersion,
            CreatedBy = string.Empty,
            CreatedAt = default,
            UpdatedBy = string.Empty,
            UpdatedAt = default,
        };

        try
        {
            var saved = IsNew
                ? await depositMethodService.CreateAsync(depositMethod)
                : await depositMethodService.UpdateAsync(depositMethod);

            // 登録後は画面を起動直後の状態へ戻す（docs/product-spec.md UI/UX節「登録後のリセット」）。
            var savedDepositMethodCode = saved.DepositMethodCode;
            ClearForm();
            IsNew = true;
            StatusMessage = $"{savedDepositMethodCode} を保存しました。";
            NotifyResetToInitialState();
        }
        catch (DepositMethodValidationException ex)
        {
            MessageBox.Show(ex.Message, "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        catch (DepositMethodConcurrencyException ex)
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
            $"{DepositMethodCode} を無効化しますか？",
            "bmcs_app",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        var depositMethod = new DepositMethod
        {
            DepositMethodCode = DepositMethodCode,
            DepositMethodName = DepositMethodName,
            RequiresBankAccount = RequiresBankAccount,
            RequiresBillDueDate = RequiresBillDueDate,
            DisplayOrder = short.TryParse(DisplayOrderText, out var displayOrder) ? displayOrder : (short)0,
            RowVersion = _loadedRowVersion,
            CreatedBy = string.Empty,
            CreatedAt = default,
            UpdatedBy = string.Empty,
            UpdatedAt = default,
        };

        try
        {
            await depositMethodService.DeactivateAsync(depositMethod);
        }
        catch (DepositMethodConcurrencyException ex)
        {
            MessageBox.Show(ex.Message, "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        catch (DepositMethodValidationException ex)
        {
            MessageBox.Show(ex.Message, "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ClearForm();
        IsNew = true;
        StatusMessage = $"{depositMethod.DepositMethodCode} を無効化しました。";
    });

    private void ClearForm()
    {
        DepositMethodCode = string.Empty;
        DepositMethodName = string.Empty;
        RequiresBankAccount = false;
        RequiresBillDueDate = false;
        DisplayOrderText = string.Empty;
        _loadedRowVersion = null;
    }
}
