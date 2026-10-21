using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
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
/// 得意先／商品／単価・掛率マスタ画面。得意先を検索モーダルで選ぶと、有効な商品の全件を一覧に並べる。
/// 単価と掛率だけを一覧上で編集し、「登録」で変更した行をまとめて保存する。
/// </summary>
public partial class CustomerProductPriceMasterViewModel(
    CustomerProductPriceService priceService,
    WindowService windowService)
    : ViewModelBase
{
    private ICollectionView? _rowsView;

    [ObservableProperty]
    public partial string CustomerCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CustomerName { get; set; } = string.Empty;

    /// <summary>単価の入力基準（得意先の税区分で決まる「税込」／「税抜」）。得意先を選ぶまでは空。</summary>
    [ObservableProperty]
    public partial string TaxLabel { get; set; } = string.Empty;

    /// <summary>絞り込み（商品コードまたは商品名の部分一致）。絞り込んでも、隠れた行の入力内容は残る。</summary>
    [ObservableProperty]
    public partial string FilterText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    public ObservableCollection<CustomerProductSettingRow> Rows { get; } = [];

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
            || (item is CustomerProductSettingRow row
                && (row.ProductCode.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || row.ProductName.Contains(filter, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// 登録していない変更があれば、破棄してよいかを確認する。得意先を変える前と、画面を閉じる前に呼ぶ。
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

    /// <summary>得意先欄で Space／Enter を押したとき（または選択ボタン）に、得意先検索モーダルを開いて一覧を読み込む。</summary>
    [RelayCommand]
    private Task SelectCustomerAsync() => RunBusyAsync(async () =>
    {
        if (!ConfirmDiscardChanges())
        {
            return;
        }

        var customer = windowService.ShowDialog<CustomerSearchDialog, CustomerSearchDialogViewModel, Customer>();
        if (customer is null)
        {
            return;
        }

        await LoadAsync(customer);
    });

    private async Task LoadAsync(Customer customer)
    {
        var taxLabel = customer.TaxUnit == TaxUnit.Line ? "税込" : "税抜";
        var settings = await priceService.GetSettingsAsync(customer.CustomerCode);

        Rows.Clear();
        foreach (var setting in settings)
        {
            Rows.Add(new CustomerProductSettingRow(setting, taxLabel));
        }

        CustomerCode = customer.CustomerCode;
        CustomerName = customer.CustomerName;
        TaxLabel = taxLabel;
        FilterText = string.Empty;
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private Task SaveAsync() => RunBusyAsync(async () =>
    {
        if (CustomerCode.Length == 0)
        {
            MessageBox.Show("得意先を選択してください。", "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var priceChanges = new List<PriceChange>();
        var rateChanges = new List<RateChange>();
        foreach (var row in Rows)
        {
            if (row.IsPriceChanged)
            {
                var text = row.PriceText.Trim();
                decimal? unitPrice = null;
                if (text.Length > 0)
                {
                    if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed))
                    {
                        ShowInvalid(row, "単価は数値で入力してください。");
                        return;
                    }

                    unitPrice = parsed;
                }

                priceChanges.Add(new PriceChange(row.ProductCode, unitPrice, row.PriceRowVersion));
            }

            if (row.IsRateChanged)
            {
                var text = row.RateText.Trim();
                int? rate = null;
                if (text.Length > 0)
                {
                    if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var parsed))
                    {
                        ShowInvalid(row, "定価掛率は 0〜100 の整数で入力してください。");
                        return;
                    }

                    rate = parsed;
                }

                rateChanges.Add(new RateChange(row.ProductCode, rate, row.RateRowVersion));
            }
        }

        if (priceChanges.Count == 0 && rateChanges.Count == 0)
        {
            StatusMessage = "変更はありません。";
            return;
        }

        try
        {
            await priceService.SaveSettingsAsync(CustomerCode, priceChanges, rateChanges);
        }
        catch (CustomerProductPriceValidationException ex)
        {
            MessageBox.Show(ex.Message, "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        catch (CustomerProductPriceConcurrencyException ex)
        {
            MessageBox.Show(
                $"{ex.Message}{Environment.NewLine}得意先を選び直すと最新の内容を読み込みます（入力内容は破棄されます）。",
                "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 登録後は画面を起動直後の状態へ戻す（docs/product-spec.md UI/UX節「登録後のリセット」）。
        var message = $"得意先 {CustomerCode} の単価・掛率を登録しました（単価 {priceChanges.Count} 件・掛率 {rateChanges.Count} 件）。";
        ClearForm();
        StatusMessage = message;
        NotifyResetToInitialState();
    });

    private static void ShowInvalid(CustomerProductSettingRow row, string message)
        => MessageBox.Show(
            $"商品「{row.ProductCode}」: {message}", "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);

    private void ClearForm()
    {
        Rows.Clear();
        CustomerCode = string.Empty;
        CustomerName = string.Empty;
        TaxLabel = string.Empty;
        FilterText = string.Empty;
        StatusMessage = string.Empty;
    }
}
