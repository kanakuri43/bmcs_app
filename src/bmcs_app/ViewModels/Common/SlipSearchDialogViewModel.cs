using System.Collections.ObjectModel;
using bmcs_app.Application.Billing;
using bmcs_app.Application.Order;
using bmcs_app.Application.Receipt;
using bmcs_app.Application.Sales;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bmcs_app.ViewModels.Common;

/// <summary>
/// 伝票検索モーダル（TODO.md 5-3・5-5・5-6の共通前提）。受注No.検索・売上No.検索・明細請求書No.検索・
/// 入金No.検索すべてに使う（<see cref="CustomerSearchDialogViewModel"/> と同じ、全件ロード後に
/// メモリ絞り込みする作り）。選択された**伝票番号**だけを返す。伝票実体の読み込みは呼び出し元が
/// 自分のクエリサービスで行う。
/// </summary>
public partial class SlipSearchDialogViewModel(
    SalesQueryService salesQueryService,
    OrderQueryService orderQueryService,
    DetailInvoiceQueryService detailInvoiceQueryService,
    ReceiptQueryService receiptQueryService,
    DetailReceiptQueryService detailReceiptQueryService)
    : DialogViewModelBase<string>
{
    private List<SlipSearchItem> _allItems = [];

    /// <summary>検索対象。<c>ShowDialog</c> の <c>configure</c> コールバックで呼び出し元が設定する。</summary>
    public SlipSearchTarget Target { get; set; } = SlipSearchTarget.Sales;

    /// <summary>
    /// <see cref="SlipSearchTarget.Order"/> 専用。<c>true</c> のとき、売上完了・中止済みの受注も
    /// 検索結果に含める（既定は<c>false</c>＝これ以上売上化できない受注を除外する。
    /// 売上入力画面の受注No.検索が使う既定挙動）。受注入力画面（TODO.md 4-6）は、修正できない
    /// 受注も閲覧目的で探せるようにするため<c>true</c>を設定する（2026-09-16確定）。
    /// </summary>
    public bool IncludeUnavailableOrders { get; set; }

    public ObservableCollection<SlipSearchItem> Results { get; } = [];

    [ObservableProperty]
    public partial string SearchKeyword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int SelectedIndex { get; set; } = -1;

    [RelayCommand]
    private Task LoadAsync() => RunBusyAsync(async () =>
    {
        _allItems = Target switch
        {
            SlipSearchTarget.Sales => (await salesQueryService.SearchAsync(keyword: null))
                .Select(SlipSearchItem.FromSalesHit)
                .ToList(),
            SlipSearchTarget.Order => (await orderQueryService.SearchAsync(
                    keyword: null, excludeUnavailableForSales: !IncludeUnavailableOrders))
                .Select(SlipSearchItem.FromOrderHit)
                .ToList(),
            SlipSearchTarget.DetailInvoice => (await detailInvoiceQueryService.SearchAsync(keyword: null))
                .Select(SlipSearchItem.FromDetailInvoiceHit)
                .ToList(),
            SlipSearchTarget.Receipt => (await receiptQueryService.SearchAsync(keyword: null))
                .Select(SlipSearchItem.FromReceiptHit)
                .ToList(),
            SlipSearchTarget.DetailReceipt => (await detailReceiptQueryService.SearchAsync(keyword: null))
                .Select(SlipSearchItem.FromDetailReceiptHit)
                .ToList(),
            _ => throw new ArgumentOutOfRangeException(nameof(Target), Target, null),
        };

        ApplyFilter();
    });

    partial void OnSearchKeywordChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var keywords = SearchKeyword
            .Split(' ', '　')
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .ToArray();

        var filtered = keywords.Length == 0
            ? _allItems
            : _allItems.Where(i => keywords.All(k => Matches(i, k)));

        Results.Clear();
        foreach (var item in filtered)
        {
            Results.Add(item);
        }

        SelectedIndex = Results.Count > 0 ? 0 : -1;
    }

    private static bool Matches(SlipSearchItem item, string keyword) =>
        Contains(item.SlipNumber, keyword)
        || Contains(item.CustomerCode, keyword)
        || Contains(item.CustomerName, keyword);

    private static bool Contains(string? source, string keyword) =>
        source is not null && source.Contains(keyword, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>一覧で選択した伝票番号を確定する（RowActivationBehavior から Enter で呼ばれる）。</summary>
    [RelayCommand]
    private void Confirm(object? item)
    {
        if (item is SlipSearchItem searchItem)
        {
            CloseWith(searchItem.SlipNumber);
        }
    }
}
