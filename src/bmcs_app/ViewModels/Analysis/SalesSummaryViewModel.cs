using System.Collections.ObjectModel;
using bmcs_app.Application.Analysis;
using bmcs_app.ViewModels.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bmcs_app.ViewModels.Analysis;

/// <summary>集計の軸。</summary>
public enum SalesSummaryKind
{
    Customer,
    Product,
}

/// <summary>
/// 得意先別・商品別売上・粗利画面（軸は <see cref="Kind"/> で切り替える。<c>WindowService.Show</c> の configure で設定する）。
/// 年（4桁入力）と月を選ぶと、その暦月の売上を都度集計して表示する（<see cref="SalesSummaryQueryService"/>）。
/// </summary>
public partial class SalesSummaryViewModel(SalesSummaryQueryService salesSummaryQueryService) : ViewModelBase
{
    public SalesSummaryKind Kind { get; set; }

    public string Title => Kind == SalesSummaryKind.Customer ? "得意先別売上・粗利" : "商品別売上・粗利";

    public string CodeHeader => Kind == SalesSummaryKind.Customer ? "得意先コード" : "商品コード";

    public string NameHeader => Kind == SalesSummaryKind.Customer ? "得意先名" : "商品名";

    private string UnitLabel => Kind == SalesSummaryKind.Customer ? "得意先" : "商品";

    [ObservableProperty]
    public partial string YearText { get; set; } = DateTime.Today.Year.ToString();

    public IReadOnlyList<int> MonthOptions { get; } = Enumerable.Range(1, 12).ToList();

    [ObservableProperty]
    public partial int SelectedMonth { get; set; } = DateTime.Today.Month;

    public ObservableCollection<SalesSummaryItem> Results { get; } = [];

    [ObservableProperty]
    public partial decimal TotalSalesAmount { get; set; }

    [ObservableProperty]
    public partial decimal TotalCostAmount { get; set; }

    [ObservableProperty]
    public partial decimal TotalGrossProfit { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [RelayCommand]
    private Task LoadAsync() => RefreshAsync();

    /// <summary>表示中の一覧を、表示されている並び順のまま CSV に保存する（<paramref name="displayedRows"/> は一覧の <c>Items</c>）。</summary>
    [RelayCommand]
    private void SaveCsv(System.Collections.IEnumerable displayedRows)
    {
        try
        {
            var path = AnalysisCsvExporter.Save(
                $"{Title}_{YearText}{SelectedMonth:00}.csv",
                [CodeHeader, NameHeader, "売上額", "原価", "粗利", "粗利率(%)"],
                displayedRows.Cast<SalesSummaryItem>().Select(r => new object?[] { r.Code, r.Name, r.SalesAmount, r.CostAmount, r.GrossProfit, r.GrossProfitRate }));
            if (path is not null)
            {
                StatusMessage = $"CSVファイルを保存しました: {path}";
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"CSVを保存できません。\n{ex.Message}", "分析", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    partial void OnYearTextChanged(string value) => _ = RefreshAsync();

    partial void OnSelectedMonthChanged(int value) => _ = RefreshAsync();

    private bool _refreshRequested;

    /// <summary>一覧の再取得。実行中に来た要求は終了後に最新の条件で取り直す（<c>EmployeeSalesViewModel</c>と同じ）。</summary>
    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            _refreshRequested = true;
            return;
        }

        await RunBusyAsync(async () =>
        {
            do
            {
                _refreshRequested = false;
                await RefreshListAsync();
            }
            while (_refreshRequested);
        });
    }

    private async Task RefreshListAsync()
    {
        if (YearText.Length != 4 || !int.TryParse(YearText, out var year) || year < 1900)
        {
            StatusMessage = "年は4桁で入力してください。";
            return;
        }

        try
        {
            var results = Kind == SalesSummaryKind.Customer
                ? await salesSummaryQueryService.GetByCustomerAsync(year, SelectedMonth)
                : await salesSummaryQueryService.GetByProductAsync(year, SelectedMonth);

            Results.Clear();
            foreach (var result in results)
            {
                Results.Add(result);
            }

            TotalSalesAmount = results.Sum(r => r.SalesAmount);
            TotalCostAmount = results.Sum(r => r.CostAmount);
            TotalGrossProfit = results.Sum(r => r.GrossProfit);
            StatusMessage = results.Count == 0
                ? $"{year}年{SelectedMonth:00}月の売上はありません。"
                : $"{year}年{SelectedMonth:00}月の売上を{results.Count}{UnitLabel}分表示しています。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"取得エラー: {ex.Message}";
        }
    }
}
