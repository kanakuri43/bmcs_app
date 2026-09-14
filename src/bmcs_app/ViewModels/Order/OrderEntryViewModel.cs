using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using bmcs_app.Application.Common;
using bmcs_app.Application.Master;
using bmcs_app.Application.Order;
using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Services;
using bmcs_app.ViewModels.Common;
using bmcs_app.Views.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bmcs_app.ViewModels.Order;

/// <summary>
/// 受注入力画面（TODO.md 4-3・5-3）。旧プロトタイプ（bmcs_app.Order）のレイアウトを再現する。
/// 新規登録に加え、既存受注の読み込み（表示専用）と中止（F8）を扱う（TODO.md 5-3で追加。
/// 4-4で先行実装した <see cref="OrderStatusService.CancelSlipAsync"/> の画面配線）。
/// 既存受注の内容そのものの訂正（数量・単価等の変更保存）は本タスクの範囲外
/// （<see cref="OrderService"/> に更新系ユースケースがないため）。担当者・前後移動は、
/// このプロジェクトのスキーマ／機能にまだ存在しないため、枠のみ用意し無効化している
/// （詳細は docs/design_document.md）。
/// </summary>
public partial class OrderEntryViewModel(
    ProductService productService,
    CustomerService customerService,
    TaxRateQueryService taxRateQueryService,
    OrderService orderService,
    OrderQueryService orderQueryService,
    OrderStatusService orderStatusService,
    WindowService windowService,
    IUnitPriceCalculator unitPriceCalculator) : ViewModelBase
{
    private Customer? _customer;
    private IReadOnlyList<TaxRateMaster> _taxRateMasters = [];

    /// <summary>読込中の受注伝票番号。<c>null</c> は新規（未保存）を意味する。</summary>
    private string? _loadedOrderSlipNumber;

    public ObservableCollection<SlipLineViewModel> Lines { get; } = [];

    [ObservableProperty]
    public partial string CustomerCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CustomerName { get; set; } = string.Empty;

    /// <summary>得意先の税区分の表示（商品検索モーダルへ渡す単価列の判定と一致させるため）。</summary>
    [ObservableProperty]
    public partial string CustomerTaxUnitDisplay { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OrderDateText { get; set; } = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy/MM/dd");

    /// <summary>
    /// 受注No.欄。採番は保存時にトランザクション内で行うため、新規時は空欄（Watermarkで案内）。
    /// 番号を直接入力して <c>Return</c> で読み込む、または <c>Space</c> で検索モーダルを開ける。
    /// 空欄のまま <c>Return</c> は新規登録モードとして次項目へフォーカス移動する
    /// （docs/product-spec.md UI/UX節「ジャーナル系画面の伝票No入力欄の挙動」）。
    /// </summary>
    [ObservableProperty]
    public partial string OrderSlipNumberDisplay { get; set; } = string.Empty;

    /// <summary>
    /// 担当者コード・名称（枠のみ）。<see cref="OrderSlip"/> に担当者列がなく、
    /// 社員マスタ画面（TODO.md 2-3）も未着手のため、値を持つだけで参照・更新する処理はない。
    /// </summary>
    [ObservableProperty]
    public partial string EmployeeCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EmployeeName { get; set; } = string.Empty;

    /// <summary>伝票摘要。同一伝票の全行に複写して保存する（<see cref="OrderSlip.SlipRemarks"/>）。</summary>
    [ObservableProperty]
    public partial string SlipRemarks { get; set; } = string.Empty;

    /// <summary>
    /// 保存済みかどうか。<see cref="OrderService"/> に更新系ユースケースがないため、
    /// 保存済みの受注を同じ画面から再度保存（＝二重登録）できないようにする。
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool IsSaved { get; set; }

    private bool CanSave => !IsSaved;

    private bool CanDeleteSlip => _loadedOrderSlipNumber is not null && OrderStatus != OrderStatus.Cancelled;

    /// <summary>受注状態バッジ。新規（未保存）は常に「未売上」、読込後は実際の状態を表示する。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSlipCommand))]
    public partial OrderStatus OrderStatus { get; set; } = OrderStatus.NotSold;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    // ── フッター集計（TODO.md 5-1 の ConsumptionTaxCalculator を再利用） ──────────
    public decimal TaxExcludedTotal => ComputeTaxSummary().TaxableAmount;

    public decimal TaxTotal => ComputeTaxSummary().TaxAmount;

    /// <summary>旧プロトタイプと同じく、外税得意先では消費税計と同値を「（外税）」欄に表示する。</summary>
    public decimal ExternalTaxTotal => TaxTotal;

    public decimal GrandTotal => ComputeTaxSummary().TotalAmount;

    public decimal GrossProfit => TaxExcludedTotal - NonBlankLines.Sum(l => l.CostPrice * l.Quantity);

    private IEnumerable<SlipLineViewModel> NonBlankLines => Lines.Where(l => !l.IsBlank);

    /// <summary>
    /// 得意先の税区分が内税明細単位（Line）なら明細ごとに1回の端数処理、
    /// それ以外（Invoice/Slip）は伝票全体で税率ごとに1回の端数処理（暫定 C-4b）。
    /// 空行（商品未選択）は税種別区分が未対応値のため、計算対象から除外する。
    /// </summary>
    private TaxSummary ComputeTaxSummary()
    {
        var taxLines = NonBlankLines.Select(l => new TaxLine(l.TaxCategory, l.TaxRate, l.Amount)).ToList();
        if (taxLines.Count == 0)
        {
            return TaxSummary.Zero;
        }

        var roundingType = _customer?.RoundingType ?? RoundingType.Floor;

        return _customer?.TaxUnit == TaxUnit.Line
            ? ConsumptionTaxCalculator.CalculateInternalTaxPerLine(taxLines, roundingType)
            : ConsumptionTaxCalculator.CalculateExternalTax(taxLines, roundingType);
    }

    [RelayCommand]
    private Task LoadAsync() => RunBusyAsync(async () =>
    {
        _taxRateMasters = await taxRateQueryService.GetAllAsync();
        Lines.CollectionChanged += OnLinesCollectionChanged;

        if (Lines.Count == 0)
        {
            Lines.Add(CreateLine());
            RenumberLines();
        }
    });

    [RelayCommand]
    private void OpenCustomerSearch()
    {
        var customer = windowService.ShowDialog<CustomerSearchDialog, CustomerSearchDialogViewModel, Customer>();
        if (customer is not null)
        {
            ApplyCustomer(customer);
        }
    }

    [RelayCommand]
    private async Task LookupCustomerByCodeAsync()
    {
        if (string.IsNullOrWhiteSpace(CustomerCode))
        {
            return;
        }

        var customer = await customerService.GetByCodeAsync(CustomerCode);
        if (customer is null)
        {
            StatusMessage = $"得意先コード「{CustomerCode}」が見つかりません。";
            return;
        }

        ApplyCustomer(customer);
    }

    private void ApplyCustomer(Customer customer)
    {
        _customer = customer;
        CustomerCode = customer.CustomerCode;
        CustomerName = customer.CustomerName;
        CustomerTaxUnitDisplay = customer.TaxUnit switch
        {
            TaxUnit.Invoice => "請求単位",
            TaxUnit.Slip => "伝票単位",
            TaxUnit.Line => "内税明細単位",
            _ => customer.TaxUnit.ToString(),
        };

        foreach (var line in Lines)
        {
            line.RoundingType = customer.RoundingType;
            line.RaiseAmountChanged();
        }

        RaiseTotalsChanged();
        StatusMessage = $"得意先: {customer.CustomerName}";
    }

    // ── 明細行 ────────────────────────────────────────────────
    [RelayCommand]
    private void AddLine()
    {
        Lines.Add(CreateLine());
        RenumberLines();
    }

    private SlipLineViewModel CreateLine()
    {
        var line = new SlipLineViewModel(
            onOpenProductLookup: OnOpenProductLookupAsync,
            onLookupProductByCode: OnLookupProductByCodeAsync,
            onDelete: OnDeleteLine)
        {
            IsSlipTypeVisible = false, // order_slip に伝票区分の概念がないため（TODO.md 5-4）
        };

        if (_customer is not null)
        {
            line.RoundingType = _customer.RoundingType;
        }

        return line;
    }

    private void OnLinesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (SlipLineViewModel line in e.NewItems)
            {
                line.PropertyChanged += OnLinePropertyChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (SlipLineViewModel line in e.OldItems)
            {
                line.PropertyChanged -= OnLinePropertyChanged;
            }
        }

        RaiseTotalsChanged();
    }

    private void OnLinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SlipLineViewModel.Amount)
                            or nameof(SlipLineViewModel.CostPrice)
                            or nameof(SlipLineViewModel.Quantity)
                            or nameof(SlipLineViewModel.ProductCode))
        {
            RaiseTotalsChanged();
        }
    }

    private void RaiseTotalsChanged()
    {
        OnPropertyChanged(nameof(TaxExcludedTotal));
        OnPropertyChanged(nameof(TaxTotal));
        OnPropertyChanged(nameof(ExternalTaxTotal));
        OnPropertyChanged(nameof(GrandTotal));
        OnPropertyChanged(nameof(GrossProfit));
    }

    private async Task OnOpenProductLookupAsync(SlipLineViewModel line)
    {
        var customerCode = string.IsNullOrWhiteSpace(CustomerCode) ? null : CustomerCode;
        var taxUnit = _customer?.TaxUnit;

        var selections = windowService.ShowDialog<ProductSearchDialog, ProductSearchDialogViewModel, IReadOnlyList<ProductSelection>>(
            vm =>
            {
                vm.TargetCustomerCode = customerCode;
                vm.TargetTaxUnit = taxUnit;
            });

        if (selections is null || selections.Count == 0)
        {
            return;
        }

        await ApplySelectionsAsync(line, selections);
    }

    private async Task OnLookupProductByCodeAsync(SlipLineViewModel line)
    {
        if (string.IsNullOrWhiteSpace(line.ProductCode))
        {
            return;
        }

        var product = await productService.GetByCodeAsync(line.ProductCode);
        if (product is null)
        {
            StatusMessage = $"商品コード「{line.ProductCode}」が見つかりません。";
            return;
        }

        var taxUnit = _customer?.TaxUnit ?? TaxUnit.Invoice;
        var selection = new ProductSelection(
            product.ProductCode,
            product.ProductName,
            product.Specification,
            product.UnitName,
            unitPriceCalculator.SelectStandardUnitPrice(product, taxUnit),
            product.StandardCostPrice,
            product.TaxCategory,
            ProductSelectionSource.Master);

        StatusMessage = string.Empty;
        await ApplySelectionsAsync(line, [selection]);
    }

    /// <summary>
    /// 商品検索モーダルの選択結果（最大6件）を行へ展開する。1件目は呼び出した行、
    /// 残りは後続の空行を埋め、足りなければ直後に挿入する（TODO.md 4-2）。
    /// </summary>
    private Task ApplySelectionsAsync(SlipLineViewModel invokingLine, IReadOnlyList<ProductSelection> selections)
    {
        var orderDate = DateOnly.TryParseExact(OrderDateText, "yyyy/MM/dd", out var parsed)
            ? parsed
            : DateOnly.FromDateTime(DateTime.Today);
        var roundingType = _customer?.RoundingType ?? RoundingType.Floor;

        var targetLines = new List<SlipLineViewModel> { invokingLine };
        var invokingIndex = Lines.IndexOf(invokingLine);

        for (var i = 1; i < selections.Count; i++)
        {
            var nextBlank = Lines.Skip(invokingIndex + 1).FirstOrDefault(l => l.IsBlank && !targetLines.Contains(l));
            if (nextBlank is not null)
            {
                targetLines.Add(nextBlank);
                continue;
            }

            var newLine = CreateLine();
            Lines.Insert(invokingIndex + targetLines.Count, newLine);
            targetLines.Add(newLine);
        }

        for (var i = 0; i < selections.Count; i++)
        {
            var selection = selections[i];
            var line = targetLines[i];
            var taxRate = TaxRateResolver.ResolveRate(_taxRateMasters, orderDate, selection.TaxCategory);

            line.ProductCode = selection.ProductCode;
            line.ProductName = selection.ProductName;
            line.Specification = selection.Specification;
            line.UnitName = selection.UnitName;
            line.UnitPrice = selection.UnitPrice;
            line.CostPrice = selection.CostPrice;
            line.TaxCategory = selection.TaxCategory;
            line.TaxRate = taxRate;
            line.RoundingType = roundingType;
            line.RaiseAmountChanged();

            if (line.Quantity == 0)
            {
                line.Quantity = 1;
            }
        }

        EnsureTrailingBlankLine();
        RenumberLines();
        invokingLine.RequestMoveToQuantity();

        return Task.CompletedTask;
    }

    private void OnDeleteLine(SlipLineViewModel line)
    {
        if (Lines.Count <= 1)
        {
            return;
        }

        Lines.Remove(line);
        EnsureTrailingBlankLine();
        RenumberLines();
    }

    private void EnsureTrailingBlankLine()
    {
        if (Lines.Count == 0 || !Lines[^1].IsBlank)
        {
            Lines.Add(CreateLine());
        }
    }

    private void RenumberLines()
    {
        for (var i = 0; i < Lines.Count; i++)
        {
            Lines[i].LineNumber = (short)(i + 1);
        }
    }

    // ── 新規 ─────────────────────────────────────────────────
    [RelayCommand]
    private void New()
    {
        _customer = null;
        _loadedOrderSlipNumber = null;
        CustomerCode = string.Empty;
        CustomerName = string.Empty;
        CustomerTaxUnitDisplay = string.Empty;
        OrderDateText = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy/MM/dd");
        OrderSlipNumberDisplay = string.Empty;
        OrderStatus = OrderStatus.NotSold;
        IsSaved = false;

        foreach (var line in Lines)
        {
            line.PropertyChanged -= OnLinePropertyChanged;
        }

        Lines.Clear();
        Lines.Add(CreateLine());
        RenumberLines();
        StatusMessage = "新規受注";
    }

    // ── 既存受注の読み込み・中止（TODO.md 5-3） ────────────────
    [RelayCommand]
    private void OpenOrderSlipSearch()
    {
        var orderSlipNumber = windowService.ShowDialog<SlipSearchDialog, SlipSearchDialogViewModel, string>(
            vm => vm.Target = SlipSearchTarget.Order);

        if (!string.IsNullOrWhiteSpace(orderSlipNumber))
        {
            _ = LoadOrderForViewingAsync(orderSlipNumber);
        }
    }

    /// <summary>
    /// 受注No.欄で Return を押したときの挙動。空欄なら新規登録モードとして次項目（受注日付）へ
    /// フォーカス移動するのみ。入力済みなら既存受注を表示専用で読み込む
    /// （docs/product-spec.md UI/UX節「ジャーナル系画面の伝票No入力欄の挙動」）。
    /// </summary>
    [RelayCommand]
    private Task LookupOrderSlipByNumberAsync() => RunBusyAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(OrderSlipNumberDisplay))
        {
            RequestFocus("OrderDate");
            return;
        }

        await LoadOrderForViewingAsync(OrderSlipNumberDisplay);
    });

    /// <summary>
    /// 既存受注を表示専用で読み込む。内容の訂正保存には対応しない（クラス冒頭の注記参照）ため、
    /// 読込後は <see cref="IsSaved"/> を立てて誤った再保存（＝新規登録）を防ぐ。
    /// </summary>
    private async Task LoadOrderForViewingAsync(string orderSlipNumber)
    {
        if (string.IsNullOrWhiteSpace(orderSlipNumber))
        {
            return;
        }

        var sourceLines = await orderQueryService.GetSlipAsync(orderSlipNumber);
        if (sourceLines.Count == 0)
        {
            StatusMessage = $"受注No.「{orderSlipNumber}」が見つかりません。";
            return;
        }

        var customer = await customerService.GetByCodeAsync(sourceLines[0].CustomerCode);
        if (customer is null)
        {
            StatusMessage = "得意先が見つかりません。";
            return;
        }

        _customer = customer;
        _loadedOrderSlipNumber = orderSlipNumber;

        CustomerCode = customer.CustomerCode;
        CustomerName = sourceLines[0].CustomerName;
        CustomerTaxUnitDisplay = customer.TaxUnit switch
        {
            TaxUnit.Invoice => "請求単位",
            TaxUnit.Slip => "伝票単位",
            TaxUnit.Line => "内税明細単位",
            _ => customer.TaxUnit.ToString(),
        };
        OrderDateText = sourceLines[0].OrderDate.ToString("yyyy/MM/dd");
        OrderSlipNumberDisplay = orderSlipNumber;
        SlipRemarks = sourceLines[0].SlipRemarks ?? string.Empty;
        OrderStatus = sourceLines.Any(l => l.OrderStatus == OrderStatus.Cancelled)
            ? OrderStatus.Cancelled
            : sourceLines.All(l => l.OrderStatus == OrderStatus.FullySold)
                ? OrderStatus.FullySold
                : sourceLines.Any(l => l.OrderStatus == OrderStatus.PartiallySold)
                    ? OrderStatus.PartiallySold
                    : OrderStatus.NotSold;
        IsSaved = true; // 内容の訂正保存は対象外のため、新規登録用のSaveを封じる

        foreach (var line in Lines)
        {
            line.PropertyChanged -= OnLinePropertyChanged;
        }

        Lines.Clear();
        foreach (var source in sourceLines.OrderBy(l => l.LineNumber))
        {
            var line = CreateLine();
            line.ProductCode = source.ProductCode;
            line.ProductName = source.ProductName;
            line.Specification = source.Specification;
            line.UnitName = source.UnitName;
            line.Quantity = source.OrderQuantity;
            line.UnitPrice = source.UnitPrice;
            line.CostPrice = source.CostPrice;
            line.TaxCategory = source.TaxCategory;
            line.TaxRate = source.TaxRate;
            line.RoundingType = customer.RoundingType;
            line.LineRemarks = source.LineRemarks ?? string.Empty;
            line.RaiseAmountChanged();
            Lines.Add(line);
        }

        RenumberLines();
        RaiseTotalsChanged();
        StatusMessage = $"受注No. {orderSlipNumber} を読み込みました（表示専用。内容の訂正は未対応）。";
        RequestFocus("OrderDate");
    }

    // ── 保存 ─────────────────────────────────────────────────
    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveAsync() => RunBusyAsync(async () =>
    {
        if (_customer is null)
        {
            StatusMessage = "得意先を指定してください。";
            return;
        }

        var nonBlankLines = Lines.Where(l => !l.IsBlank).ToList();
        if (nonBlankLines.Count == 0)
        {
            StatusMessage = "明細行を1件以上入力してください。";
            return;
        }

        var orderDate = DateOnly.TryParseExact(OrderDateText, "yyyy/MM/dd", out var parsed)
            ? parsed
            : DateOnly.FromDateTime(DateTime.Today);

        var now = DateTime.Now;
        var slipRemarks = string.IsNullOrWhiteSpace(SlipRemarks) ? null : SlipRemarks;
        var entities = nonBlankLines.Select((line, index) => new OrderSlip
        {
            OrderSlipNumber = string.Empty, // OrderService.CreateAsync がトランザクション内の採番結果で上書きする
            LineNumber = (short)(index + 1),
            OrderDate = orderDate,
            CustomerCode = _customer.CustomerCode,
            CustomerName = this.CustomerName, // ViewModelのプロパティ（手入力で上書きされていればその値。C-9・2026-09-10確定）
            ProductCode = line.ProductCode,
            ProductName = line.ProductName,
            Specification = line.Specification,
            UnitName = line.UnitName,
            OrderQuantity = line.Quantity,
            UnitPrice = line.UnitPrice,
            Amount = line.Amount,
            CostPrice = line.CostPrice,
            TaxCategory = line.TaxCategory,
            TaxRate = line.TaxRate,
            AllocatedQuantity = 0m,
            OrderStatus = OrderStatus.NotSold,
            SalesConfirmedQuantity = 0m,
            SlipRemarks = slipRemarks, // 伝票摘要は全行に複写する（docs/database-schema.md 1章）
            LineRemarks = string.IsNullOrWhiteSpace(line.LineRemarks) ? null : line.LineRemarks,
            CreatedBy = string.Empty, // OrderService.CreateAsync が現在の社員コードで上書きする
            CreatedAt = now,
            UpdatedBy = string.Empty,
            UpdatedAt = now,
        }).ToList();

        try
        {
            var orderSlipNumber = await orderService.CreateAsync(entities);

            // 登録後は画面を起動直後の状態へ戻す（docs/product-spec.md UI/UX節「登録後のリセット」）。
            New();
            StatusMessage = $"登録しました。受注No. {orderSlipNumber}";
            NotifyResetToInitialState();
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存エラー: {ex.Message}";
        }
    });

    // ── 枠のみ・使用不可（TODO.md 4-3。理由は docs/design_document.md 参照） ──────
    private bool CanUseUnimplementedFeature => false;

    /// <summary>
    /// 中止（TODO.md 4-4・5-3）。物理削除はしない（M-17）。<see cref="OrderStatusService.CancelSlipAsync"/>
    /// による伝票単位の中止（終端状態・解除なし）。売上完了済みの明細行を含む受注・
    /// 既に中止済みの受注は拒否される。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDeleteSlip))]
    private Task DeleteSlipAsync() => RunBusyAsync(async () =>
    {
        if (_loadedOrderSlipNumber is null)
        {
            return;
        }

        try
        {
            await orderStatusService.CancelSlipAsync(_loadedOrderSlipNumber);
            var cancelledOrderSlipNumber = _loadedOrderSlipNumber;
            New();
            StatusMessage = $"受注No. {cancelledOrderSlipNumber} を中止しました。";
        }
        catch (OrderOperationException ex)
        {
            StatusMessage = $"中止エラー: {ex.Message}";
        }
        catch (OrderConcurrencyException ex)
        {
            StatusMessage = $"中止エラー: {ex.Message}";
        }
    });

    /// <summary>既存受注の一覧・検索機能は伝票検索モーダルに統合済みのため、前後移動は当面実装しない。</summary>
    [RelayCommand(CanExecute = nameof(CanUseUnimplementedFeature))]
    private void PrevSlip()
    {
    }

    [RelayCommand(CanExecute = nameof(CanUseUnimplementedFeature))]
    private void NextSlip()
    {
    }
}
