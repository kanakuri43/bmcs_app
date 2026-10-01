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
/// 受注入力画面（TODO.md 4-3・5-3・4-6）。旧プロトタイプ（bmcs_app.Order）のレイアウトを再現する。
/// 新規登録に加え、既存受注の読み込み・直接修正・中止（F8）を扱う（TODO.md 4-6で訂正保存を追加。
/// 4-4で先行実装した <see cref="OrderStatusService.CancelSlipAsync"/> の画面配線は5-3で追加済み）。
/// 直接修正できるのは未売上（<see cref="OrderStatus.NotSold"/>）の伝票のみ（<see cref="OrderEditLockEvaluator"/>）。
/// 前後移動は、このプロジェクトの機能にまだ存在しないため、枠のみ用意し無効化している
/// （詳細は docs/design_document.md）。
/// </summary>
public partial class OrderEntryViewModel(
    ProductService productService,
    CustomerService customerService,
    EmployeeService employeeService,
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

    /// <summary>
    /// 得意先マスタに設定された自社の営業担当名（<see cref="Customer.SalesEmployeeCode"/>）。
    /// 右上ステータス欄に「担当：〇〇〇〇」として表示する。未設定・該当社員なしの場合は空欄。
    /// 伝票（<see cref="OrderSlip"/>）自体の担当者（下記 <see cref="EmployeeCode"/>）とは別概念。
    /// </summary>
    [ObservableProperty]
    public partial string SalesRepName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial DateTime? OrderDate { get; set; } = DateTime.Today;

    /// <summary>
    /// 受注No.欄。採番は保存時にトランザクション内で行うため、新規時は空欄（Watermarkで案内）。
    /// 番号を直接入力して <c>Return</c> で読み込む、または <c>Space</c> で検索モーダルを開ける。
    /// 空欄のまま <c>Return</c> は新規登録モードとして次項目へフォーカス移動する
    /// （docs/product-spec.md UI/UX節「ジャーナル系画面の伝票No入力欄の挙動」）。
    /// </summary>
    [ObservableProperty]
    public partial string OrderSlipNumberDisplay { get; set; } = string.Empty;

    /// <summary>
    /// 担当者コード・名称。伝票自体の担当者（<see cref="OrderSlip.EmployeeCode"/>）で、任意項目。
    /// 得意先選択時に得意先の営業担当（<see cref="Customer.SalesEmployeeCode"/>）が初期値として入るが、修正できる。
    /// </summary>
    [ObservableProperty]
    public partial string EmployeeCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EmployeeName { get; set; } = string.Empty;

    /// <summary>伝票摘要。同一伝票の全行に複写して保存する（<see cref="OrderSlip.SlipRemarks"/>）。</summary>
    [ObservableProperty]
    public partial string SlipRemarks { get; set; } = string.Empty;

    /// <summary>社内摘要。画面表示のみで納品書には印字しない（<see cref="OrderSlip.InternalRemarks"/>）。</summary>
    [ObservableProperty]
    public partial string InternalRemarks { get; set; } = string.Empty;

    /// <summary>
    /// 新規登録済みかどうか。新規モードでの二重登録防止にのみ使う
    /// （訂正モードでは <see cref="_loadedOrderSlipNumber"/> と <see cref="IsEditLocked"/> で判定する）。
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool IsSaved { get; set; }

    /// <summary>
    /// 修正不可かどうか（未売上でない受注を読み込んだ場合。TODO.md 4-6・<see cref="OrderEditLockEvaluator"/>）。
    /// ViewModelは判定せず、Domain純粋関数の結果をそのまま表示・反映するだけにする
    /// （docs/architecture.md 5章）。
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyPropertyChangedFor(nameof(IsEditable))]
    public partial bool IsEditLocked { get; set; }

    /// <summary>
    /// 保存が例外で失敗した後、再読込まで保存を封じるフラグ。ミューテーション後に例外が発生すると
    /// ChangeTrackerが汚れたまま残り、同じ画面から再保存すると行が静かに論理削除される
    /// 潜在バグ（TODO.md 4-6レビューで発見）への対策。自動マージ・後勝ちの上書きは行わない
    /// （docs/architecture.md 9章）ため、操作面でも再読込を強制する。
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool IsReloadRequired { get; set; }

    /// <summary>
    /// 訂正モード（既存受注を読み込んだ）かどうか。得意先の変更は <see cref="OrderService.UpdateAsync"/>
    /// が無視するため、画面側で得意先コードの変更操作自体を封じる（黙って無視される潜在的な不整合を
    /// UIレベルで防ぐ。TODO.md 4-6レビュー）。
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCustomerSearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(LookupCustomerByCodeCommand))]
    [NotifyPropertyChangedFor(nameof(IsCustomerCodeReadOnly))]
    public partial bool IsCorrectionMode { get; set; }

    /// <summary>得意先コード欄の <c>IsReadOnly</c> バインディング用。得意先名欄は対象外（C-9の宛名都度書き換え）。</summary>
    public bool IsCustomerCodeReadOnly => IsCorrectionMode;

    /// <summary>ヘッダー入力欄・明細グリッドの <c>IsEnabled</c> バインディング用。修正不可の受注は読取専用にする。</summary>
    public bool IsEditable => !IsEditLocked;

    private bool CanSave => _loadedOrderSlipNumber is null ? !IsSaved : !IsEditLocked && !IsReloadRequired;

    private bool CanChangeCustomer => !IsCorrectionMode;

    private bool CanEditEmployee => true;

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

    /// <summary>
    /// 旧プロトタイプと同じく、外税得意先（Invoice/Slip）では消費税計と同値を「（外税）」欄に表示する。
    /// 内税明細単位（Line）は税額を明細内に含めて計算するため外税欄には表示しない。
    /// </summary>
    public decimal ExternalTaxTotal => _customer?.TaxUnit == TaxUnit.Line ? 0m : TaxTotal;

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

    [RelayCommand(CanExecute = nameof(CanChangeCustomer))]
    private async Task OpenCustomerSearchAsync()
    {
        var customer = windowService.ShowDialog<CustomerSearchDialog, CustomerSearchDialogViewModel, Customer>();
        if (customer is not null)
        {
            await ApplyCustomerAsync(customer);
        }
    }

    [RelayCommand(CanExecute = nameof(CanChangeCustomer))]
    private async Task LookupCustomerByCodeAsync()
    {
        if (string.IsNullOrWhiteSpace(CustomerCode))
        {
            return;
        }

        if (_customer?.CustomerCode == CustomerCode)
        {
            // すでに確定済みの得意先（得意先名を手入力で上書き済みの場合を含む。C-9）と同じコード
            // なら再取得しない。マスタを読み直すと上書きが失われるため。
            // ただしEnterでの通常のフォーカス送りは維持する（ApplyCustomerAsyncと同じ送り先）。
            RequestFocus("EmployeeCode");
            return;
        }

        var customer = await customerService.GetByCodeAsync(CustomerCode);
        if (customer is null)
        {
            StatusMessage = $"得意先コード「{CustomerCode}」が見つかりません。";
            return;
        }

        await ApplyCustomerAsync(customer);
    }

    private async Task ApplyCustomerAsync(Customer customer)
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
        SalesRepName = await ResolveSalesRepNameAsync(customer.SalesEmployeeCode);
        await ApplyDefaultEmployeeAsync(customer.SalesEmployeeCode);

        foreach (var line in Lines)
        {
            line.RoundingType = customer.RoundingType;
            line.RaiseAmountChanged();
        }

        RaiseTotalsChanged();
        StatusMessage = $"得意先: {customer.CustomerName}";
        RequestFocus("EmployeeCode");
    }

    /// <summary>
    /// 得意先マスタの<see cref="Customer.SalesEmployeeCode"/>から自社の営業担当名を解決する。
    /// 未設定、または社員マスタに該当なしの場合は空欄（ステータス欄には「担当：」行自体を出さない）。
    /// </summary>
    private async Task<string> ResolveSalesRepNameAsync(string? salesEmployeeCode)
    {
        if (string.IsNullOrWhiteSpace(salesEmployeeCode))
        {
            return string.Empty;
        }

        var employee = await employeeService.GetByCodeAsync(salesEmployeeCode);
        return employee?.EmployeeName ?? string.Empty;
    }

    // ── 伝票の担当者（得意先の営業担当とは別） ─────────────────────────────────
    /// <summary>社員マスタで確定した担当者コード。画面の <see cref="EmployeeCode"/> 欄の文字と一致している間だけ有効。</summary>
    private string? _confirmedEmployeeCode;

    partial void OnEmployeeCodeChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            _confirmedEmployeeCode = null;
            EmployeeName = string.Empty;
        }
        else if (value != _confirmedEmployeeCode)
        {
            EmployeeName = string.Empty;
        }
    }

    private void ApplyEmployee(string? employeeCode, string? employeeName)
    {
        _confirmedEmployeeCode = string.IsNullOrWhiteSpace(employeeCode) ? null : employeeCode;
        EmployeeCode = _confirmedEmployeeCode ?? string.Empty;
        EmployeeName = _confirmedEmployeeCode is null ? string.Empty : employeeName ?? string.Empty;
    }

    /// <summary>得意先の営業担当を担当者の初期値にする。未設定・無効化済み・該当なしの場合は空にする。</summary>
    private async Task ApplyDefaultEmployeeAsync(string? salesEmployeeCode)
    {
        var employee = string.IsNullOrWhiteSpace(salesEmployeeCode)
            ? null
            : await employeeService.GetActiveByCodeAsync(salesEmployeeCode);
        ApplyEmployee(employee?.EmployeeCode, employee?.EmployeeName);
    }

    /// <summary>既存伝票の担当者を復元する。無効化された社員でも、保存済みの値はそのまま表示する。</summary>
    private async Task RestoreEmployeeAsync(string? employeeCode)
    {
        var employee = string.IsNullOrWhiteSpace(employeeCode)
            ? null
            : await employeeService.GetByCodeAsync(employeeCode);
        ApplyEmployee(employeeCode, employee?.EmployeeName);
    }

    [RelayCommand(CanExecute = nameof(CanEditEmployee))]
    private void OpenEmployeeSearch()
    {
        var employee = windowService.ShowDialog<EmployeeMasterSearchDialog, EmployeeMasterSearchDialogViewModel, Employee>();
        if (employee is not null)
        {
            ApplyEmployee(employee.EmployeeCode, employee.EmployeeName);
            RequestFocus("SlipRemarks");
        }
    }

    [RelayCommand(CanExecute = nameof(CanEditEmployee))]
    private async Task LookupEmployeeByCodeAsync()
    {
        if (string.IsNullOrWhiteSpace(EmployeeCode) || EmployeeCode == _confirmedEmployeeCode)
        {
            // 担当者は任意。空欄、または確定済みのコードのままEnterで摘要へ進む。
            RequestFocus("SlipRemarks");
            return;
        }

        var employee = await employeeService.GetActiveByCodeAsync(EmployeeCode.Trim());
        if (employee is null)
        {
            StatusMessage = $"担当者コード「{EmployeeCode}」が見つかりません。";
            return;
        }

        ApplyEmployee(employee.EmployeeCode, employee.EmployeeName);
        RequestFocus("SlipRemarks");
    }

    /// <summary>保存前に、Enterで確定していない担当者コードを社員マスタで確定する。見つからなければ <c>false</c>。</summary>
    private async Task<bool> EnsureEmployeeConfirmedAsync()
    {
        if (string.IsNullOrWhiteSpace(EmployeeCode) || EmployeeCode == _confirmedEmployeeCode)
        {
            return true;
        }

        var employee = await employeeService.GetActiveByCodeAsync(EmployeeCode.Trim());
        if (employee is null)
        {
            StatusMessage = $"担当者コード「{EmployeeCode}」が見つかりません。";
            return false;
        }

        ApplyEmployee(employee.EmployeeCode, employee.EmployeeName);
        return true;
    }

    // ── 明細行 ────────────────────────────────────────────────
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
        var orderDate = OrderDate is { } orderDateValue ? DateOnly.FromDateTime(orderDateValue) : DateOnly.FromDateTime(DateTime.Today);
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

    /// <summary>行の購読解除・全消去（New・訂正読込の前処理で共用）。</summary>
    private void ClearLines()
    {
        foreach (var line in Lines)
        {
            line.PropertyChanged -= OnLinePropertyChanged;
        }

        Lines.Clear();
    }

    // ── 新規 ─────────────────────────────────────────────────
    [RelayCommand]
    private void New()
    {
        _customer = null;
        _loadedOrderSlipNumber = null;
        _loadedLineNumbers = [];
        SaveCommand.NotifyCanExecuteChanged();
        DeleteSlipCommand.NotifyCanExecuteChanged();
        CustomerCode = string.Empty;
        CustomerName = string.Empty;
        CustomerTaxUnitDisplay = string.Empty;
        SalesRepName = string.Empty;
        ApplyEmployee(null, null);
        OrderDate = DateTime.Today;
        OrderSlipNumberDisplay = string.Empty;
        SlipRemarks = string.Empty;
        InternalRemarks = string.Empty;
        OrderStatus = OrderStatus.NotSold;
        IsSaved = false;
        IsEditLocked = false;
        IsReloadRequired = false;
        IsCorrectionMode = false;

        ClearLines();
        Lines.Add(CreateLine());
        RenumberLines();
        StatusMessage = "新規受注";
    }

    // ── 過去伝票の複写（売上入力5-5の受注版） ──────────────────
    [RelayCommand]
    private Task CopyFromPastSlipAsync() => RunBusyAsync(async () =>
    {
        var sourceOrderSlipNumber = windowService.ShowDialog<SlipSearchDialog, SlipSearchDialogViewModel, string>(
            vm =>
            {
                vm.Target = SlipSearchTarget.Order;
                vm.IncludeUnavailableOrders = true;
            });

        if (string.IsNullOrWhiteSpace(sourceOrderSlipNumber))
        {
            return;
        }

        var sourceLines = await orderQueryService.GetSlipAsync(sourceOrderSlipNumber);
        if (sourceLines.Count == 0)
        {
            StatusMessage = $"受注No.「{sourceOrderSlipNumber}」が見つかりません。";
            return;
        }

        var customer = await customerService.GetByCodeAsync(sourceLines[0].CustomerCode);
        if (customer is null)
        {
            StatusMessage = "複写元の得意先が見つかりません。";
            return;
        }

        New();
        await ApplyCustomerAsync(customer);
        CustomerName = sourceLines[0].CustomerName;
        if (!string.IsNullOrWhiteSpace(sourceLines[0].EmployeeCode)
            && await employeeService.GetActiveByCodeAsync(sourceLines[0].EmployeeCode!) is { } sourceEmployee)
        {
            ApplyEmployee(sourceEmployee.EmployeeCode, sourceEmployee.EmployeeName);
        }

        var orderDate = OrderDate is { } orderDateValue ? DateOnly.FromDateTime(orderDateValue) : DateOnly.FromDateTime(DateTime.Today);
        ClearLines();
        foreach (var source in sourceLines.OrderBy(l => l.LineNumber))
        {
            var line = CreateLine();
            var taxRate = TaxRateResolver.ResolveRate(_taxRateMasters, orderDate, source.TaxCategory);

            line.ProductCode = source.ProductCode;
            line.ProductName = source.ProductName;
            line.Specification = source.Specification;
            line.UnitName = source.UnitName;
            line.Quantity = source.OrderQuantity;
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
        InternalRemarks = sourceLines[0].InternalRemarks ?? string.Empty;
        EnsureTrailingBlankLine();
        RenumberLines();
        StatusMessage = $"受注No. {sourceOrderSlipNumber} を複写しました（新規登録として保存されます）。";
    });

    // ── 既存受注の読み込み・修正・中止（TODO.md 5-3・4-6） ─────
    [RelayCommand]
    private void OpenOrderSlipSearch()
    {
        // 修正できない受注（売上完了・中止済み）も閲覧目的で探せるようにする（2026-09-16確定）。
        // 売上入力画面の受注No.検索（売上化できる受注のみ）とは異なる挙動。
        var orderSlipNumber = windowService.ShowDialog<SlipSearchDialog, SlipSearchDialogViewModel, string>(
            vm =>
            {
                vm.Target = SlipSearchTarget.Order;
                vm.IncludeUnavailableOrders = true;
            });

        if (!string.IsNullOrWhiteSpace(orderSlipNumber))
        {
            _ = RunBusyAsync(() => LoadOrderForCorrectionAsync(orderSlipNumber));
        }
    }

    /// <summary>
    /// 受注No.欄で Return を押したときの挙動。空欄なら新規登録モードとして次項目（受注日付）へ
    /// フォーカス移動するのみ。入力済みなら既存受注を読み込む
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

        await LoadOrderForCorrectionAsync(OrderSlipNumberDisplay);
    });

    /// <summary>
    /// 既存受注を読み込む。未売上の伝票は直接修正・保存できる（TODO.md 4-6）。
    /// 一部売上・売上完了・中止済みの伝票は読み込めるが修正できない（<see cref="OrderEditLockEvaluator"/>）。
    /// 例外は呼び出し元がfire-and-forget（<see cref="OpenOrderSlipSearch"/>）でも握れるよう、ここで捕まえる。
    /// </summary>
    private async Task LoadOrderForCorrectionAsync(string orderSlipNumber)
    {
        if (string.IsNullOrWhiteSpace(orderSlipNumber))
        {
            return;
        }

        try
        {
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

            var lockResult = OrderEditLockEvaluator.Evaluate(sourceLines);

            _customer = customer;
            _loadedOrderSlipNumber = orderSlipNumber;
            _loadedLineNumbers = sourceLines.Select(l => l.LineNumber).ToList();
            SaveCommand.NotifyCanExecuteChanged();
            DeleteSlipCommand.NotifyCanExecuteChanged();

            CustomerCode = customer.CustomerCode;
            CustomerName = sourceLines[0].CustomerName;
            CustomerTaxUnitDisplay = customer.TaxUnit switch
            {
                TaxUnit.Invoice => "請求単位",
                TaxUnit.Slip => "伝票単位",
                TaxUnit.Line => "内税明細単位",
                _ => customer.TaxUnit.ToString(),
            };
            SalesRepName = await ResolveSalesRepNameAsync(customer.SalesEmployeeCode);
            await RestoreEmployeeAsync(sourceLines[0].EmployeeCode);
            OrderDate = sourceLines[0].OrderDate.ToDateTime(TimeOnly.MinValue);
            OrderSlipNumberDisplay = orderSlipNumber;
            SlipRemarks = sourceLines[0].SlipRemarks ?? string.Empty;
            InternalRemarks = sourceLines[0].InternalRemarks ?? string.Empty;
            OrderStatus = sourceLines.Any(l => l.OrderStatus == OrderStatus.Cancelled)
                ? OrderStatus.Cancelled
                : sourceLines.All(l => l.OrderStatus == OrderStatus.FullySold)
                    ? OrderStatus.FullySold
                    : sourceLines.Any(l => l.OrderStatus == OrderStatus.PartiallySold)
                        ? OrderStatus.PartiallySold
                        : OrderStatus.NotSold;
            IsSaved = false;
            IsEditLocked = lockResult.IsLocked;
            IsReloadRequired = false;
            IsCorrectionMode = true;

            ClearLines();
            foreach (var source in sourceLines.OrderBy(l => l.LineNumber))
            {
                var line = CreateLine();
                line.PersistedLineNumber = source.LineNumber;
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

            EnsureTrailingBlankLine();
            RenumberLines();
            RaiseTotalsChanged();

            StatusMessage = lockResult.IsLocked
                ? $"受注No. {orderSlipNumber} を読み込みました（修正不可: {lockResult.Reason}）"
                : $"受注No. {orderSlipNumber} を読み込みました。";
            RequestFocus("OrderDate");
        }
        catch (Exception ex)
        {
            StatusMessage = $"読込エラー: {ex.Message}";
        }
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

        if (OrderDate is not { } orderDateValue)
        {
            StatusMessage = "受注日付を入力してください。";
            return;
        }

        if (!await EnsureEmployeeConfirmedAsync())
        {
            RequestFocus("EmployeeCode");
            return;
        }

        var orderDate = DateOnly.FromDateTime(orderDateValue);

        try
        {
            if (_loadedOrderSlipNumber is null)
            {
                await SaveNewAsync(nonBlankLines, orderDate);
            }
            else
            {
                await SaveCorrectionAsync(nonBlankLines, orderDate);
            }
        }
        catch (OrderOperationException ex)
        {
            HandleSaveFailure(ex);
        }
        catch (SlipConcurrencyException ex)
        {
            HandleSaveFailure(ex);
        }
        catch (Exception ex)
        {
            HandleSaveFailure(ex);
        }
    });

    /// <summary>
    /// 保存失敗時の共通処理。訂正モード中の失敗は、ミューテーション後にChangeTrackerが汚れたまま
    /// 残る可能性があるため、再読込までSaveを封じる（TODO.md 4-6レビューで発見した潜在バグの対策）。
    /// </summary>
    private void HandleSaveFailure(Exception ex)
    {
        if (_loadedOrderSlipNumber is not null)
        {
            IsReloadRequired = true;
        }

        StatusMessage = $"保存エラー: {ex.Message}";
    }

    private async Task SaveNewAsync(List<SlipLineViewModel> nonBlankLines, DateOnly orderDate)
    {
        var now = DateTime.Now;
        var slipRemarks = string.IsNullOrWhiteSpace(SlipRemarks) ? null : SlipRemarks;
        var internalRemarks = string.IsNullOrWhiteSpace(InternalRemarks) ? null : InternalRemarks;
        var entities = nonBlankLines.Select((line, index) => BuildEntity(
            orderSlipNumber: string.Empty, // OrderService.CreateAsync がトランザクション内の採番結果で上書きする
            lineNumber: (short)(index + 1),
            orderDate: orderDate,
            slipRemarks: slipRemarks,
            internalRemarks: internalRemarks,
            line: line,
            now: now)).ToList();

        var orderSlipNumber = await orderService.CreateAsync(entities);

        // 登録後は画面を起動直後の状態へ戻す（docs/product-spec.md UI/UX節「登録後のリセット」）。
        New();
        StatusMessage = $"登録しました。受注No. {orderSlipNumber}";
        NotifyResetToInitialState();
    }

    private async Task SaveCorrectionAsync(List<SlipLineViewModel> nonBlankLines, DateOnly orderDate)
    {
        var now = DateTime.Now;
        var slipRemarks = string.IsNullOrWhiteSpace(SlipRemarks) ? null : SlipRemarks;
        var internalRemarks = string.IsNullOrWhiteSpace(InternalRemarks) ? null : InternalRemarks;
        var entities = nonBlankLines.Select(line => BuildEntity(
            orderSlipNumber: _loadedOrderSlipNumber!,
            lineNumber: line.PersistedLineNumber ?? 0,
            orderDate: orderDate,
            slipRemarks: slipRemarks,
            internalRemarks: internalRemarks,
            line: line,
            now: now)).ToList();

        var correctedOrderSlipNumber = _loadedOrderSlipNumber!;
        await orderService.UpdateAsync(correctedOrderSlipNumber, entities, _loadedLineNumbers);

        // 登録後は画面を起動直後の状態へ戻す（docs/product-spec.md UI/UX節「登録後のリセット」）。
        // 新規登録と挙動を揃え、訂正保存だけ伝票を表示し続ける例外を作らない。
        New();
        StatusMessage = $"訂正しました。受注No. {correctedOrderSlipNumber}";
        NotifyResetToInitialState();
    }

    private OrderSlip BuildEntity(
        string orderSlipNumber, short lineNumber, DateOnly orderDate, string? slipRemarks, string? internalRemarks, SlipLineViewModel line, DateTime now) => new()
    {
        OrderSlipNumber = orderSlipNumber,
        LineNumber = lineNumber,
        OrderDate = orderDate,
        CustomerCode = _customer!.CustomerCode,
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
        InternalRemarks = internalRemarks, // 社内摘要も同様に全行へ複写する
        EmployeeCode = _confirmedEmployeeCode, // 担当者も伝票単位の値として全行へ複写する
        LineRemarks = string.IsNullOrWhiteSpace(line.LineRemarks) ? null : line.LineRemarks,
        CreatedBy = string.Empty, // OrderService が現在の社員コードで上書きする（新規のみ）
        CreatedAt = now,
        UpdatedBy = string.Empty,
        UpdatedAt = now,
    };

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
