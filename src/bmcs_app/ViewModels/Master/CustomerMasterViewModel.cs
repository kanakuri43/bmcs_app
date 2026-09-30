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
/// 得意先マスタ画面。一覧は持たず、得意先コードを直接入力するか、
/// コード欄で Space を押して検索モーダル（<see cref="CustomerSearchDialog"/>）を呼び出して対象を選ぶ
/// （受注入力・売上入力画面と同じ SPACEで検索／Enter読込のパターンに揃える）。
/// </summary>
public partial class CustomerMasterViewModel(
    CustomerService customerService, BankAccountService bankAccountService, WindowService windowService) : ViewModelBase
{
    [ObservableProperty]
    public partial string CustomerCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CustomerName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CustomerNameKana { get; set; } = string.Empty;

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
    public partial string SalesEmployeeCode { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsClosingTransaction))]
    public partial TransactionType TransactionType { get; set; } = TransactionType.OneOff;

    /// <summary>締め取引かどうか（締日・税区分の入力欄の表示切替に使う）。</summary>
    public bool IsClosingTransaction => TransactionType == TransactionType.Closing;

    /// <summary>締め取引で選択可能な税区分（請求単位／伝票単位のみ）。</summary>
    public static IReadOnlyList<TaxUnit> ClosingTaxUnitOptions { get; } = [TaxUnit.Invoice, TaxUnit.Slip];

    /// <summary>端数区分の選択肢。</summary>
    public static IReadOnlyList<RoundingType> RoundingTypeOptions { get; } =
        Enum.GetValues<RoundingType>();

    /// <summary>締め取引のときのみ使用する締日（1〜31、末日締めは99）。</summary>
    [ObservableProperty]
    public partial string ClosingDayText { get; set; } = string.Empty;

    /// <summary>締め取引のときのみ使用する税区分（Invoice/Slipのいずれか）。</summary>
    [ObservableProperty]
    public partial TaxUnit ClosingTaxUnit { get; set; } = TaxUnit.Invoice;

    [ObservableProperty]
    public partial RoundingType RoundingType { get; set; } = RoundingType.RoundHalfUp;

    [ObservableProperty]
    public partial bool PrintRepresentativeFlag { get; set; }

    /// <summary>請求書に印字する振込先口座1（<c>bank_accounts</c>）のコード。空欄＝紐づけなし。</summary>
    [ObservableProperty]
    public partial string BankAccountCode1Text { get; set; } = string.Empty;

    /// <summary>振込先口座1欄に入力されたコードの銀行名等（照会結果の表示専用）。</summary>
    [ObservableProperty]
    public partial string BankAccount1Label { get; set; } = string.Empty;

    /// <summary>請求書に印字する振込先口座2（<c>bank_accounts</c>）のコード。空欄＝紐づけなし。</summary>
    [ObservableProperty]
    public partial string BankAccountCode2Text { get; set; } = string.Empty;

    /// <summary>振込先口座2欄に入力されたコードの銀行名等（照会結果の表示専用）。</summary>
    [ObservableProperty]
    public partial string BankAccount2Label { get; set; } = string.Empty;

    /// <summary>
    /// 請求得意先コード（親子請求・請求集約、2026-09-29確定）。空欄は「自分自身＝単独で請求」を意味する。
    /// 他の得意先コードを入力すると、その得意先（請求集約先）に売上が集約される。
    /// </summary>
    [ObservableProperty]
    public partial string BillingCustomerCodeText { get; set; } = string.Empty;

    /// <summary>請求得意先コード欄に入力されたコードの得意先名（照会結果の表示専用）。</summary>
    [ObservableProperty]
    public partial string BillingCustomerName { get; set; } = string.Empty;

    /// <summary>
    /// 請求得意先コードの編集可否（業務ルール7）。新規登録時は常に可、更新時は
    /// 確定済み請求に取り込まれた売上の有無等をサービスに問い合わせて決める
    /// （closing_day/tax_unit/rounding_typeとは異なり新規登録時限定ではない）。
    /// </summary>
    [ObservableProperty]
    public partial bool IsBillingCustomerEditable { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsClosingTypeEditable))]
    public partial bool IsNew { get; set; } = true;

    /// <summary>締め区分・税区分は登録後変更不可（TODO.md 2-1）。</summary>
    public bool IsClosingTypeEditable => IsNew;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    private byte[]? _loadedRowVersion;

    [RelayCommand]
    private void NewCustomer()
    {
        ClearForm();
        IsNew = true;
        StatusMessage = string.Empty;
    }

    /// <summary>コード欄で Space を押したときに検索モーダルを開く。</summary>
    [RelayCommand]
    private Task OpenCustomerSearchAsync() => RunBusyAsync(async () =>
    {
        var customer = windowService.ShowDialog<CustomerSearchDialog, CustomerSearchDialogViewModel, Customer>();
        if (customer is not null)
        {
            await ApplyCustomerAsync(customer);
            RequestFocus("Name");
        }
    });

    /// <summary>コード欄で Enter を押したときに、入力済みコードで直接読み込む。</summary>
    [RelayCommand]
    private Task LookupCustomerByCodeAsync() => RunBusyAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(CustomerCode))
        {
            return;
        }

        var customer = await customerService.GetByCodeAsync(CustomerCode);
        if (customer is null)
        {
            var enteredCode = CustomerCode;
            ClearForm();
            CustomerCode = enteredCode;
            IsNew = true;
            StatusMessage = $"得意先コード「{enteredCode}」は未登録です。新規登録として入力してください。";
            return;
        }

        await ApplyCustomerAsync(customer);
    });

    /// <summary>請求得意先コード欄で Space を押したときに検索モーダルを開く（請求集約先のみ表示）。</summary>
    [RelayCommand]
    private Task OpenBillingCustomerSearchAsync() => RunBusyAsync(async () =>
    {
        var customer = windowService.ShowDialog<CustomerSearchDialog, CustomerSearchDialogViewModel, Customer>(
            vm => vm.BillingRootOnly = true);
        if (customer is not null)
        {
            BillingCustomerCodeText = customer.CustomerCode;
            BillingCustomerName = customer.CustomerName;
            RequestFocus(IsClosingTypeEditable ? "RoundingType" : "PrintRepresentative");
        }
    });

    /// <summary>請求得意先コード欄で Enter を押したときに、入力済みコードの得意先名を照会する。</summary>
    [RelayCommand]
    private Task LookupBillingCustomerByCodeAsync() => RunBusyAsync(RefreshBillingCustomerNameAsync);

    private async Task RefreshBillingCustomerNameAsync()
    {
        if (string.IsNullOrWhiteSpace(BillingCustomerCodeText))
        {
            BillingCustomerName = string.Empty;
            return;
        }

        var billingCustomer = await customerService.GetByCodeAsync(BillingCustomerCodeText);
        BillingCustomerName = billingCustomer?.CustomerName ?? "（該当なし）";
    }

    /// <summary>振込先口座1欄で Space を押したときに検索モーダルを開く。</summary>
    [RelayCommand]
    private Task OpenBankAccount1SearchAsync() => RunBusyAsync(async () =>
    {
        var bankAccount = windowService.ShowDialog<BankAccountMasterSearchDialog, BankAccountMasterSearchDialogViewModel, BankAccount>();
        if (bankAccount is not null)
        {
            BankAccountCode1Text = bankAccount.BankAccountCode;
            BankAccount1Label = FormatBankAccountLabel(bankAccount);
            RequestFocus("BankAccount2");
        }
    });

    /// <summary>振込先口座1欄で Enter を押したときに、入力済みコードの銀行名等を照会する。</summary>
    [RelayCommand]
    private Task LookupBankAccount1ByCodeAsync() => RunBusyAsync(RefreshBankAccount1LabelAsync);

    private async Task RefreshBankAccount1LabelAsync()
    {
        if (string.IsNullOrWhiteSpace(BankAccountCode1Text))
        {
            BankAccount1Label = string.Empty;
            return;
        }

        var bankAccount = await bankAccountService.GetByCodeAsync(BankAccountCode1Text);
        BankAccount1Label = bankAccount is null ? "（該当なし）" : FormatBankAccountLabel(bankAccount);
    }

    /// <summary>振込先口座2欄で Space を押したときに検索モーダルを開く。</summary>
    [RelayCommand]
    private Task OpenBankAccount2SearchAsync() => RunBusyAsync(async () =>
    {
        var bankAccount = windowService.ShowDialog<BankAccountMasterSearchDialog, BankAccountMasterSearchDialogViewModel, BankAccount>();
        if (bankAccount is not null)
        {
            BankAccountCode2Text = bankAccount.BankAccountCode;
            BankAccount2Label = FormatBankAccountLabel(bankAccount);
            RequestFocus("NewButton");
        }
    });

    /// <summary>振込先口座2欄で Enter を押したときに、入力済みコードの銀行名等を照会する。</summary>
    [RelayCommand]
    private Task LookupBankAccount2ByCodeAsync() => RunBusyAsync(RefreshBankAccount2LabelAsync);

    private async Task RefreshBankAccount2LabelAsync()
    {
        if (string.IsNullOrWhiteSpace(BankAccountCode2Text))
        {
            BankAccount2Label = string.Empty;
            return;
        }

        var bankAccount = await bankAccountService.GetByCodeAsync(BankAccountCode2Text);
        BankAccount2Label = bankAccount is null ? "（該当なし）" : FormatBankAccountLabel(bankAccount);
    }

    private static string FormatBankAccountLabel(BankAccount bankAccount)
        => $"{bankAccount.BankName}　{bankAccount.BranchName}支店";

    private async Task ApplyCustomerAsync(Customer customer)
    {
        CustomerCode = customer.CustomerCode;
        CustomerName = customer.CustomerName;
        CustomerNameKana = customer.CustomerNameKana ?? string.Empty;
        PostalCode = customer.PostalCode ?? string.Empty;
        Address1 = customer.Address1 ?? string.Empty;
        Address2 = customer.Address2 ?? string.Empty;
        PhoneNumber = customer.PhoneNumber ?? string.Empty;
        FaxNumber = customer.FaxNumber ?? string.Empty;
        ContactPersonName = customer.ContactPersonName ?? string.Empty;
        SalesEmployeeCode = customer.SalesEmployeeCode ?? string.Empty;
        RoundingType = customer.RoundingType;
        PrintRepresentativeFlag = customer.PrintRepresentativeFlag;

        if (customer.ClosingDay == 0)
        {
            TransactionType = TransactionType.OneOff;
            ClosingDayText = string.Empty;
            ClosingTaxUnit = TaxUnit.Invoice;
        }
        else
        {
            TransactionType = TransactionType.Closing;
            ClosingDayText = customer.ClosingDay.ToString();
            ClosingTaxUnit = customer.TaxUnit;
        }

        BillingCustomerCodeText = customer.IsBillingRoot ? string.Empty : customer.BillingCustomerCode;
        await RefreshBillingCustomerNameAsync();
        IsBillingCustomerEditable = await customerService.CanChangeBillingCustomerAsync(customer.CustomerCode);

        BankAccountCode1Text = customer.BankAccountCode1 ?? string.Empty;
        await RefreshBankAccount1LabelAsync();
        BankAccountCode2Text = customer.BankAccountCode2 ?? string.Empty;
        await RefreshBankAccount2LabelAsync();

        _loadedRowVersion = customer.RowVersion;
        IsNew = false;
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private Task SaveAsync() => RunBusyAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(CustomerCode) || string.IsNullOrWhiteSpace(CustomerName))
        {
            MessageBox.Show("得意先コードと得意先名は必須です。", "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (CustomerCode.Length > 10)
        {
            MessageBox.Show("得意先コードは10文字以内で入力してください。", "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        byte closingDay;
        TaxUnit taxUnit;

        if (TransactionType == TransactionType.OneOff)
        {
            closingDay = 0;
            taxUnit = TaxUnit.Line;
        }
        else
        {
            if (!byte.TryParse(ClosingDayText, out closingDay) || closingDay is (< 1 or > 31) and not 99)
            {
                MessageBox.Show("締日は1〜31で入力してください（末日締めは99）。", "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            taxUnit = ClosingTaxUnit;
        }

        var billingCustomerCode = string.IsNullOrWhiteSpace(BillingCustomerCodeText) ? CustomerCode : BillingCustomerCodeText;

        var customer = new Customer
        {
            CustomerCode = CustomerCode,
            CustomerName = CustomerName,
            CustomerNameKana = NullIfEmpty(CustomerNameKana),
            PostalCode = NullIfEmpty(PostalCode),
            Address1 = NullIfEmpty(Address1),
            Address2 = NullIfEmpty(Address2),
            PhoneNumber = NullIfEmpty(PhoneNumber),
            FaxNumber = NullIfEmpty(FaxNumber),
            ContactPersonName = NullIfEmpty(ContactPersonName),
            SalesEmployeeCode = NullIfEmpty(SalesEmployeeCode),
            BankAccountCode1 = NullIfEmpty(BankAccountCode1Text),
            BankAccountCode2 = NullIfEmpty(BankAccountCode2Text),
            ClosingDay = closingDay,
            TaxUnit = taxUnit,
            RoundingType = RoundingType,
            PrintRepresentativeFlag = PrintRepresentativeFlag,
            BillingCustomerCode = billingCustomerCode,
            RowVersion = _loadedRowVersion,
            CreatedBy = string.Empty,
            CreatedAt = default,
            UpdatedBy = string.Empty,
            UpdatedAt = default,
        };

        try
        {
            var saved = IsNew
                ? await customerService.CreateAsync(customer)
                : await customerService.UpdateAsync(customer);

            // 登録後は画面を起動直後の状態へ戻す（docs/product-spec.md UI/UX節「登録後のリセット」）。
            var savedCustomerCode = saved.CustomerCode;
            ClearForm();
            IsNew = true;
            StatusMessage = $"{savedCustomerCode} を保存しました。";
            NotifyResetToInitialState();
        }
        catch (CustomerValidationException ex)
        {
            MessageBox.Show(ex.Message, "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        catch (CustomerConcurrencyException ex)
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
            $"{CustomerCode} を無効化しますか？",
            "bmcs_app",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        var customer = new Customer
        {
            CustomerCode = CustomerCode,
            CustomerName = CustomerName,
            ClosingDay = 0,
            TaxUnit = TaxUnit.Line,
            RoundingType = RoundingType,
            PrintRepresentativeFlag = PrintRepresentativeFlag,
            BillingCustomerCode = CustomerCode,
            RowVersion = _loadedRowVersion,
            CreatedBy = string.Empty,
            CreatedAt = default,
            UpdatedBy = string.Empty,
            UpdatedAt = default,
        };

        try
        {
            await customerService.DeactivateAsync(customer);
        }
        catch (CustomerConcurrencyException ex)
        {
            MessageBox.Show(ex.Message, "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        catch (CustomerValidationException ex)
        {
            MessageBox.Show(ex.Message, "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ClearForm();
        IsNew = true;
        StatusMessage = $"{customer.CustomerCode} を無効化しました。";
    });

    private void ClearForm()
    {
        CustomerCode = string.Empty;
        CustomerName = string.Empty;
        CustomerNameKana = string.Empty;
        PostalCode = string.Empty;
        Address1 = string.Empty;
        Address2 = string.Empty;
        PhoneNumber = string.Empty;
        FaxNumber = string.Empty;
        ContactPersonName = string.Empty;
        SalesEmployeeCode = string.Empty;
        TransactionType = TransactionType.OneOff;
        ClosingDayText = string.Empty;
        ClosingTaxUnit = TaxUnit.Invoice;
        RoundingType = RoundingType.RoundHalfUp;
        PrintRepresentativeFlag = false;
        BillingCustomerCodeText = string.Empty;
        BillingCustomerName = string.Empty;
        IsBillingCustomerEditable = true;
        BankAccountCode1Text = string.Empty;
        BankAccount1Label = string.Empty;
        BankAccountCode2Text = string.Empty;
        BankAccount2Label = string.Empty;
        _loadedRowVersion = null;
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
