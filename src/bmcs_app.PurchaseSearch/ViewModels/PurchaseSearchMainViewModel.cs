using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Prism.Commands;
using Prism.Mvvm;
using bmcs_app.Core.Interfaces;
using bmcs_app.Core.Models;

namespace bmcs_app.PurchaseSearch.ViewModels;

public class PurchaseSearchMainViewModel : BindableBase
{
    private readonly IPurchaseSearchRepository _repo;

    // ===== 種別 =====

    private bool _includePurchaseOrders = true;
    public bool IncludePurchaseOrders
    {
        get => _includePurchaseOrders;
        set => SetProperty(ref _includePurchaseOrders, value);
    }

    private bool _includePurchases = true;
    public bool IncludePurchases
    {
        get => _includePurchases;
        set => SetProperty(ref _includePurchases, value);
    }

    private bool _includePayments = true;
    public bool IncludePayments
    {
        get => _includePayments;
        set => SetProperty(ref _includePayments, value);
    }

    // ===== 日付 =====

    private DateTime? _dateFrom = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    public DateTime? DateFrom
    {
        get => _dateFrom;
        set => SetProperty(ref _dateFrom, value);
    }

    private DateTime? _dateTo = DateTime.Today;
    public DateTime? DateTo
    {
        get => _dateTo;
        set => SetProperty(ref _dateTo, value);
    }

    // ===== キーワード =====

    private string _keyword = string.Empty;
    public string Keyword
    {
        get => _keyword;
        set => SetProperty(ref _keyword, value);
    }

    // ===== 仕入先コード =====

    private string _supplierCode = string.Empty;
    public string SupplierCode
    {
        get => _supplierCode;
        set => SetProperty(ref _supplierCode, value);
    }

    // ===== 集計状態 =====

    public List<string> AggregationStatuses { get; } = ["全件", "未処理のみ", "処理済のみ"];

    private string _selectedAggregationStatus = "全件";
    public string SelectedAggregationStatus
    {
        get => _selectedAggregationStatus;
        set => SetProperty(ref _selectedAggregationStatus, value);
    }

    // ===== 結果 =====

    public ObservableCollection<SearchResultItem> Results { get; } = new();

    private SearchResultItem? _selectedResult;
    public SearchResultItem? SelectedResult
    {
        get => _selectedResult;
        set
        {
            SetProperty(ref _selectedResult, value);
            OpenSlipCommand.RaiseCanExecuteChanged();
        }
    }

    // ===== ステータス =====

    private string _statusMessage = "条件を指定して検索してください。";
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    // ===== コマンド =====

    public DelegateCommand SearchCommand { get; }
    public DelegateCommand OpenSlipCommand { get; }

    public PurchaseSearchMainViewModel(IPurchaseSearchRepository repo)
    {
        _repo           = repo;
        SearchCommand   = new DelegateCommand(async () => await OnSearchAsync());
        OpenSlipCommand = new DelegateCommand(OnOpenSlip, () => SelectedResult is not null);
    }

    private void OnOpenSlip()
    {
        if (SelectedResult is null) return;

        var exeName = SelectedResult.SlipType switch
        {
            "発注" => "bmcs_app.PurchaseOrder.exe",
            "仕入" => "bmcs_app.Purchase.exe",
            "支払" => "bmcs_app.Payment.exe",
            _      => null,
        };

        if (exeName is null) return;

        var dir  = AppDomain.CurrentDomain.BaseDirectory;
        var path = Path.Combine(dir, exeName);

        if (!File.Exists(path))
        {
            MessageBox.Show($"{exeName} が見つかりません。\n\n{path}",
                            "起動エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Process.Start(new ProcessStartInfo(path)
        {
            UseShellExecute = true,
            Arguments       = $"--slip-no={SelectedResult.SlipNo}",
        });
    }

    private async Task OnSearchAsync()
    {
        if (!IncludePurchaseOrders && !IncludePurchases && !IncludePayments)
        {
            StatusMessage = "種別を1つ以上選択してください。";
            return;
        }

        try
        {
            StatusMessage = "検索中...";

            var dateFrom = DateFrom.HasValue ? DateOnly.FromDateTime(DateFrom.Value) : (DateOnly?)null;
            var dateTo   = DateTo.HasValue   ? DateOnly.FromDateTime(DateTo.Value)   : (DateOnly?)null;

            var statusCode = SelectedAggregationStatus switch
            {
                "未処理のみ" => "unprocessed",
                "処理済のみ" => "processed",
                _           => "all",
            };

            var results = await _repo.SearchAsync(
                IncludePurchaseOrders, IncludePurchases, IncludePayments,
                dateFrom, dateTo,
                string.IsNullOrWhiteSpace(Keyword)      ? null : Keyword,
                string.IsNullOrWhiteSpace(SupplierCode) ? null : SupplierCode,
                statusCode);

            Results.Clear();
            foreach (var item in results)
                Results.Add(item);

            StatusMessage = Results.Count == 0
                ? "該当する伝票が見つかりませんでした。"
                : $"{Results.Count} 件";
        }
        catch (Exception ex)
        {
            StatusMessage = $"エラー: {ex.Message}";
        }
    }
}
