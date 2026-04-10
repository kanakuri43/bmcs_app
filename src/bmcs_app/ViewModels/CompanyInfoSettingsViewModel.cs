using bmcs_app.Infrastructure;
using bmcs_app.Infrastructure.Repositories;
using Prism.Commands;
using Prism.Mvvm;

namespace bmcs_app.ViewModels;

public class CompanyInfoSettingsViewModel : BindableBase
{
    private readonly CompanyInfoRepository _repo = new();

    private string _name = "";
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    private string _address = "";
    public string Address
    {
        get => _address;
        set => SetProperty(ref _address, value);
    }

    private string _phone = "";
    public string Phone
    {
        get => _phone;
        set => SetProperty(ref _phone, value);
    }

    private string _fax = "";
    public string Fax
    {
        get => _fax;
        set => SetProperty(ref _fax, value);
    }

    private string _invoiceRegistrationNo = "";
    public string InvoiceRegistrationNo
    {
        get => _invoiceRegistrationNo;
        set => SetProperty(ref _invoiceRegistrationNo, value);
    }

    private string _bankAccountNumber1 = "";
    public string BankAccountNumber1
    {
        get => _bankAccountNumber1;
        set => SetProperty(ref _bankAccountNumber1, value);
    }

    private string _bankAccountNumber2 = "";
    public string BankAccountNumber2
    {
        get => _bankAccountNumber2;
        set => SetProperty(ref _bankAccountNumber2, value);
    }

    private string _bankAccountNumber3 = "";
    public string BankAccountNumber3
    {
        get => _bankAccountNumber3;
        set => SetProperty(ref _bankAccountNumber3, value);
    }

    private string _statusMessage = "";
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public DelegateCommand SaveCommand { get; }

    public CompanyInfoSettingsViewModel()
    {
        SaveCommand = new DelegateCommand(async () => await OnSaveAsync());
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        var info = await _repo.GetAsync();
        Name                  = info.Name;
        Address               = info.Address;
        Phone                 = info.Phone;
        Fax                   = info.Fax;
        InvoiceRegistrationNo = info.InvoiceRegistrationNo;
        BankAccountNumber1    = info.BankAccountNumber1;
        BankAccountNumber2    = info.BankAccountNumber2;
        BankAccountNumber3    = info.BankAccountNumber3;
    }

    private async Task OnSaveAsync()
    {
        try
        {
            await _repo.UpsertAsync(new CompanyInfo
            {
                Name                  = Name,
                Address               = Address,
                Phone                 = Phone,
                Fax                   = Fax,
                InvoiceRegistrationNo = InvoiceRegistrationNo,
                BankAccountNumber1    = BankAccountNumber1,
                BankAccountNumber2    = BankAccountNumber2,
                BankAccountNumber3    = BankAccountNumber3,
            });
            StatusMessage = "保存しました。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"エラー: {ex.Message}";
        }
    }
}
