using System.Collections.ObjectModel;
using System.Windows;
using bmcs_app.Application.Master;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bmcs_app.ViewModels.Master;

/// <summary>商品マスタ画面。一覧（左）と詳細フォーム（右）を持つ。</summary>
public partial class ProductMasterViewModel(ProductService productService) : ViewModelBase
{
    public ObservableCollection<ProductListItem> Products { get; } = [];

    [ObservableProperty]
    public partial string ProductCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ProductName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ProductNameKana { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Specification { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string UnitName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial decimal StandardUnitPriceExclTax { get; set; }

    [ObservableProperty]
    public partial decimal StandardUnitPriceInclTax { get; set; }

    [ObservableProperty]
    public partial decimal StandardCostPrice { get; set; }

    [ObservableProperty]
    public partial TaxCategory TaxCategory { get; set; } = TaxCategory.Standard;

    /// <summary>税種別区分の選択肢。</summary>
    public static IReadOnlyList<TaxCategory> TaxCategoryOptions { get; } = Enum.GetValues<TaxCategory>();

    [ObservableProperty]
    public partial bool IsNew { get; set; } = true;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    private byte[]? _loadedRowVersion;

    [RelayCommand]
    private Task LoadProductsAsync() => RunBusyAsync(LoadProductsCoreAsync);

    /// <summary>
    /// 一覧の再読み込み本体。<see cref="RunBusyAsync"/> でラップしない。
    /// Save/Deactivate から呼ぶ際は既に IsBusy=true になっており、二重にラップすると
    /// RunBusyAsync の多重実行抑止（if (IsBusy) return;）に引っかかって何もしないまま返ってしまう。
    /// </summary>
    private async Task LoadProductsCoreAsync()
    {
        var products = await productService.GetProductsAsync();

        Products.Clear();
        foreach (var product in products)
        {
            Products.Add(ToListItem(product));
        }
    }

    [RelayCommand]
    private void NewProduct()
    {
        ClearForm();
        IsNew = true;
        StatusMessage = string.Empty;
    }

    /// <summary>一覧で選択した商品をフォームへ読み込む（RowActivationBehavior から Enter で呼ばれる）。</summary>
    [RelayCommand]
    private Task LoadSelectedProductAsync(object? item) => RunBusyAsync(async () =>
    {
        if (item is not ProductListItem listItem)
        {
            return;
        }

        var product = await productService.GetByCodeAsync(listItem.ProductCode);
        if (product is null)
        {
            StatusMessage = "商品が見つかりませんでした。再読み込みしてください。";
            return;
        }

        ProductCode = product.ProductCode;
        ProductName = product.ProductName;
        ProductNameKana = product.ProductNameKana ?? string.Empty;
        Specification = product.Specification ?? string.Empty;
        UnitName = product.UnitName ?? string.Empty;
        StandardUnitPriceExclTax = product.StandardUnitPriceExclTax;
        StandardUnitPriceInclTax = product.StandardUnitPriceInclTax;
        StandardCostPrice = product.StandardCostPrice;
        TaxCategory = product.TaxCategory;

        _loadedRowVersion = product.RowVersion;
        IsNew = false;
        StatusMessage = string.Empty;
    });

    [RelayCommand]
    private Task SaveAsync() => RunBusyAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(ProductCode) || string.IsNullOrWhiteSpace(ProductName))
        {
            MessageBox.Show("商品コードと商品名は必須です。", "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (ProductCode.Length > 20)
        {
            MessageBox.Show("商品コードは20文字以内で入力してください。", "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var product = new Product
        {
            ProductCode = ProductCode,
            ProductName = ProductName,
            ProductNameKana = NullIfEmpty(ProductNameKana),
            Specification = NullIfEmpty(Specification),
            UnitName = NullIfEmpty(UnitName),
            StandardUnitPriceExclTax = StandardUnitPriceExclTax,
            StandardUnitPriceInclTax = StandardUnitPriceInclTax,
            StandardCostPrice = StandardCostPrice,
            TaxCategory = TaxCategory,
            RowVersion = _loadedRowVersion,
            CreatedBy = string.Empty,
            CreatedAt = default,
            UpdatedBy = string.Empty,
            UpdatedAt = default,
        };

        try
        {
            var saved = IsNew
                ? await productService.CreateAsync(product)
                : await productService.UpdateAsync(product);

            _loadedRowVersion = saved.RowVersion;
            IsNew = false;
            StatusMessage = $"{saved.ProductCode} を保存しました。";
        }
        catch (ProductValidationException ex)
        {
            MessageBox.Show(ex.Message, "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        catch (ProductConcurrencyException ex)
        {
            MessageBox.Show(ex.Message, "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await LoadProductsCoreAsync();
    });

    [RelayCommand]
    private Task DeactivateAsync() => RunBusyAsync(async () =>
    {
        if (IsNew)
        {
            return;
        }

        var confirm = MessageBox.Show(
            $"{ProductCode} を無効化しますか？",
            "bmcs_app",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        var product = new Product
        {
            ProductCode = ProductCode,
            ProductName = ProductName,
            StandardUnitPriceExclTax = StandardUnitPriceExclTax,
            StandardUnitPriceInclTax = StandardUnitPriceInclTax,
            StandardCostPrice = StandardCostPrice,
            TaxCategory = TaxCategory,
            RowVersion = _loadedRowVersion,
            CreatedBy = string.Empty,
            CreatedAt = default,
            UpdatedBy = string.Empty,
            UpdatedAt = default,
        };

        try
        {
            await productService.DeactivateAsync(product);
        }
        catch (ProductConcurrencyException ex)
        {
            MessageBox.Show(ex.Message, "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        catch (ProductValidationException ex)
        {
            MessageBox.Show(ex.Message, "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ClearForm();
        IsNew = true;
        StatusMessage = $"{product.ProductCode} を無効化しました。";
        await LoadProductsCoreAsync();
    });

    private void ClearForm()
    {
        ProductCode = string.Empty;
        ProductName = string.Empty;
        ProductNameKana = string.Empty;
        Specification = string.Empty;
        UnitName = string.Empty;
        StandardUnitPriceExclTax = 0;
        StandardUnitPriceInclTax = 0;
        StandardCostPrice = 0;
        TaxCategory = TaxCategory.Standard;
        _loadedRowVersion = null;
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static ProductListItem ToListItem(Product product) => new(
        product.ProductCode,
        product.ProductName,
        product.ProductNameKana,
        product.UnitName,
        TaxCategoryDisplay: product.TaxCategory switch
        {
            TaxCategory.Standard => "課税10%",
            TaxCategory.Reduced => "軽減8%",
            TaxCategory.TaxExempt => "非課税",
            _ => product.TaxCategory.ToString(),
        });
}
