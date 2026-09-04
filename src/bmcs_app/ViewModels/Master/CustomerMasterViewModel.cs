using System.Collections.ObjectModel;
using System.Windows;
using bmcs_app.Application.Master;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bmcs_app.ViewModels.Master;

/// <summary>得意先マスタ画面。一覧（左）と詳細フォーム（右）を持つ。</summary>
public partial class CustomerMasterViewModel(CustomerService customerService) : ViewModelBase
{
    public ObservableCollection<CustomerListItem> Customers { get; } = [];

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

    /// <summary>締め取引で選択可能な税区分（外税一括／外税伝票単位のみ）。</summary>
    public static IReadOnlyList<TaxUnit> ClosingTaxUnitOptions { get; } = [TaxUnit.Invoice, TaxUnit.Slip];

    /// <summary>端数区分の選択肢。</summary>
    public static IReadOnlyList<RoundingType> RoundingTypeOptions { get; } =
        Enum.GetValues<RoundingType>();

    /// <summary>締め取引のときのみ使用する締日（1〜31、末日締めは31）。</summary>
    [ObservableProperty]
    public partial string ClosingDayText { get; set; } = string.Empty;

    /// <summary>締め取引のときのみ使用する税区分（Invoice/Slipのいずれか）。</summary>
    [ObservableProperty]
    public partial TaxUnit ClosingTaxUnit { get; set; } = TaxUnit.Invoice;

    [ObservableProperty]
    public partial RoundingType RoundingType { get; set; } = RoundingType.RoundHalfUp;

    [ObservableProperty]
    public partial bool PrintRepresentativeFlag { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsClosingTypeEditable))]
    public partial bool IsNew { get; set; } = true;

    /// <summary>締め区分・税区分は登録後変更不可（TODO.md 2-1）。</summary>
    public bool IsClosingTypeEditable => IsNew;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    private byte[]? _loadedRowVersion;

    [RelayCommand]
    private Task LoadCustomersAsync() => RunBusyAsync(LoadCustomersCoreAsync);

    /// <summary>
    /// 一覧の再読み込み本体。<see cref="RunBusyAsync"/> でラップしない。
    /// Save/Deactivate から呼ぶ際は既に IsBusy=true になっており、二重にラップすると
    /// RunBusyAsync の多重実行抑止（if (IsBusy) return;）に引っかかって何もしないまま返ってしまう。
    /// </summary>
    private async Task LoadCustomersCoreAsync()
    {
        var customers = await customerService.GetCustomersAsync();

        Customers.Clear();
        foreach (var customer in customers)
        {
            Customers.Add(ToListItem(customer));
        }
    }

    [RelayCommand]
    private void NewCustomer()
    {
        ClearForm();
        IsNew = true;
        StatusMessage = string.Empty;
    }

    /// <summary>一覧で選択した得意先をフォームへ読み込む（RowActivationBehavior から Enter で呼ばれる）。</summary>
    [RelayCommand]
    private Task LoadSelectedCustomerAsync(object? item) => RunBusyAsync(async () =>
    {
        if (item is not CustomerListItem listItem)
        {
            return;
        }

        var customer = await customerService.GetByCodeAsync(listItem.CustomerCode);
        if (customer is null)
        {
            StatusMessage = "得意先が見つかりませんでした。再読み込みしてください。";
            return;
        }

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

        _loadedRowVersion = customer.RowVersion;
        IsNew = false;
        StatusMessage = string.Empty;
    });

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
            if (!byte.TryParse(ClosingDayText, out closingDay) || closingDay is < 1 or > 31)
            {
                MessageBox.Show("締日は1〜31で入力してください（末日締めは31）。", "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            taxUnit = ClosingTaxUnit;
        }

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
            ClosingDay = closingDay,
            TaxUnit = taxUnit,
            RoundingType = RoundingType,
            PrintRepresentativeFlag = PrintRepresentativeFlag,
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

            _loadedRowVersion = saved.RowVersion;
            IsNew = false;
            StatusMessage = $"{saved.CustomerCode} を保存しました。";
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

        await LoadCustomersCoreAsync();
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
        await LoadCustomersCoreAsync();
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
        _loadedRowVersion = null;
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static CustomerListItem ToListItem(Customer customer) => new(
        customer.CustomerCode,
        customer.CustomerName,
        customer.CustomerNameKana,
        ClosingDayDisplay: customer.ClosingDay switch
        {
            0 => "都度",
            31 => "末日締め",
            var day => $"{day}日締め",
        },
        TaxUnitDisplay: customer.TaxUnit switch
        {
            TaxUnit.Invoice => "外税一括",
            TaxUnit.Slip => "外税伝票単位",
            TaxUnit.Line => "内税明細単位",
            _ => customer.TaxUnit.ToString(),
        });
}
