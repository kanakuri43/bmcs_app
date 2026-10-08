using System.Text.RegularExpressions;
using System.Windows;
using bmcs_app.Application.Master;
using bmcs_app.Domain.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bmcs_app.ViewModels.Master;

/// <summary>
/// 自社情報マスタ画面。1レコード運用のため、他マスタ画面（得意先・商品・社員）と違い
/// コード検索・新規登録・無効化を持たない。画面表示時に唯一の行を読み込み、保存のみを行う
/// （<see cref="Master.PrinterSettingsViewModel"/> と同じ「読み込み→編集→保存」の構成）。
/// </summary>
public partial class CompanyInfoSettingsViewModel(CompanyInfoService companyInfoService) : ViewModelBase
{
    [ObservableProperty]
    public partial string CompanyName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string InvoiceRegistrationNumber { get; set; } = string.Empty;

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
    public partial string RepresentativeName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    private byte[]? _loadedRowVersion;

    private static readonly Regex InvoiceRegistrationNumberPattern = new(@"^T\d{13}$", RegexOptions.Compiled);

    /// <summary>画面表示時に唯一の行を読み込む（Window の Loaded から呼ばれる）。</summary>
    [RelayCommand]
    private Task LoadAsync() => RunBusyAsync(async () =>
    {
        var companyInfo = await companyInfoService.GetAsync();
        if (companyInfo is null)
        {
            _loadedRowVersion = null;
            StatusMessage = "自社情報が未登録です。入力して保存してください。";
            return;
        }

        CompanyName = companyInfo.CompanyName;
        InvoiceRegistrationNumber = companyInfo.InvoiceRegistrationNumber;
        PostalCode = companyInfo.PostalCode ?? string.Empty;
        Address1 = companyInfo.Address1 ?? string.Empty;
        Address2 = companyInfo.Address2 ?? string.Empty;
        PhoneNumber = companyInfo.PhoneNumber ?? string.Empty;
        FaxNumber = companyInfo.FaxNumber ?? string.Empty;
        RepresentativeName = companyInfo.RepresentativeName ?? string.Empty;

        _loadedRowVersion = companyInfo.RowVersion;
        StatusMessage = string.Empty;
    });

    [RelayCommand]
    private Task SaveAsync() => RunBusyAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(CompanyName))
        {
            MessageBox.Show("会社名は必須です。", "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!InvoiceRegistrationNumberPattern.IsMatch(InvoiceRegistrationNumber))
        {
            MessageBox.Show("適格請求書発行事業者の登録番号は「T + 13桁の数字」の形式で入力してください（例: T1234567890123）。", "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var companyInfo = new CompanyInfo
        {
            CompanyInfoId = 1,
            CompanyName = CompanyName,
            InvoiceRegistrationNumber = InvoiceRegistrationNumber,
            PostalCode = NullIfEmpty(PostalCode),
            Address1 = NullIfEmpty(Address1),
            Address2 = NullIfEmpty(Address2),
            PhoneNumber = NullIfEmpty(PhoneNumber),
            FaxNumber = NullIfEmpty(FaxNumber),
            RepresentativeName = NullIfEmpty(RepresentativeName),
            RowVersion = _loadedRowVersion,
            CreatedBy = string.Empty,
            CreatedAt = default,
            UpdatedBy = string.Empty,
            UpdatedAt = default,
        };

        try
        {
            var saved = await companyInfoService.SaveAsync(companyInfo);
            _loadedRowVersion = saved.RowVersion;
            StatusMessage = "保存しました。";
        }
        catch (CompanyInfoConcurrencyException ex)
        {
            MessageBox.Show(ex.Message, "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    });

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
