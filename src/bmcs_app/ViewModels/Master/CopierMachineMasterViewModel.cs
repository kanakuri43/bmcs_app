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
/// コピー機マスタ画面。一覧は持たず、機番を直接入力するか、機番欄で Space を押して検索モーダルから選ぶ。
/// 得意先コード欄は Space で得意先検索、Enter で得意先名を照会する。
/// </summary>
public partial class CopierMachineMasterViewModel(
    CopierMachineService copierMachineService, CustomerService customerService, WindowService windowService)
    : ViewModelBase
{
    [ObservableProperty]
    public partial string MachineNo { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CustomerCode { get; set; } = string.Empty;

    /// <summary>得意先コードに対応する得意先名（照会結果の表示用）。</summary>
    [ObservableProperty]
    public partial string CustomerName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MachineModel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Remarks { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsNew { get; set; } = true;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    private byte[]? _loadedRowVersion;

    [RelayCommand]
    private void NewMachine()
    {
        ClearForm();
        IsNew = true;
        StatusMessage = string.Empty;
    }

    /// <summary>機番欄で Space を押したときに検索モーダルを開く。</summary>
    [RelayCommand]
    private Task OpenMachineSearchAsync() => RunBusyAsync(async () =>
    {
        var machine = windowService.ShowDialog<CopierMachineMasterSearchDialog, CopierMachineMasterSearchDialogViewModel, CopierMachine>();
        if (machine is not null)
        {
            await ApplyMachineAsync(machine);
            RequestFocus("Customer");
        }
    });

    /// <summary>機番欄で Enter を押したときに、入力済みの機番で直接読み込む。</summary>
    [RelayCommand]
    private Task LookupMachineByNoAsync() => RunBusyAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(MachineNo))
        {
            return;
        }

        var machine = await copierMachineService.GetByMachineNoAsync(MachineNo);
        if (machine is null)
        {
            var enteredNo = MachineNo;
            ClearForm();
            MachineNo = enteredNo;
            IsNew = true;
            StatusMessage = $"機番「{enteredNo}」は未登録です。新規登録として入力してください。";
            return;
        }

        await ApplyMachineAsync(machine);
    });

    private async Task ApplyMachineAsync(CopierMachine machine)
    {
        MachineNo = machine.MachineNo;
        CustomerCode = machine.CustomerCode;
        MachineModel = machine.MachineModel ?? string.Empty;
        Remarks = machine.Remarks ?? string.Empty;
        _loadedRowVersion = machine.RowVersion;
        IsNew = false;
        StatusMessage = machine.IsDeleted ? "この機番は無効化されています。" : string.Empty;
        await RefreshCustomerNameAsync();
    }

    /// <summary>得意先コード欄で Space を押したときに得意先検索モーダルを開く。</summary>
    [RelayCommand]
    private void OpenCustomerSearch()
    {
        var customer = windowService.ShowDialog<CustomerSearchDialog, CustomerSearchDialogViewModel, Customer>();
        if (customer is not null)
        {
            CustomerCode = customer.CustomerCode;
            CustomerName = customer.CustomerName;
            RequestFocus("Model");
        }
    }

    /// <summary>得意先コード欄で Enter を押したときに、入力済みコードの得意先名を照会する。</summary>
    [RelayCommand]
    private Task LookupCustomerByCodeAsync() => RunBusyAsync(RefreshCustomerNameAsync);

    private async Task RefreshCustomerNameAsync()
    {
        if (string.IsNullOrWhiteSpace(CustomerCode))
        {
            CustomerName = string.Empty;
            return;
        }

        var customer = await customerService.GetByCodeAsync(CustomerCode);
        CustomerName = customer is null ? "（該当なし）" : customer.IsDeleted ? "（無効）" : customer.CustomerName;
    }

    [RelayCommand]
    private Task SaveAsync() => RunBusyAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(MachineNo) || string.IsNullOrWhiteSpace(CustomerCode))
        {
            MessageBox.Show("機番・得意先コードは必須です。", "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (MachineNo.Length > 20)
        {
            MessageBox.Show("機番は20文字以内で入力してください。", "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var machine = BuildEntity();
        try
        {
            var saved = IsNew
                ? await copierMachineService.CreateAsync(machine)
                : await copierMachineService.UpdateAsync(machine);

            // 登録後は画面を起動直後の状態へ戻す（docs/product-spec.md UI/UX節「登録後のリセット」）。
            var savedMachineNo = saved.MachineNo;
            ClearForm();
            IsNew = true;
            StatusMessage = $"{savedMachineNo} を保存しました。";
            NotifyResetToInitialState();
        }
        catch (Exception ex) when (ex is CopierMachineValidationException or CopierMachineConcurrencyException)
        {
            MessageBox.Show(ex.Message, "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    });

    [RelayCommand]
    private Task DeactivateAsync() => RunBusyAsync(async () =>
    {
        if (IsNew)
        {
            return;
        }

        var confirm = MessageBox.Show($"{MachineNo} を無効化しますか？", "bmcs_app", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        var machine = BuildEntity();
        try
        {
            await copierMachineService.DeactivateAsync(machine);
        }
        catch (Exception ex) when (ex is CopierMachineValidationException or CopierMachineConcurrencyException)
        {
            MessageBox.Show(ex.Message, "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ClearForm();
        IsNew = true;
        StatusMessage = $"{machine.MachineNo} を無効化しました。";
    });

    private CopierMachine BuildEntity() => new()
    {
        MachineNo = MachineNo,
        CustomerCode = CustomerCode,
        MachineModel = string.IsNullOrWhiteSpace(MachineModel) ? null : MachineModel,
        Remarks = string.IsNullOrWhiteSpace(Remarks) ? null : Remarks,
        RowVersion = _loadedRowVersion,
        CreatedBy = string.Empty,
        CreatedAt = default,
        UpdatedBy = string.Empty,
        UpdatedAt = default,
    };

    private void ClearForm()
    {
        MachineNo = string.Empty;
        CustomerCode = string.Empty;
        CustomerName = string.Empty;
        MachineModel = string.Empty;
        Remarks = string.Empty;
        _loadedRowVersion = null;
    }
}
