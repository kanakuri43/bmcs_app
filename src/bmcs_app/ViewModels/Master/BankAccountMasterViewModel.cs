using System.Windows;
using bmcs_app.Application.Master;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Services;
using bmcs_app.ViewModels.Common;
using bmcs_app.Views.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bmcs_app.ViewModels.Master;

/// <summary>
/// 銀行マスタ画面。一覧は持たず、銀行口座コードを直接入力するか、
/// コード欄で Space を押して検索モーダル（<see cref="BankAccountMasterSearchDialog"/>）を呼び出して対象を選ぶ
/// （得意先マスタ・商品マスタ・社員マスタ画面と同じ Space検索／Enter読込のパターンに揃える）。
/// </summary>
public partial class BankAccountMasterViewModel(BankAccountService bankAccountService, WindowService windowService) : ViewModelBase
{
    [ObservableProperty]
    public partial string BankAccountCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string BankName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string BranchName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial BankAccountType AccountType { get; set; } = BankAccountType.Ordinary;

    /// <summary>口座種別の選択肢。</summary>
    public static IReadOnlyList<BankAccountType> AccountTypeOptions { get; } = Enum.GetValues<BankAccountType>();

    [ObservableProperty]
    public partial string AccountNumber { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AccountHolderName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsPrintOnInvoice { get; set; }

    /// <summary>表示順。文字列で保持し保存時に数値として検証する（社員マスタの権限レベルと同じ方式）。</summary>
    [ObservableProperty]
    public partial string DisplayOrderText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsNew { get; set; } = true;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    private byte[]? _loadedRowVersion;

    [RelayCommand]
    private void NewBankAccount()
    {
        ClearForm();
        IsNew = true;
        StatusMessage = string.Empty;
    }

    /// <summary>コード欄で Space を押したときに検索モーダルを開く。</summary>
    [RelayCommand]
    private void OpenBankAccountSearch()
    {
        var bankAccount = windowService.ShowDialog<BankAccountMasterSearchDialog, BankAccountMasterSearchDialogViewModel, BankAccount>();
        if (bankAccount is not null)
        {
            ApplyBankAccount(bankAccount);
        }
    }

    /// <summary>コード欄で Enter を押したときに、入力済みコードで直接読み込む。</summary>
    [RelayCommand]
    private Task LookupBankAccountByCodeAsync() => RunBusyAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(BankAccountCode))
        {
            return;
        }

        var bankAccount = await bankAccountService.GetByCodeAsync(BankAccountCode);
        if (bankAccount is null)
        {
            var enteredCode = BankAccountCode;
            ClearForm();
            BankAccountCode = enteredCode;
            IsNew = true;
            StatusMessage = $"銀行口座コード「{enteredCode}」は未登録です。新規登録として入力してください。";
            return;
        }

        ApplyBankAccount(bankAccount);
    });

    private void ApplyBankAccount(BankAccount bankAccount)
    {
        BankAccountCode = bankAccount.BankAccountCode;
        BankName = bankAccount.BankName;
        BranchName = bankAccount.BranchName;
        AccountType = bankAccount.AccountType;
        AccountNumber = bankAccount.AccountNumber;
        AccountHolderName = bankAccount.AccountHolderName;
        IsPrintOnInvoice = bankAccount.IsPrintOnInvoice;
        DisplayOrderText = bankAccount.DisplayOrder.ToString();

        _loadedRowVersion = bankAccount.RowVersion;
        IsNew = false;
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private Task SaveAsync() => RunBusyAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(BankAccountCode) || string.IsNullOrWhiteSpace(BankName)
            || string.IsNullOrWhiteSpace(BranchName) || string.IsNullOrWhiteSpace(AccountNumber)
            || string.IsNullOrWhiteSpace(AccountHolderName))
        {
            MessageBox.Show("銀行口座コード・銀行名・支店名・口座番号・口座名義は必須です。", "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (BankAccountCode.Length > 10)
        {
            MessageBox.Show("銀行口座コードは10文字以内で入力してください。", "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (AccountNumber.Length > 10)
        {
            MessageBox.Show("口座番号は10文字以内で入力してください。", "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!short.TryParse(DisplayOrderText, out var displayOrder))
        {
            MessageBox.Show("表示順は数値で入力してください。", "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var bankAccount = new BankAccount
        {
            BankAccountCode = BankAccountCode,
            BankName = BankName,
            BranchName = BranchName,
            AccountType = AccountType,
            AccountNumber = AccountNumber,
            AccountHolderName = AccountHolderName,
            IsPrintOnInvoice = IsPrintOnInvoice,
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
                ? await bankAccountService.CreateAsync(bankAccount)
                : await bankAccountService.UpdateAsync(bankAccount);

            // 登録後は画面を起動直後の状態へ戻す（docs/product-spec.md UI/UX節「登録後のリセット」）。
            var savedBankAccountCode = saved.BankAccountCode;
            ClearForm();
            IsNew = true;
            StatusMessage = $"{savedBankAccountCode} を保存しました。";
            NotifyResetToInitialState();
        }
        catch (BankAccountValidationException ex)
        {
            MessageBox.Show(ex.Message, "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        catch (BankAccountConcurrencyException ex)
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
            $"{BankAccountCode} を無効化しますか？",
            "bmcs_app",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        var bankAccount = new BankAccount
        {
            BankAccountCode = BankAccountCode,
            BankName = BankName,
            BranchName = BranchName,
            AccountType = AccountType,
            AccountNumber = AccountNumber,
            AccountHolderName = AccountHolderName,
            IsPrintOnInvoice = IsPrintOnInvoice,
            DisplayOrder = short.TryParse(DisplayOrderText, out var displayOrder) ? displayOrder : (short)0,
            RowVersion = _loadedRowVersion,
            CreatedBy = string.Empty,
            CreatedAt = default,
            UpdatedBy = string.Empty,
            UpdatedAt = default,
        };

        try
        {
            await bankAccountService.DeactivateAsync(bankAccount);
        }
        catch (BankAccountConcurrencyException ex)
        {
            MessageBox.Show(ex.Message, "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        catch (BankAccountValidationException ex)
        {
            MessageBox.Show(ex.Message, "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ClearForm();
        IsNew = true;
        StatusMessage = $"{bankAccount.BankAccountCode} を無効化しました。";
    });

    private void ClearForm()
    {
        BankAccountCode = string.Empty;
        BankName = string.Empty;
        BranchName = string.Empty;
        AccountType = BankAccountType.Ordinary;
        AccountNumber = string.Empty;
        AccountHolderName = string.Empty;
        IsPrintOnInvoice = false;
        DisplayOrderText = string.Empty;
        _loadedRowVersion = null;
    }
}
