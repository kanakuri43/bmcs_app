using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using bmcs_app.Application.Master;
using bmcs_app.Domain.Entities;
using bmcs_app.Services;
using bmcs_app.ViewModels.Common;
using bmcs_app.Views.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bmcs_app.ViewModels.Master;

/// <summary>
/// 仕入先／商品／原価マスタ画面。仕入先を検索モーダルで選ぶと、有効な商品の全件を一覧に並べる。
/// 原価だけを一覧上で編集し、「登録」で変更した行をまとめて保存する。
/// </summary>
public partial class ProductSupplierCostMasterViewModel(
    ProductSupplierCostService costService,
    SupplierService supplierService,
    WindowService windowService)
    : ViewModelBase
{
    private ICollectionView? _rowsView;

    /// <summary>一覧に読み込み済みの仕入先コード。コード欄は入力途中の文字列を持つため、登録はこちらを使う。</summary>
    private string _loadedSupplierCode = string.Empty;

    [ObservableProperty]
    public partial string SupplierCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SupplierName { get; set; } = string.Empty;

    /// <summary>絞り込み（商品コードまたは商品名の部分一致）。絞り込んでも、隠れた行の入力内容は残る。</summary>
    [ObservableProperty]
    public partial string FilterText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    public ObservableCollection<ProductSupplierCostRow> Rows { get; } = [];

    /// <summary>一覧の表示用ビュー。絞り込みはここで行い、<see cref="Rows"/> 自体は変えない。</summary>
    public ICollectionView RowsView => _rowsView ??= CreateRowsView();

    /// <summary>登録していない変更があるか。</summary>
    public bool HasChanges => Rows.Any(r => r.IsChanged);

    partial void OnFilterTextChanged(string value) => RowsView.Refresh();

    private ICollectionView CreateRowsView()
    {
        var view = CollectionViewSource.GetDefaultView(Rows);
        view.Filter = MatchesFilter;
        return view;
    }

    private bool MatchesFilter(object item)
    {
        var filter = FilterText.Trim();
        return filter.Length == 0
            || (item is ProductSupplierCostRow row
                && (row.ProductCode.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || row.ProductName.Contains(filter, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// 登録していない変更があれば、破棄してよいかを確認する。仕入先を変える前と、画面を閉じる前に呼ぶ。
    /// </summary>
    /// <returns>続行してよければ <c>true</c>。</returns>
    public bool ConfirmDiscardChanges()
    {
        if (!HasChanges)
        {
            return true;
        }

        return MessageBox.Show(
            "登録していない変更があります。破棄してよろしいですか？",
            "bmcs_app", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    /// <summary>仕入先欄で Space を押したとき（または「...」ボタン）に、仕入先検索モーダルを開いて一覧を読み込む。</summary>
    [RelayCommand]
    private Task SelectSupplierAsync() => RunBusyAsync(async () =>
    {
        if (!ConfirmDiscardChanges())
        {
            return;
        }

        var supplier = windowService.ShowDialog<SupplierMasterSearchDialog, SupplierMasterSearchDialogViewModel, Supplier>();
        if (supplier is null)
        {
            return;
        }

        await LoadAsync(supplier);
        RequestFocus("Filter");
    });

    /// <summary>仕入先欄で Enter を押したとき、入力済みのコードの仕入先を読み込む。</summary>
    [RelayCommand]
    private Task LookupSupplierByCodeAsync() => RunBusyAsync(async () =>
    {
        var code = SupplierCode.Trim();
        if (code.Length == 0 || code == _loadedSupplierCode)
        {
            return;
        }

        var supplier = await supplierService.GetActiveByCodeAsync(code);
        if (supplier is null)
        {
            StatusMessage = $"仕入先コード「{code}」が見つからないか、無効です。";
            return;
        }

        if (!ConfirmDiscardChanges())
        {
            // 確認で中止したら、欄を表示中の仕入先のコードへ戻す。
            SupplierCode = _loadedSupplierCode;
            return;
        }

        await LoadAsync(supplier);
        RequestFocus("Filter");
    });

    private async Task LoadAsync(Supplier supplier)
    {
        var settings = await costService.GetSettingsAsync(supplier.SupplierCode);

        Rows.Clear();
        foreach (var setting in settings)
        {
            Rows.Add(new ProductSupplierCostRow(setting));
        }

        _loadedSupplierCode = supplier.SupplierCode;
        SupplierCode = supplier.SupplierCode;
        SupplierName = supplier.SupplierName;
        FilterText = string.Empty;
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private Task SaveAsync() => RunBusyAsync(async () =>
    {
        if (_loadedSupplierCode.Length == 0)
        {
            MessageBox.Show("仕入先を選択してください。", "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var changes = new List<ProductSupplierCostChange>();
        foreach (var row in Rows)
        {
            if (!row.IsChanged)
            {
                continue;
            }

            var text = row.CostText.Trim();
            decimal? costPrice = null;
            if (text.Length > 0)
            {
                if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed))
                {
                    MessageBox.Show(
                        $"商品「{row.ProductCode}」: 原価は数値で入力してください。",
                        "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                costPrice = parsed;
            }

            changes.Add(new ProductSupplierCostChange(row.ProductCode, costPrice, row.RowVersion));
        }

        if (changes.Count == 0)
        {
            StatusMessage = "変更はありません。";
            return;
        }

        try
        {
            await costService.SaveSettingsAsync(_loadedSupplierCode, changes);
        }
        catch (ProductSupplierCostValidationException ex)
        {
            MessageBox.Show(ex.Message, "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        catch (ProductSupplierCostConcurrencyException ex)
        {
            MessageBox.Show(
                $"{ex.Message}{Environment.NewLine}仕入先を選び直すと最新の内容を読み込みます（入力内容は破棄されます）。",
                "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 登録後は画面を起動直後の状態へ戻す（docs/product-spec.md UI/UX節「登録後のリセット」）。
        var message = $"仕入先 {_loadedSupplierCode} の原価を登録しました（{changes.Count} 件）。";
        ClearForm();
        StatusMessage = message;
        NotifyResetToInitialState();
    });

    private void ClearForm()
    {
        Rows.Clear();
        _loadedSupplierCode = string.Empty;
        SupplierCode = string.Empty;
        SupplierName = string.Empty;
        FilterText = string.Empty;
        StatusMessage = string.Empty;
    }
}
