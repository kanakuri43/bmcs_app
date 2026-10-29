using System.Collections.ObjectModel;
using bmcs_app.Application.Analysis;
using bmcs_app.ViewModels.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bmcs_app.ViewModels.Analysis;

/// <summary>
/// 担当者別売上・粗利画面。年（4桁入力）と月を選ぶと、その暦月の売上を担当者別に都度集計して表示する
/// （<see cref="EmployeeSalesQueryService"/>）。月次締めの確定有無に関係なく、任意の月を表示できる。
/// </summary>
public partial class EmployeeSalesViewModel(EmployeeSalesQueryService employeeSalesQueryService) : ViewModelBase
{
    [ObservableProperty]
    public partial string YearText { get; set; } = DateTime.Today.Year.ToString();

    public IReadOnlyList<int> MonthOptions { get; } = Enumerable.Range(1, 12).ToList();

    [ObservableProperty]
    public partial int SelectedMonth { get; set; } = DateTime.Today.Month;

    public ObservableCollection<EmployeeSalesItem> Results { get; } = [];

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

    partial void OnYearTextChanged(string value) => _ = RefreshAsync();

    partial void OnSelectedMonthChanged(int value) => _ = RefreshAsync();

    private bool _refreshRequested;

    /// <summary>一覧の再取得。実行中に来た要求は終了後に最新の条件で取り直す（<c>MonthlyClosingViewModel</c>と同じ）。</summary>
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
            var results = await employeeSalesQueryService.GetByMonthAsync(year, SelectedMonth);

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
                : $"{year}年{SelectedMonth:00}月の売上を{results.Count}名分表示しています。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"取得エラー: {ex.Message}";
        }
    }
}
