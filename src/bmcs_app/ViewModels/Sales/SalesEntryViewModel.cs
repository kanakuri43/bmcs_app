using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using bmcs_app.Application.Common;
using bmcs_app.Application.Master;
using bmcs_app.Application.Order;
using bmcs_app.Application.Sales;
using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Reports;
using bmcs_app.Services;
using bmcs_app.ViewModels.Common;
using bmcs_app.Views.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SalesEntity = bmcs_app.Domain.Entities.Sales;

namespace bmcs_app.ViewModels.Sales;

/// <summary>
/// 売上入力画面（TODO.md 5-2・5-3・5-4・5-5・5-6）。都度売上の直接入力、受注からの売上確定、
/// 返品・値引、過去伝票の複写、既存伝票の訂正・取消を扱う。
/// 印刷・前後移動・担当者は、対象フェーズが別、またはこのプロジェクトのスキーマ／機能に
/// まだ存在しないため、枠のみ用意し無効化している（詳細は docs/design_document.md）。
/// </summary>
public partial class SalesEntryViewModel(
    ProductService productService,
    CustomerService customerService,
    TaxRateQueryService taxRateQueryService,
    SalesService salesService,
    SalesQueryService salesQueryService,
    SalesEditLockService salesEditLockService,
    OrderQueryService orderQueryService,
    DeliveryNoteService deliveryNoteService,
    WindowService windowService,
    IUnitPriceCalculator unitPriceCalculator) : ViewModelBase
{
    private Customer? _customer;
    private IReadOnlyList<TaxRateMaster> _taxRateMasters = [];

    /// <summary>編集中の売上伝票番号。<c>null</c> は新規（都度売上・受注確定・複写）を意味する。</summary>
    private string? _loadedSalesSlipNumber;

    /// <summary>
    /// 伝票プレビュー（TODO.md 8-3）用の入口。得意先元帳からの表示専用で開くとき、
    /// <see cref="Services.WindowService.Show{TWindow, TViewModel}"/> の <c>configure</c> から
    /// ウィンドウ表示前に一度だけ設定する。<see cref="LoadAsync"/> の末尾でこの伝票を読み込み、
    /// 以後値は変化しない（ウィンドウは毎回新規に開くため）ため、<c>[ObservableProperty]</c>や
    /// <c>NotifyCanExecuteChangedFor</c>は不要。
    /// </summary>
    public string? PreviewSlipNumber { get; set; }

    /// <summary>プレビュー表示中かどうか。</summary>
    public bool IsPreviewMode => PreviewSlipNumber is not null;

    /// <summary>読込時点の明細行番号の集合（訂正の排他制御用。docs/architecture.md 9章）。</summary>
    private IReadOnlyList<short> _loadedLineNumbers = [];

    public ObservableCollection<SlipLineViewModel> Lines { get; } = [];

    [ObservableProperty]
    public partial string CustomerCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CustomerName { get; set; } = string.Empty;

    /// <summary>得意先の税区分の表示（商品検索モーダルへ渡す単価列の判定と一致させるため）。</summary>
    [ObservableProperty]
    public partial string CustomerTaxUnitDisplay { get; set; } = string.Empty;

    [ObservableProperty]
    public partial DateTime? SlipDate { get; set; } = DateTime.Today;

    /// <summary>
    /// 売上No.欄。新規時は空欄（Watermarkで「自動採番」を案内）。既存伝票の訂正・取消（TODO.md 5-6）では
    /// 番号を直接入力して <c>Return</c> で読み込む、または <c>Space</c> で検索モーダルを開ける。
    /// 空欄のまま <c>Return</c> は新規登録モードとして次項目へフォーカス移動する
    /// （docs/product-spec.md UI/UX節「ジャーナル系画面の伝票No入力欄の挙動」）。
    /// </summary>
    [ObservableProperty]
    public partial string SalesSlipNumberDisplay { get; set; } = string.Empty;

    /// <summary>受注No.欄（TODO.md 5-3）。<c>Space</c> で受注検索モーダル、<c>Return</c> で直接読込。</summary>
    [ObservableProperty]
    public partial string OrderSlipNumberQuery { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string BillingStatusDisplay { get; set; } = "未請求";

    [ObservableProperty]
    public partial string SettlementStatusDisplay { get; set; } = "未消込";

    /// <summary>
    /// 担当者コード・名称（枠のみ）。<see cref="SalesEntity"/> に担当者列がなく、
    /// 社員マスタ画面（TODO.md 2-3）も未着手のため、値を持つだけで参照・更新する処理はない。
    /// </summary>
    [ObservableProperty]
    public partial string EmployeeCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EmployeeName { get; set; } = string.Empty;

    /// <summary>伝票摘要。同一伝票の全行に複写して保存する（<see cref="SalesEntity.SlipRemarks"/>）。</summary>
    [ObservableProperty]
    public partial string SlipRemarks { get; set; } = string.Empty;

    /// <summary>
    /// 新規登録済みかどうか。新規モードでの二重登録防止にのみ使う
    /// （訂正モードでは <see cref="_loadedSalesSlipNumber"/> と <see cref="IsEditLocked"/> で判定する。
    /// 訂正の再保存はUpdateAsyncが対象伝票を確定して更新するため二重登録にならない）。
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool IsSaved { get; set; }

    /// <summary>編集ロック中かどうか（C-6の3条件。TODO.md 5-6）。ViewModelは判定せず、
    /// <see cref="SalesEditLockService"/> の結果をそのまま表示・反映するだけにする
    /// （docs/architecture.md 5章）。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSlipCommand))]
    public partial bool IsEditLocked { get; set; }

    private bool CanEdit => !IsPreviewMode;

    private bool CanSave => CanEdit && (_loadedSalesSlipNumber is null ? !IsSaved : !IsEditLocked);

    private bool CanDeleteSlip => CanEdit && _loadedSalesSlipNumber is not null && !IsEditLocked;

    /// <summary>ヘッダー入力欄の <c>IsReadOnly</c> バインディング用（TODO.md 8-3）。</summary>
    public bool IsHeaderLocked => IsPreviewMode;

    /// <summary>明細行グリッドの <c>IsEnabled</c> バインディング用（TODO.md 8-3）。</summary>
    public bool IsEditable => !IsPreviewMode;

    /// <summary>ウィンドウタイトル（TODO.md 8-3）。</summary>
    public string WindowTitle => IsPreviewMode ? "bmcs_app - 売上入力（プレビュー・編集不可）" : "bmcs_app - 売上入力";

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    // ── フッター集計（TODO.md 5-1 の ConsumptionTaxCalculator を再利用） ──────────
    public decimal TaxExcludedTotal => ComputeTaxSummary().TaxableAmount;

    public decimal TaxTotal => ComputeTaxSummary().TaxAmount;

    /// <summary>旧プロトタイプと同じく、外税得意先では消費税計と同値を「（外税）」欄に表示する。</summary>
    public decimal ExternalTaxTotal => TaxTotal;

    public decimal GrandTotal => ComputeTaxSummary().TotalAmount;

    /// <summary>
    /// 値引行の原価は0として扱う（TODO.md 5-4。<see cref="SalesSlipTypeRules.NormalizeCostPrice"/>）。
    /// 行自体の <see cref="SlipLineViewModel.CostPrice"/> は破壊的に書き換えない
    /// （値引→売上の往復で原価を失わないため）。
    /// </summary>
    public decimal GrossProfit => TaxExcludedTotal
        - NonBlankLines.Sum(l => SalesSlipTypeRules.NormalizeCostPrice(l.SlipType, l.CostPrice) * l.Quantity);

    private IEnumerable<SlipLineViewModel> NonBlankLines => Lines.Where(l => !l.IsBlank);

    /// <summary>
    /// 得意先の税区分が内税明細単位（Line）なら明細ごとに1回の端数処理、
    /// それ以外（Invoice/Slip）は伝票全体で税率ごとに1回の端数処理（暫定 C-4b）。
    /// これは画面表示専用（フッター集計）であり、保存時の税額確定は
    /// <see cref="SalesTaxAmountAssigner"/> が別途行う。空行（商品未選択）は
    /// 税種別区分が未対応値のため、計算対象から除外する。
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

        if (PreviewSlipNumber is { } previewSlipNumber)
        {
            // Loaded → LoadCommand の async void 経路で呼ばれるため、ここで例外を握らないと
            // アプリがクラッシュする（TODO.md 8-3）。
            try
            {
                await LoadSalesSlipForCorrectionAsync(previewSlipNumber);
            }
            catch (Exception ex)
            {
                StatusMessage = $"プレビューの読込に失敗しました: {ex.Message}";
            }
        }
    });

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void OpenCustomerSearch()
    {
        var customer = windowService.ShowDialog<CustomerSearchDialog, CustomerSearchDialogViewModel, Customer>();
        if (customer is not null)
        {
            ApplyCustomer(customer);
        }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
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

    private DateOnly ParseSlipDate() => SlipDate is { } value ? DateOnly.FromDateTime(value) : DateOnly.FromDateTime(DateTime.Today);

    // ── 明細行 ────────────────────────────────────────────────
    [RelayCommand(CanExecute = nameof(CanEdit))]
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
            onDelete: OnDeleteLine);

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
                            or nameof(SlipLineViewModel.SlipType)
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
        var slipDate = ParseSlipDate();
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
            var taxRate = TaxRateResolver.ResolveRate(_taxRateMasters, slipDate, selection.TaxCategory);

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

    /// <summary>行の購読解除・全消去（New・複写・訂正読込の前処理で共用）。</summary>
    private void ClearLines()
    {
        foreach (var line in Lines)
        {
            line.PropertyChanged -= OnLinePropertyChanged;
        }

        Lines.Clear();
    }

    // ── 新規 ─────────────────────────────────────────────────
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void New()
    {
        if (IsPreviewMode)
        {
            return;
        }

        _customer = null;
        _loadedSalesSlipNumber = null;
        _loadedLineNumbers = [];
        PrintCommand.NotifyCanExecuteChanged();
        CustomerCode = string.Empty;
        CustomerName = string.Empty;
        CustomerTaxUnitDisplay = string.Empty;
        SlipDate = DateTime.Today;
        SalesSlipNumberDisplay = string.Empty;
        OrderSlipNumberQuery = string.Empty;
        BillingStatusDisplay = "未請求";
        SettlementStatusDisplay = "未消込";
        SlipRemarks = string.Empty;
        IsSaved = false;
        IsEditLocked = false;

        ClearLines();
        Lines.Add(CreateLine());
        RenumberLines();
        StatusMessage = "新規売上";
    }

    // ── 保存 ─────────────────────────────────────────────────
    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveAsync() => RunBusyAsync(async () =>
    {
        if (IsPreviewMode)
        {
            return;
        }

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

        if (SlipDate is null)
        {
            StatusMessage = "売上日付を入力してください。";
            return;
        }

        try
        {
            if (_loadedSalesSlipNumber is null)
            {
                await SaveNewAsync(nonBlankLines);
            }
            else
            {
                await SaveCorrectionAsync(nonBlankLines);
            }
        }
        catch (SalesOperationException ex)
        {
            StatusMessage = $"保存エラー: {ex.Message}";
        }
        catch (SlipConcurrencyException ex)
        {
            StatusMessage = $"保存エラー: {ex.Message}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存エラー: {ex.Message}";
        }
    });

    private async Task SaveNewAsync(List<SlipLineViewModel> nonBlankLines)
    {
        var slipDate = ParseSlipDate();
        var now = DateTime.Now;
        var slipRemarks = string.IsNullOrWhiteSpace(SlipRemarks) ? null : SlipRemarks;
        var entities = nonBlankLines.Select((line, index) => BuildEntity(
            salesSlipNumber: string.Empty, // SalesService.CreateAsync がトランザクション内の採番結果で上書きする
            lineNumber: (short)(index + 1),
            slipDate: slipDate,
            slipRemarks: slipRemarks,
            line: line,
            now: now)).ToList();

        var salesSlipNumber = await salesService.CreateAsync(entities, _customer!.RoundingType);

        var printNote = await PromptAndPrintDeliveryNoteAsync(salesSlipNumber);

        // 登録後は画面を起動直後の状態へ戻す（docs/product-spec.md UI/UX節「登録後のリセット」）。
        // 納品書の発行有無に関わらずリセットする（TODO.md 10-4。保存直後の発行確認は
        // New() の前に完結させ、リセット自体の方針は変えない）。
        New();
        StatusMessage = printNote is null
            ? $"登録しました。売上No. {salesSlipNumber}"
            : $"登録しました。売上No. {salesSlipNumber}　{printNote}";
        NotifyResetToInitialState();
    }

    private async Task SaveCorrectionAsync(List<SlipLineViewModel> nonBlankLines)
    {
        var slipDate = ParseSlipDate();
        var now = DateTime.Now;
        var slipRemarks = string.IsNullOrWhiteSpace(SlipRemarks) ? null : SlipRemarks;
        var entities = nonBlankLines.Select(line => BuildEntity(
            salesSlipNumber: _loadedSalesSlipNumber!,
            lineNumber: line.PersistedLineNumber ?? 0,
            slipDate: slipDate,
            slipRemarks: slipRemarks,
            line: line,
            now: now)).ToList();

        var correctedSalesSlipNumber = _loadedSalesSlipNumber!;
        await salesService.UpdateAsync(correctedSalesSlipNumber, entities, _customer!.RoundingType, _loadedLineNumbers);

        var printNote = await PromptAndPrintDeliveryNoteAsync(correctedSalesSlipNumber);

        // 登録後は画面を起動直後の状態へ戻す（docs/product-spec.md UI/UX節「登録後のリセット」）。
        // 新規登録と挙動を揃え、訂正保存だけ伝票を表示し続ける例外を作らない。
        New();
        StatusMessage = printNote is null
            ? $"訂正しました。売上No. {correctedSalesSlipNumber}"
            : $"訂正しました。売上No. {correctedSalesSlipNumber}　{printNote}";
        NotifyResetToInitialState();
    }

    private SalesEntity BuildEntity(
        string salesSlipNumber, short lineNumber, DateOnly slipDate, string? slipRemarks, SlipLineViewModel line, DateTime now) => new()
    {
        SalesSlipNumber = salesSlipNumber,
        LineNumber = lineNumber,
        SlipDate = slipDate,
        CustomerCode = _customer!.CustomerCode,
        TaxUnit = _customer.TaxUnit,
        CustomerName = CustomerName, // 手入力で上書きされていればその値（C-9・2026-09-10確定）
        SlipType = line.SlipType,
        ProductCode = line.ProductCode,
        ProductName = line.ProductName,
        Specification = line.Specification,
        UnitName = line.UnitName,
        Quantity = line.Quantity,
        UnitPrice = line.UnitPrice,
        Amount = line.Amount,
        CostPrice = SalesSlipTypeRules.NormalizeCostPrice(line.SlipType, line.CostPrice),
        TaxCategory = line.TaxCategory,
        TaxRate = line.TaxRate,
        // SlipTaxAmount / TaxAmount は設定しない。SalesService が SalesTaxAmountAssigner で確定する。
        DeliveryNoteIssueCount = 0,
        BillingStatus = BillingLinkStatus.Unbilled,
        SettlementStatus = SettlementStatus.Unsettled,
        SettledAmount = 0m,
        OrderSlipNumber = line.OrderSlipNumber,
        OrderLineNumber = line.OrderLineNumber,
        SlipRemarks = slipRemarks,
        LineRemarks = string.IsNullOrWhiteSpace(line.LineRemarks) ? null : line.LineRemarks,
        CreatedBy = string.Empty,
        CreatedAt = now,
        UpdatedBy = string.Empty,
        UpdatedAt = now,
    };

    // ── 受注からの売上確定（TODO.md 5-3） ──────────────────────
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void OpenOrderSlipSearch()
    {
        var orderSlipNumber = windowService.ShowDialog<SlipSearchDialog, SlipSearchDialogViewModel, string>(
            vm => vm.Target = SlipSearchTarget.Order);

        if (!string.IsNullOrWhiteSpace(orderSlipNumber))
        {
            _ = LoadOrderIntoLinesAsync(orderSlipNumber);
        }
    }

    /// <summary>
    /// 受注No.欄で Return を押したときの挙動。空欄なら次項目（得意先）へフォーカス移動するのみ。
    /// 入力済みなら受注の残数量を明細行へ転記する（docs/product-spec.md UI/UX節参照）。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task LookupOrderSlipByNumberAsync() => RunBusyAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(OrderSlipNumberQuery))
        {
            RequestFocus("CustomerCode");
            return;
        }

        await LoadOrderIntoLinesAsync(OrderSlipNumberQuery);
    });

    private async Task LoadOrderIntoLinesAsync(string orderSlipNumber)
    {
        if (string.IsNullOrWhiteSpace(orderSlipNumber))
        {
            return;
        }

        var orderLines = await orderQueryService.GetSlipAsync(orderSlipNumber);
        if (orderLines.Count == 0)
        {
            StatusMessage = $"受注No.「{orderSlipNumber}」が見つかりません。";
            return;
        }

        var sellableLines = orderLines
            .Where(o => o.OrderStatus is OrderStatus.NotSold or OrderStatus.PartiallySold)
            .Where(o => o.OrderQuantity - o.SalesConfirmedQuantity > 0m)
            .ToList();

        if (sellableLines.Count == 0)
        {
            StatusMessage = $"受注No.「{orderSlipNumber}」に売上化できる明細行がありません。";
            return;
        }

        var orderCustomer = await customerService.GetByCodeAsync(sellableLines[0].CustomerCode);
        if (orderCustomer is null)
        {
            StatusMessage = "受注の得意先が見つかりません。";
            return;
        }

        if (_customer is null)
        {
            ApplyCustomer(orderCustomer);
        }
        else if (_customer.CustomerCode != orderCustomer.CustomerCode)
        {
            StatusMessage = "この売上とは異なる得意先の受注のため転記できません。";
            return;
        }

        var slipDate = ParseSlipDate();
        foreach (var orderLine in sellableLines)
        {
            var target = Lines.FirstOrDefault(l => l.IsBlank) ?? AppendNewLine();
            var remainingQuantity = orderLine.OrderQuantity - orderLine.SalesConfirmedQuantity;
            var taxRate = TaxRateResolver.ResolveRate(_taxRateMasters, slipDate, orderLine.TaxCategory);

            target.ProductCode = orderLine.ProductCode;
            target.ProductName = orderLine.ProductName;
            target.Specification = orderLine.Specification;
            target.UnitName = orderLine.UnitName;
            target.UnitPrice = orderLine.UnitPrice;
            target.CostPrice = orderLine.CostPrice;
            target.TaxCategory = orderLine.TaxCategory;
            target.TaxRate = taxRate;
            target.RoundingType = _customer!.RoundingType;
            target.OrderSlipNumber = orderLine.OrderSlipNumber;
            target.OrderLineNumber = orderLine.LineNumber;
            target.Quantity = remainingQuantity;
            target.RaiseAmountChanged();
        }

        EnsureTrailingBlankLine();
        RenumberLines();
        OrderSlipNumberQuery = orderSlipNumber;
        StatusMessage = $"受注No. {orderSlipNumber} を読み込みました（残数量を転記）。";
        RequestFocus("CustomerCode");
    }

    private SlipLineViewModel AppendNewLine()
    {
        var line = CreateLine();
        Lines.Add(line);
        return line;
    }

    // ── 過去伝票の複写（TODO.md 5-5） ──────────────────────────
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task CopyFromPastSlipAsync() => RunBusyAsync(async () =>
    {
        var sourceSlipNumber = windowService.ShowDialog<SlipSearchDialog, SlipSearchDialogViewModel, string>(
            vm => vm.Target = SlipSearchTarget.Sales);

        if (string.IsNullOrWhiteSpace(sourceSlipNumber))
        {
            return;
        }

        var sourceLines = await salesQueryService.GetSlipAsync(sourceSlipNumber);
        if (sourceLines.Count == 0)
        {
            StatusMessage = $"売上No.「{sourceSlipNumber}」が見つかりません。";
            return;
        }

        var customer = await customerService.GetByCodeAsync(sourceLines[0].CustomerCode);
        if (customer is null)
        {
            StatusMessage = "複写元の得意先が見つかりません。";
            return;
        }

        New();
        ApplyCustomer(customer);
        CustomerName = sourceLines[0].CustomerName;

        var slipDate = ParseSlipDate();
        ClearLines();
        foreach (var source in sourceLines.OrderBy(l => l.LineNumber))
        {
            var line = CreateLine();
            var taxRate = TaxRateResolver.ResolveRate(_taxRateMasters, slipDate, source.TaxCategory);

            line.ProductCode = source.ProductCode;
            line.ProductName = source.ProductName;
            line.Specification = source.Specification;
            line.UnitName = source.UnitName;
            line.SlipType = source.SlipType;
            line.Quantity = source.Quantity;
            line.UnitPrice = source.UnitPrice;
            line.CostPrice = source.CostPrice;
            line.TaxCategory = source.TaxCategory;
            line.TaxRate = taxRate;
            line.RoundingType = customer.RoundingType;
            line.LineRemarks = source.LineRemarks ?? string.Empty;
            line.RaiseAmountChanged();
            Lines.Add(line);
        }

        SlipRemarks = sourceLines[0].SlipRemarks ?? string.Empty;
        EnsureTrailingBlankLine();
        RenumberLines();
        StatusMessage = $"売上No. {sourceSlipNumber} を複写しました（新規登録として保存されます）。";
    });

    // ── 既存伝票の訂正・取消（TODO.md 5-6） ────────────────────
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void OpenSalesSlipSearch()
    {
        var salesSlipNumber = windowService.ShowDialog<SlipSearchDialog, SlipSearchDialogViewModel, string>(
            vm => vm.Target = SlipSearchTarget.Sales);

        if (!string.IsNullOrWhiteSpace(salesSlipNumber))
        {
            _ = LoadSalesSlipForCorrectionAsync(salesSlipNumber);
        }
    }

    /// <summary>
    /// 売上No.欄で Return を押したときの挙動（docs/product-spec.md UI/UX節「ジャーナル系画面の
    /// 伝票No入力欄の挙動」）。空欄なら新規登録モードとして次項目（売上日付）へフォーカス移動するのみ。
    /// 入力済みなら既存伝票の訂正・取消として読み込む。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task LookupSalesSlipByNumberAsync() => RunBusyAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(SalesSlipNumberDisplay))
        {
            RequestFocus("SlipDate");
            return;
        }

        await LoadSalesSlipForCorrectionAsync(SalesSlipNumberDisplay);
    });

    private async Task LoadSalesSlipForCorrectionAsync(string salesSlipNumber)
    {
        if (string.IsNullOrWhiteSpace(salesSlipNumber))
        {
            return;
        }

        var sourceLines = await salesQueryService.GetSlipAsync(salesSlipNumber);
        if (sourceLines.Count == 0)
        {
            StatusMessage = $"売上No.「{salesSlipNumber}」が見つかりません。";
            return;
        }

        var customer = await customerService.GetByCodeAsync(sourceLines[0].CustomerCode);
        if (customer is null)
        {
            StatusMessage = "得意先が見つかりません。";
            return;
        }

        var lockResult = await salesEditLockService.EvaluateAsync(sourceLines);

        _customer = customer;
        _loadedSalesSlipNumber = salesSlipNumber;
        _loadedLineNumbers = sourceLines.Select(l => l.LineNumber).ToList();
        PrintCommand.NotifyCanExecuteChanged();

        CustomerCode = customer.CustomerCode;
        CustomerName = sourceLines[0].CustomerName;
        CustomerTaxUnitDisplay = customer.TaxUnit switch
        {
            TaxUnit.Invoice => "請求単位",
            TaxUnit.Slip => "伝票単位",
            TaxUnit.Line => "内税明細単位",
            _ => customer.TaxUnit.ToString(),
        };
        SlipDate = sourceLines[0].SlipDate.ToDateTime(TimeOnly.MinValue);
        SalesSlipNumberDisplay = salesSlipNumber;
        SlipRemarks = sourceLines[0].SlipRemarks ?? string.Empty;
        BillingStatusDisplay = sourceLines.Any(l => l.BillingStatus == BillingLinkStatus.Billed) ? "請求済" : "未請求";
        SettlementStatusDisplay = sourceLines.All(l => l.SettlementStatus == SettlementStatus.FullySettled)
            ? "消込完了"
            : sourceLines.Any(l => l.SettlementStatus != SettlementStatus.Unsettled)
                ? "一部消込"
                : "未消込";
        IsSaved = false;
        IsEditLocked = lockResult.IsLocked;

        ClearLines();
        foreach (var source in sourceLines.OrderBy(l => l.LineNumber))
        {
            var line = CreateLine();
            line.PersistedLineNumber = source.LineNumber;
            line.ProductCode = source.ProductCode;
            line.ProductName = source.ProductName;
            line.Specification = source.Specification;
            line.UnitName = source.UnitName;
            line.SlipType = source.SlipType;
            line.Quantity = source.Quantity;
            line.UnitPrice = source.UnitPrice;
            line.CostPrice = source.CostPrice;
            line.TaxCategory = source.TaxCategory;
            line.TaxRate = source.TaxRate;
            line.RoundingType = customer.RoundingType;
            line.OrderSlipNumber = source.OrderSlipNumber;
            line.OrderLineNumber = source.OrderLineNumber;
            line.LineRemarks = source.LineRemarks ?? string.Empty;
            line.RaiseAmountChanged();
            Lines.Add(line);
        }

        if (!IsPreviewMode)
        {
            EnsureTrailingBlankLine();
        }
        RenumberLines();
        RaiseTotalsChanged();

        StatusMessage = IsPreviewMode
            ? $"売上No. {salesSlipNumber} をプレビュー表示中（編集できません）。"
            : lockResult.IsLocked
                ? $"売上No. {salesSlipNumber} を読み込みました（編集不可: {lockResult.Reason}）"
                : $"売上No. {salesSlipNumber} を読み込みました。";

        if (!IsPreviewMode)
        {
            RequestFocus("SlipDate");
        }
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSlip))]
    private Task DeleteSlipAsync() => RunBusyAsync(async () =>
    {
        if (IsPreviewMode || _loadedSalesSlipNumber is null)
        {
            return;
        }

        try
        {
            await salesService.CancelSlipAsync(_loadedSalesSlipNumber);
            var cancelledSlipNumber = _loadedSalesSlipNumber;
            New();
            StatusMessage = $"売上No. {cancelledSlipNumber} を取消しました。";
        }
        catch (SalesOperationException ex)
        {
            StatusMessage = $"取消エラー: {ex.Message}";
        }
        catch (SlipConcurrencyException ex)
        {
            StatusMessage = $"取消エラー: {ex.Message}";
        }
    });

    // ── 枠のみ・使用不可（理由は docs/design_document.md 参照） ──────
    private bool CanUseUnimplementedFeature => false;

    /// <summary>
    /// 印刷（納品書、TODO.md 10-4）。既存伝票を読み込んでいる場合のみ有効
    /// （プレビューモードでも可＝再発行。docs/design_document.md「プレビューは書き込まない」方針の
    /// 明示的な例外。発行日時・発行回数は伝票内容の編集ではなく帳簿外の記録列のため）。
    /// 新規未保存の伝票はここでは印刷できず、保存直後の発行確認（<see cref="PromptAndPrintDeliveryNoteAsync"/>）
    /// で扱う。
    /// </summary>
    private bool CanPrint => _loadedSalesSlipNumber is not null;

    [RelayCommand(CanExecute = nameof(CanPrint))]
    private async Task PrintAsync()
    {
        if (_loadedSalesSlipNumber is null)
        {
            return;
        }

        var note = await PrintDeliveryNoteAsync(_loadedSalesSlipNumber);
        if (note is not null)
        {
            StatusMessage = note;
        }
    }

    /// <summary>保存直後に「納品書を発行しますか？」を確認し、Yesならプレビュー・印刷・発行記録まで行う。</summary>
    private async Task<string?> PromptAndPrintDeliveryNoteAsync(string salesSlipNumber)
    {
        var confirm = MessageBox.Show(
            $"売上No. {salesSlipNumber} の納品書を発行しますか？",
            "bmcs_app", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return null;
        }

        return await PrintDeliveryNoteAsync(salesSlipNumber);
    }

    /// <summary>
    /// 納品書データを取得してプレビューを開き、印刷が成功した場合のみ発行記録
    /// （<see cref="DeliveryNoteService.MarkIssuedAsync"/>）を行う。プレビューはモーダル
    /// （TODO.md 10-3ユーザー確認済み。開いている間は他画面を操作できない）。
    /// </summary>
    private async Task<string?> PrintDeliveryNoteAsync(string salesSlipNumber)
    {
        DeliveryNoteData? data;
        try
        {
            data = await deliveryNoteService.GetAsync(salesSlipNumber);
        }
        catch (DeliveryNoteException ex)
        {
            return $"印刷エラー: {ex.Message}";
        }

        if (data is null)
        {
            return $"売上No. {salesSlipNumber} が見つかりません。";
        }

        var printed = windowService.ShowDialog<ReportPreviewDialog, ReportPreviewDialogViewModel, bool>(
            vm => vm.Initialize(ReportKind.DeliveryNote, $"納品書 {salesSlipNumber}", () => new DeliveryNoteDocumentBuilder(data).Build()));

        if (!printed)
        {
            return null;
        }

        try
        {
            await deliveryNoteService.MarkIssuedAsync(salesSlipNumber);
            return $"納品書を発行しました（売上No. {salesSlipNumber}）。";
        }
        catch (DeliveryNoteException ex)
        {
            return $"発行記録エラー: {ex.Message}";
        }
    }

    /// <summary>既存売上の一覧・検索機能は伝票検索モーダルに統合済みのため、前後移動は当面実装しない。</summary>
    [RelayCommand(CanExecute = nameof(CanUseUnimplementedFeature))]
    private void PrevSlip()
    {
    }

    [RelayCommand(CanExecute = nameof(CanUseUnimplementedFeature))]
    private void NextSlip()
    {
    }
}
