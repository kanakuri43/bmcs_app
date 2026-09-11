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
/// 商品マスタ画面。一覧は持たず、商品コードを直接入力するか、
/// コード欄で Space を押して検索モーダル（<see cref="ProductMasterSearchDialog"/>）を呼び出して対象を選ぶ
/// （得意先マスタ画面と同じ Space検索／Enter読込のパターンに揃える）。
/// </summary>
public partial class ProductMasterViewModel(ProductService productService, WindowService windowService) : ViewModelBase
{
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
    private void NewProduct()
    {
        ClearForm();
        IsNew = true;
        StatusMessage = string.Empty;
    }

    /// <summary>コード欄で Space を押したときに検索モーダルを開く。</summary>
    [RelayCommand]
    private void OpenProductSearch()
    {
        var product = windowService.ShowDialog<ProductMasterSearchDialog, ProductMasterSearchDialogViewModel, Product>();
        if (product is not null)
        {
            ApplyProduct(product);
        }
    }

    /// <summary>コード欄で Enter を押したときに、入力済みコードで直接読み込む。</summary>
    [RelayCommand]
    private Task LookupProductByCodeAsync() => RunBusyAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(ProductCode))
        {
            return;
        }

        var product = await productService.GetByCodeAsync(ProductCode);
        if (product is null)
        {
            var enteredCode = ProductCode;
            ClearForm();
            ProductCode = enteredCode;
            IsNew = true;
            StatusMessage = $"商品コード「{enteredCode}」は未登録です。新規登録として入力してください。";
            return;
        }

        ApplyProduct(product);
    });

    private void ApplyProduct(Product product)
    {
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
    }

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

            // 登録後は画面を起動直後の状態へ戻す（docs/product-spec.md UI/UX節「登録後のリセット」）。
            var savedProductCode = saved.ProductCode;
            ClearForm();
            IsNew = true;
            StatusMessage = $"{savedProductCode} を保存しました。";
            NotifyResetToInitialState();
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
}
