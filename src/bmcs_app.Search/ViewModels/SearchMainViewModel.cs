using System.Collections.ObjectModel;
using Prism.Commands;
using Prism.Mvvm;
using bmcs_app.Core.Interfaces;
using bmcs_app.Core.Models;

namespace bmcs_app.Search.ViewModels;

public class SearchMainViewModel : BindableBase
{
    private readonly ISearchRepository _repo;

    // ===== 種別 =====

    private bool _slipTypeBoth = true;
    public bool SlipTypeBoth
    {
        get => _slipTypeBoth;
        set => SetProperty(ref _slipTypeBoth, value);
    }

    private bool _slipTypeSalesOnly;
    public bool SlipTypeSalesOnly
    {
        get => _slipTypeSalesOnly;
        set => SetProperty(ref _slipTypeSalesOnly, value);
    }

    private bool _slipTypeReceiptsOnly;
    public bool SlipTypeReceiptsOnly
    {
        get => _slipTypeReceiptsOnly;
        set => SetProperty(ref _slipTypeReceiptsOnly, value);
    }

    private bool IncludeSales    => SlipTypeBoth || SlipTypeSalesOnly;
    private bool IncludeReceipts => SlipTypeBoth || SlipTypeReceiptsOnly;

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

    // ===== 得意先コード =====

    private string _customerCode = string.Empty;
    public string CustomerCode
    {
        get => _customerCode;
        set => SetProperty(ref _customerCode, value);
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

    // ===== ステータス =====

    private string _statusMessage = "条件を指定して検索してください。";
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    // ===== コマンド =====

    public DelegateCommand SearchCommand { get; }

    public SearchMainViewModel(ISearchRepository repo)
    {
        _repo         = repo;
        SearchCommand = new DelegateCommand(async () => await OnSearchAsync());
    }

    private async Task OnSearchAsync()
    {
        if (!IncludeSales && !IncludeReceipts)
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
                IncludeSales, IncludeReceipts,
                dateFrom, dateTo,
                string.IsNullOrWhiteSpace(Keyword)      ? null : Keyword,
                string.IsNullOrWhiteSpace(CustomerCode) ? null : CustomerCode,
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
