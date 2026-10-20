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
/// 得意先／商品／定価掛率マスタ画面。一覧は持たず、得意先コードと商品コードを直接入力するか、各欄で Space を押して
/// 検索モーダルから選ぶ。2つのコードが確定すると、登録済みの定価掛率を読み込む。
/// </summary>
public partial class CustomerProductRateMasterViewModel(
    CustomerProductRateService rateService,
    CustomerService customerService,
    ProductService productService,
    WindowService windowService)
    : ViewModelBase
{
    [ObservableProperty]
    public partial string CustomerCode { get; set; } = string.Empty;

    /// <summary>得意先コードに対応する得意先名（照会結果の表示用）。</summary>
    [ObservableProperty]
    public partial string CustomerName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ProductCode { get; set; } = string.Empty;

    /// <summary>商品コードに対応する商品名（照会結果の表示用）。</summary>
    [ObservableProperty]
    public partial string ProductName { get; set; } = string.Empty;

    /// <summary>定価掛率（パーセント）の入力文字列。整数のみ受け付けるため、保存時に検証する。</summary>
    [ObservableProperty]
    public partial string RateText { get; set; } = string.Empty;

    /// <summary>商品の定価の案内。定価が0の商品は、掛率を登録しても単価が0になるため注意を促す。</summary>
    [ObservableProperty]
    public partial string CatalogPriceNote { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsNew { get; set; } = true;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    private byte[]? _loadedRowVersion;

    /// <summary>照会で有効な得意先・商品と確認できたときだけ設定する（<c>null</c> は未確定・未登録・無効）。</summary>
    private Customer? _customer;

    private Product? _product;

    // コードを書き換えたら、照会済みの名称・読み込み済みの行との対応は切れる（Enter で読み直すまで新規扱い）。
    // 検索モーダルからの設定は、コード代入のあとに ApplyCustomer／ApplyProduct を呼ぶので影響しない。
    partial void OnCustomerCodeChanged(string value)
    {
        _customer = null;
        CustomerName = string.Empty;
        ResetLoadedRow();
    }

    partial void OnProductCodeChanged(string value)
    {
        _product = null;
        ProductName = string.Empty;
        CatalogPriceNote = string.Empty;
        ResetLoadedRow();
    }

    private void ResetLoadedRow()
    {
        _loadedRowVersion = null;
        IsNew = true;
    }

    [RelayCommand]
    private void NewRate()
    {
        ClearForm();
        StatusMessage = string.Empty;
    }

    /// <summary>得意先コード欄で Space を押したときに得意先検索モーダルを開く。</summary>
    [RelayCommand]
    private Task OpenCustomerSearchAsync() => RunBusyAsync(async () =>
    {
        var customer = windowService.ShowDialog<CustomerSearchDialog, CustomerSearchDialogViewModel, Customer>();
        if (customer is null)
        {
            return;
        }

        CustomerCode = customer.CustomerCode;
        ApplyCustomer(customer);
        await LoadRegisteredRateAsync();
        RequestFocus("Product");
    });

    /// <summary>得意先コード欄で Enter を押したときに、入力済みコードの得意先名を照会する。</summary>
    [RelayCommand]
    private Task LookupCustomerByCodeAsync() => RunBusyAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(CustomerCode))
        {
            return;
        }

        ApplyCustomer(await customerService.GetByCodeAsync(CustomerCode));
        await LoadRegisteredRateAsync();
        if (_customer is not null)
        {
            RequestFocus("Product");
        }
    });

    /// <summary>商品コード欄で Space を押したときに商品検索モーダルを開く。</summary>
    [RelayCommand]
    private Task OpenProductSearchAsync() => RunBusyAsync(async () =>
    {
        var product = windowService.ShowDialog<ProductMasterSearchDialog, ProductMasterSearchDialogViewModel, Product>();
        if (product is null)
        {
            return;
        }

        ProductCode = product.ProductCode;
        ApplyProduct(product);
        await LoadRegisteredRateAsync();
        RequestFocus("Rate");
    });

    /// <summary>商品コード欄で Enter を押したときに、入力済みコードの商品名を照会する。</summary>
    [RelayCommand]
    private Task LookupProductByCodeAsync() => RunBusyAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(ProductCode))
        {
            return;
        }

        ApplyProduct(await productService.GetByCodeAsync(ProductCode));
        await LoadRegisteredRateAsync();
        if (_product is not null)
        {
            RequestFocus("Rate");
        }
    });

    private void ApplyCustomer(Customer? customer)
    {
        _customer = customer is { IsDeleted: false } ? customer : null;
        CustomerName = customer is null ? "（該当なし）" : customer.IsDeleted ? "（無効）" : customer.CustomerName;
    }

    private void ApplyProduct(Product? product)
    {
        _product = product is { IsDeleted: false } ? product : null;
        ProductName = product is null ? "（該当なし）" : product.IsDeleted ? "（無効）" : product.ProductName;
        CatalogPriceNote = _product is null
            ? string.Empty
            : _product.CatalogPrice == 0m
                ? "定価が未設定（0）のため、掛率を登録しても単価は0になります。"
                : $"定価（税抜）: {_product.CatalogPrice:N0}";
    }

    /// <summary>得意先・商品が両方確定していれば、登録済みの定価掛率を読み込む。</summary>
    private async Task LoadRegisteredRateAsync()
    {
        if (_customer is null || _product is null)
        {
            return;
        }

        var rate = await rateService.GetAsync(_customer.CustomerCode, _product.ProductCode);
        if (rate is null)
        {
            StatusMessage = "未登録の組み合わせです。新規登録として定価掛率を入力してください。";
            return;
        }

        if (rate.IsDeleted)
        {
            // 無効化済みの行は、保存すると有効に戻る（既存の掛率は引き継がない）。
            StatusMessage = "この組み合わせは無効化されています。保存すると有効に戻ります。";
            return;
        }

        RateText = rate.CatalogPriceRate.ToString();
        _loadedRowVersion = rate.RowVersion;
        IsNew = false;
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private Task SaveAsync() => RunBusyAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(CustomerCode) || string.IsNullOrWhiteSpace(ProductCode))
        {
            MessageBox.Show("得意先コード・商品コードは必須です。", "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(RateText, out var rateValue) || rateValue < 0 || rateValue > 100)
        {
            MessageBox.Show("定価掛率は 0〜100 の整数で入力してください。", "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var rate = BuildEntity(rateValue);
        try
        {
            if (IsNew)
            {
                await rateService.CreateAsync(rate);
            }
            else
            {
                await rateService.UpdateAsync(rate);
            }

            // 登録後は画面を起動直後の状態へ戻す（docs/product-spec.md UI/UX節「登録後のリセット」）。
            var message = $"得意先 {rate.CustomerCode}・商品 {rate.ProductCode} の定価掛率を保存しました。";
            ClearForm();
            StatusMessage = message;
            NotifyResetToInitialState();
        }
        catch (Exception ex) when (ex is CustomerProductRateValidationException or CustomerProductRateConcurrencyException)
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

        var confirm = MessageBox.Show(
            $"得意先 {CustomerCode}・商品 {ProductCode} の定価掛率を無効化しますか？",
            "bmcs_app", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        // 無効化は掛率を使わないので、入力文字列の検証は行わない。
        var rate = BuildEntity(0);
        try
        {
            await rateService.DeactivateAsync(rate);
        }
        catch (Exception ex) when (ex is CustomerProductRateValidationException or CustomerProductRateConcurrencyException)
        {
            MessageBox.Show(ex.Message, "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ClearForm();
        StatusMessage = $"得意先 {rate.CustomerCode}・商品 {rate.ProductCode} の定価掛率を無効化しました。";
    });

    private CustomerProductRate BuildEntity(int catalogPriceRate) => new()
    {
        CustomerCode = CustomerCode,
        ProductCode = ProductCode,
        CatalogPriceRate = catalogPriceRate,
        RowVersion = _loadedRowVersion,
        CreatedBy = string.Empty,
        CreatedAt = default,
        UpdatedBy = string.Empty,
        UpdatedAt = default,
    };

    private void ClearForm()
    {
        CustomerCode = string.Empty;
        CustomerName = string.Empty;
        ProductCode = string.Empty;
        ProductName = string.Empty;
        RateText = string.Empty;
        CatalogPriceNote = string.Empty;
        _customer = null;
        _product = null;
        _loadedRowVersion = null;
        IsNew = true;
    }
}
