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
/// 仕入先マスタ画面。一覧は持たず、仕入先コードを直接入力するか、
/// コード欄で Space を押して検索モーダル（<see cref="SupplierMasterSearchDialog"/>）から対象を選ぶ
/// （銀行マスタ・商品マスタ画面と同じ SPACEで検索／Enter読込のパターン）。
/// </summary>
public partial class SupplierMasterViewModel(SupplierService supplierService, WindowService windowService) : ViewModelBase
{
    [ObservableProperty]
    public partial string SupplierCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SupplierName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SupplierNameKana { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PostalCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Address1 { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Address2 { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PhoneNumber { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FaxNumber { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ContactPersonName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Remarks { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsNew { get; set; } = true;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    /// <summary>コード欄の横に出す「未登録コード→新規登録」の通知。コードを編集したら消える。</summary>
    [ObservableProperty]
    public partial string NewCodeMessage { get; set; } = string.Empty;

    partial void OnSupplierCodeChanged(string value) => NewCodeMessage = string.Empty;

    private byte[]? _loadedRowVersion;

    [RelayCommand]
    private void NewSupplier()
    {
        ClearForm();
        IsNew = true;
        StatusMessage = string.Empty;
    }

    /// <summary>コード欄で Space を押したときに検索モーダルを開く。</summary>
    [RelayCommand]
    private void OpenSupplierSearch()
    {
        var supplier = windowService.ShowDialog<SupplierMasterSearchDialog, SupplierMasterSearchDialogViewModel, Supplier>();
        if (supplier is not null)
        {
            ApplySupplier(supplier);
            RequestFocus("Name");
        }
    }

    /// <summary>コード欄で Enter を押したときに、入力済みコードで直接読み込む。</summary>
    [RelayCommand]
    private Task LookupSupplierByCodeAsync() => RunBusyAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(SupplierCode))
        {
            return;
        }

        var supplier = await supplierService.GetByCodeAsync(SupplierCode);
        if (supplier is null)
        {
            var enteredCode = SupplierCode;
            ClearForm();
            SupplierCode = enteredCode;
            IsNew = true;
            NewCodeMessage = "未登録のコードです。新規登録として入力してください。";
            RequestFocus("Name");
            return;
        }

        ApplySupplier(supplier);
    });

    private void ApplySupplier(Supplier supplier)
    {
        SupplierCode = supplier.SupplierCode;
        SupplierName = supplier.SupplierName;
        SupplierNameKana = supplier.SupplierNameKana ?? string.Empty;
        PostalCode = supplier.PostalCode ?? string.Empty;
        Address1 = supplier.Address1 ?? string.Empty;
        Address2 = supplier.Address2 ?? string.Empty;
        PhoneNumber = supplier.PhoneNumber ?? string.Empty;
        FaxNumber = supplier.FaxNumber ?? string.Empty;
        ContactPersonName = supplier.ContactPersonName ?? string.Empty;
        Remarks = supplier.Remarks ?? string.Empty;

        _loadedRowVersion = supplier.RowVersion;
        IsNew = false;
        StatusMessage = supplier.IsDeleted ? "この仕入先は無効化されています。" : string.Empty;
    }

    [RelayCommand]
    private Task SaveAsync() => RunBusyAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(SupplierCode) || string.IsNullOrWhiteSpace(SupplierName))
        {
            Warn("仕入先コード・仕入先名は必須です。");
            return;
        }

        var lengthError = FirstLengthError(
            ("仕入先コード", SupplierCode, 10),
            ("仕入先名", SupplierName, 60),
            ("仕入先名カナ", SupplierNameKana, 60),
            ("郵便番号", PostalCode, 8),
            ("住所1", Address1, 100),
            ("住所2", Address2, 100),
            ("電話番号", PhoneNumber, 20),
            ("FAX番号", FaxNumber, 20),
            ("先方担当者", ContactPersonName, 40),
            ("備考", Remarks, 100));
        if (lengthError is not null)
        {
            Warn(lengthError);
            return;
        }

        try
        {
            var supplier = BuildSupplier();
            var saved = IsNew
                ? await supplierService.CreateAsync(supplier)
                : await supplierService.UpdateAsync(supplier);

            // 登録後は画面を起動直後の状態へ戻す（docs/product-spec.md UI/UX節「登録後のリセット」）。
            var savedCode = saved.SupplierCode;
            ClearForm();
            IsNew = true;
            StatusMessage = $"{savedCode} を保存しました。";
            NotifyResetToInitialState();
        }
        catch (SupplierValidationException ex)
        {
            Warn(ex.Message);
        }
        catch (SupplierConcurrencyException ex)
        {
            Warn(ex.Message);
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
            $"{SupplierCode} を無効化しますか？",
            "bmcs_app",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        var deactivatedCode = SupplierCode;
        try
        {
            await supplierService.DeactivateAsync(BuildSupplier());
        }
        catch (SupplierValidationException ex)
        {
            Warn(ex.Message);
            return;
        }
        catch (SupplierConcurrencyException ex)
        {
            Warn(ex.Message);
            return;
        }

        ClearForm();
        IsNew = true;
        StatusMessage = $"{deactivatedCode} を無効化しました。";
    });

    private Supplier BuildSupplier() => new()
    {
        SupplierCode = SupplierCode.Trim(),
        SupplierName = SupplierName.Trim(),
        SupplierNameKana = NullIfBlank(SupplierNameKana),
        PostalCode = NullIfBlank(PostalCode),
        Address1 = NullIfBlank(Address1),
        Address2 = NullIfBlank(Address2),
        PhoneNumber = NullIfBlank(PhoneNumber),
        FaxNumber = NullIfBlank(FaxNumber),
        ContactPersonName = NullIfBlank(ContactPersonName),
        Remarks = NullIfBlank(Remarks),
        RowVersion = _loadedRowVersion,
        CreatedBy = string.Empty,
        CreatedAt = default,
        UpdatedBy = string.Empty,
        UpdatedAt = default,
    };

    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? FirstLengthError(params (string Label, string Value, int Max)[] fields)
    {
        foreach (var (label, value, max) in fields)
        {
            if (value.Trim().Length > max)
            {
                return $"{label}は{max}文字以内で入力してください。";
            }
        }

        return null;
    }

    private static void Warn(string message)
        => MessageBox.Show(message, "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);

    private void ClearForm()
    {
        SupplierCode = string.Empty;
        SupplierName = string.Empty;
        SupplierNameKana = string.Empty;
        PostalCode = string.Empty;
        Address1 = string.Empty;
        Address2 = string.Empty;
        PhoneNumber = string.Empty;
        FaxNumber = string.Empty;
        ContactPersonName = string.Empty;
        Remarks = string.Empty;
        _loadedRowVersion = null;
    }
}
