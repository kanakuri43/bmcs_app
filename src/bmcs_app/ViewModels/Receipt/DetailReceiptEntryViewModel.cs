using System.Collections.ObjectModel;
using bmcs_app.Application.Billing;
using bmcs_app.Application.Master;
using bmcs_app.Application.Receipt;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Services;
using bmcs_app.ViewModels.Common;
using bmcs_app.Views.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DetailReceiptEntity = bmcs_app.Domain.Entities.DetailReceipt;

namespace bmcs_app.ViewModels.Receipt;

/// <summary>
/// 明細入金画面（TODO.md 7-4）。都度得意先（内税明細単位）専用。締め得意先（請求単位／伝票単位）の
/// 入金は入金入力画面（TODO.md 7-2）が担う。
///
/// 画面レイアウト・操作方法は旧デモ<c>bmcs_app.LineReceipt</c>を踏襲する（左＝入金登録の明細行、
/// 右＝上段タブ〈売上伝票／明細請求書〉＋下段の選択伝票明細）。ただし充当の粒度はスキーマに
/// 合わせて改めた: 売上伝票タブは売上明細行単位、明細請求書タブは請求書まるごと1行（デモは
/// 請求書タブでも行単位だったが不採用。2026-09-15ユーザー確認。docs/design_document.md 18章）。
/// 金額は常に対象の全額（または残額）で固定・読み取り専用。前受金は無く、充当先が未定の行は
/// 作らない。
///
/// 振込手数料差額の入力（TODO.md 7-3）・既存伝票の訂正／取消（TODO.md 7-5）はこの画面の
/// スコープ外。伝票No.欄で既存の明細入金を読み込んだ場合は読み取り専用表示にする。
/// </summary>
public partial class DetailReceiptEntryViewModel(
    DetailReceiptEntryService detailReceiptEntryService,
    DetailInvoiceService detailInvoiceService,
    CustomerService customerService,
    BankAccountService bankAccountService,
    WindowService windowService) : ViewModelBase
{
    private Customer? _customer;

    public ObservableCollection<BankAccount> BankAccounts { get; } = [];

    public ObservableCollection<ReceiptMethodOption> ReceiptMethodOptions { get; } =
    [
        new(ReceiptMethod.Cash, "現金"),
        new(ReceiptMethod.BankTransfer, "振込"),
        new(ReceiptMethod.Offset, "相殺"),
        // 手形は除外する（detail_receipt は手形期日を保持する列を持たないため。決定3・2026-09-15確定）。
    ];

    public ObservableCollection<DetailReceiptLineViewModel> Lines { get; } = [];

    [ObservableProperty]
    public partial string DetailReceiptNumberQuery { get; set; } = string.Empty;

    /// <summary>既存の明細入金を読み込んだ状態かどうか。真のときは読み取り専用（訂正・取消はTODO.md 7-5）。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddLineCommand))]
    [NotifyPropertyChangedFor(nameof(IsEditable))]
    public partial bool IsExistingLoaded { get; set; }

    /// <summary>ヘッダー・明細の編集可否（<see cref="IsExistingLoaded"/>の否定）。</summary>
    public bool IsEditable => !IsExistingLoaded;

    [ObservableProperty]
    public partial string ReceiptDateText { get; set; } = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy/MM/dd");

    [ObservableProperty]
    public partial string CustomerCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CustomerName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SlipRemarks { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    /// <summary>今回の入金額（明細行の合計）。</summary>
    public decimal ReceiptTotal => Lines.Sum(l => l.Amount);

    // ── 売上伝票タブ ──────────────────────────────────────────
    public ObservableCollection<DetailReceiptSalesCandidate> SalesCandidates { get; } = [];

    public ObservableCollection<DetailReceiptCandidateSlipSummary> CandidateSlips { get; } = [];

    [ObservableProperty]
    public partial DetailReceiptCandidateSlipSummary? SelectedCandidateSlip { get; set; }

    public ObservableCollection<DetailReceiptSalesCandidate> SelectedSlipLines { get; } = [];

    [ObservableProperty]
    public partial DetailReceiptSalesCandidate? SelectedSlipLine { get; set; }

    // ── 明細請求書タブ ────────────────────────────────────────
    public ObservableCollection<DetailReceiptInvoiceCandidate> InvoiceCandidates { get; } = [];

    [ObservableProperty]
    public partial DetailReceiptInvoiceCandidate? SelectedInvoiceCandidate { get; set; }

    /// <summary>選択中の明細請求書の明細（参考表示・読み取り専用。決定1により行単位では取込めない）。</summary>
    public ObservableCollection<DetailInvoiceSalesLineItem> SelectedInvoiceLines { get; } = [];

    // ── 右ペインのタブ選択（0=売上伝票, 1=明細請求書） ─────────
    [ObservableProperty]
    public partial int RightTabIndex { get; set; }

    public bool IsSlipTabActive => RightTabIndex == 0;

    public bool IsInvoiceTabActive => RightTabIndex == 1;

    /// <summary>アクティブなタブで選択されている、取込可能な候補（F2用）。</summary>
    public object? SelectedPayableCandidate => RightTabIndex == 0
        ? (object?)SelectedSlipLine
        : SelectedInvoiceCandidate;

    private bool CanSave => !IsExistingLoaded && _customer is not null && Lines.Count > 0;

    private bool CanEdit => IsEditable;

    private bool CanUseUnimplementedFeature => false;

    [RelayCommand]
    private Task LoadAsync() => RunBusyAsync(async () =>
    {
        var accounts = await bankAccountService.GetBankAccountsAsync();
        BankAccounts.Clear();
        foreach (var account in accounts)
        {
            BankAccounts.Add(account);
        }
    });

    /// <summary>新規（F3）。画面を起動直後の状態に戻す。</summary>
    [RelayCommand]
    private void New()
    {
        ClearForm();
        StatusMessage = "新規明細入金";
        NotifyResetToInitialState();
    }

    [RelayCommand]
    private void OpenCustomerSearch()
    {
        var customer = windowService.ShowDialog<CustomerSearchDialog, CustomerSearchDialogViewModel, Customer>();
        if (customer is not null)
        {
            _ = RunBusyAsync(() => ApplyCustomerAsync(customer));
        }
    }

    [RelayCommand]
    private Task LookupCustomerByCodeAsync() => RunBusyAsync(async () =>
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

        await ApplyCustomerAsync(customer);
    });

    private async Task ApplyCustomerAsync(Customer customer)
    {
        if (customer.TaxUnit != TaxUnit.Line)
        {
            StatusMessage = "締め得意先（請求単位／伝票単位）はこの画面では入金登録できません。入金入力画面をご利用ください。";
            return;
        }

        _customer = customer;
        CustomerCode = customer.CustomerCode;
        CustomerName = customer.CustomerName;

        await LoadCandidatesAsync();

        StatusMessage = $"得意先: {customer.CustomerName}";
        SaveCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadCandidatesAsync()
    {
        SalesCandidates.Clear();
        InvoiceCandidates.Clear();
        if (_customer is null)
        {
            RebuildCandidateSlips();
            return;
        }

        var alreadyOnSlipSales = Lines
            .Where(l => l.TargetType == DetailReceiptTargetType.SalesLine)
            .Select(l => (l.TargetSalesSlipNumber, l.TargetSalesLineNumber))
            .ToHashSet();
        var salesCandidates = await detailReceiptEntryService.GetSalesLineCandidatesAsync(_customer.CustomerCode);
        foreach (var c in salesCandidates.Where(c => !alreadyOnSlipSales.Contains((c.SalesSlipNumber, c.LineNumber))))
        {
            SalesCandidates.Add(c);
        }

        RebuildCandidateSlips();

        var alreadyOnSlipInvoices = Lines
            .Where(l => l.TargetType == DetailReceiptTargetType.DetailInvoice)
            .Select(l => l.TargetDetailInvoiceNumber)
            .ToHashSet();
        var invoiceCandidates = await detailReceiptEntryService.GetDetailInvoiceCandidatesAsync(_customer.CustomerCode);
        foreach (var c in invoiceCandidates.Where(c => !alreadyOnSlipInvoices.Contains(c.DetailInvoiceNumber)))
        {
            InvoiceCandidates.Add(c);
        }

        RefreshBottomPane();
    }

    /// <summary>売上伝票タブの上段リスト（候補を売上No.単位にグルーピング）。</summary>
    private void RebuildCandidateSlips()
    {
        var keepSlipNumber = SelectedCandidateSlip?.SalesSlipNumber;

        var groups = SalesCandidates
            .GroupBy(c => c.SalesSlipNumber)
            .Select(g => new DetailReceiptCandidateSlipSummary(
                g.Key, g.First().SlipDate, g.Count(), g.Sum(c => c.RemainingAmount)))
            .OrderBy(s => s.SlipDate).ThenBy(s => s.SalesSlipNumber)
            .ToList();

        CandidateSlips.Clear();
        foreach (var g in groups)
        {
            CandidateSlips.Add(g);
        }

        SelectedCandidateSlip = keepSlipNumber is null
            ? null
            : CandidateSlips.FirstOrDefault(s => s.SalesSlipNumber == keepSlipNumber);
    }

    partial void OnSelectedCandidateSlipChanged(DetailReceiptCandidateSlipSummary? value) => RefreshSelectedSlipLines();

    partial void OnSelectedSlipLineChanged(DetailReceiptSalesCandidate? value) =>
        OnPropertyChanged(nameof(SelectedPayableCandidate));

    private void RefreshSelectedSlipLines()
    {
        SelectedSlipLines.Clear();
        if (SelectedCandidateSlip is null)
        {
            return;
        }

        foreach (var c in SalesCandidates.Where(c => c.SalesSlipNumber == SelectedCandidateSlip.SalesSlipNumber))
        {
            SelectedSlipLines.Add(c);
        }
    }

    partial void OnSelectedInvoiceCandidateChanged(DetailReceiptInvoiceCandidate? value)
    {
        OnPropertyChanged(nameof(SelectedPayableCandidate));
        _ = RefreshSelectedInvoiceLinesAsync();
    }

    /// <summary>選択中の明細請求書の明細を参考表示する（既存の<see cref="DetailInvoiceService.GetByNumberAsync"/>を再利用）。</summary>
    private async Task RefreshSelectedInvoiceLinesAsync()
    {
        SelectedInvoiceLines.Clear();
        if (SelectedInvoiceCandidate is null)
        {
            return;
        }

        var view = await detailInvoiceService.GetByNumberAsync(SelectedInvoiceCandidate.DetailInvoiceNumber);
        if (view is null)
        {
            return;
        }

        foreach (var line in view.Lines)
        {
            SelectedInvoiceLines.Add(line);
        }
    }

    partial void OnRightTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsSlipTabActive));
        OnPropertyChanged(nameof(IsInvoiceTabActive));
        OnPropertyChanged(nameof(SelectedPayableCandidate));
        RefreshBottomPane();
    }

    private void RefreshBottomPane()
    {
        if (RightTabIndex == 0)
        {
            RefreshSelectedSlipLines();
        }
        else
        {
            _ = RefreshSelectedInvoiceLinesAsync();
        }
    }

    /// <summary>
    /// 候補行を明細入金の明細行へ追加する（F2、または各リストの「入金」ボタン・Enter・ダブルクリック）。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void AddLine(object? candidate)
    {
        switch (candidate)
        {
            case DetailReceiptSalesCandidate sales:
                SalesCandidates.Remove(sales);
                Lines.Add(CreateLineFromSalesCandidate(sales));
                RebuildCandidateSlips();
                RefreshSelectedSlipLines();
                break;

            case DetailReceiptInvoiceCandidate invoice:
                InvoiceCandidates.Remove(invoice);
                Lines.Add(CreateLineFromInvoiceCandidate(invoice));
                break;

            default:
                StatusMessage = "取込む対象を選択してください。";
                return;
        }

        RenumberLines();
        OnPropertyChanged(nameof(ReceiptTotal));
        SaveCommand.NotifyCanExecuteChanged();
        StatusMessage = "明細を追加しました。";
    }

    private DetailReceiptLineViewModel CreateLineFromSalesCandidate(DetailReceiptSalesCandidate c) => new(OnDeleteLine)
    {
        TargetType = DetailReceiptTargetType.SalesLine,
        TargetSalesSlipNumber = c.SalesSlipNumber,
        TargetSalesLineNumber = c.LineNumber,
        TargetDisplay = $"{c.SalesSlipNumber}-{c.LineNumber} {c.ProductName}",
        Amount = c.RemainingAmount,
        SourceSalesCandidate = c,
    };

    private DetailReceiptLineViewModel CreateLineFromInvoiceCandidate(DetailReceiptInvoiceCandidate c) => new(OnDeleteLine)
    {
        TargetType = DetailReceiptTargetType.DetailInvoice,
        TargetDetailInvoiceNumber = c.DetailInvoiceNumber,
        TargetDisplay = $"{c.DetailInvoiceNumber}（請求書一括）",
        Amount = c.TotalAmount,
        SourceInvoiceCandidate = c,
    };

    private void OnDeleteLine(DetailReceiptLineViewModel line)
    {
        Lines.Remove(line);
        RenumberLines();

        if (line.SourceSalesCandidate is not null)
        {
            SalesCandidates.Add(line.SourceSalesCandidate);
            RebuildCandidateSlips();
            RefreshSelectedSlipLines();
        }
        else if (line.SourceInvoiceCandidate is not null)
        {
            InvoiceCandidates.Add(line.SourceInvoiceCandidate);
        }

        OnPropertyChanged(nameof(ReceiptTotal));
        SaveCommand.NotifyCanExecuteChanged();
        StatusMessage = "明細を削除しました。";
    }

    private void RenumberLines()
    {
        for (var i = 0; i < Lines.Count; i++)
        {
            Lines[i].LineNumber = (short)(i + 1);
        }
    }

    /// <summary>
    /// 入金No.欄で Return を押したときの挙動（docs/product-spec.md UI/UX節「ジャーナル系画面の
    /// 伝票No入力欄の挙動」）。空欄なら新規登録モードとして次項目（入金日付）へフォーカス移動する
    /// のみ。入力済みなら既存の明細入金No.で直接読み込む（読み取り専用表示。訂正・取消はTODO.md 7-5）。
    /// </summary>
    [RelayCommand]
    private Task LookupAsync() => RunBusyAsync(async () =>
    {
        var number = DetailReceiptNumberQuery.Trim();
        if (string.IsNullOrWhiteSpace(number))
        {
            RequestFocus("ReceiptDate");
            return;
        }

        var lines = await detailReceiptEntryService.GetByNumberAsync(number);
        if (lines.Count == 0)
        {
            StatusMessage = $"明細入金No.「{number}」は見つかりません。";
            return;
        }

        ApplyExisting(number, lines);
    });

    /// <summary>明細入金検索モーダルを開く（<c>Space</c>）。選択した番号は <see cref="LookupAsync"/> と同じ経路で読み込む。</summary>
    [RelayCommand]
    private void OpenDetailReceiptSearch()
    {
        var detailReceiptNumber = windowService.ShowDialog<SlipSearchDialog, SlipSearchDialogViewModel, string>(
            vm => vm.Target = SlipSearchTarget.DetailReceipt);

        if (!string.IsNullOrWhiteSpace(detailReceiptNumber))
        {
            DetailReceiptNumberQuery = detailReceiptNumber;
            _ = LookupAsync();
        }
    }

    /// <summary>保存（F10）。</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveAsync() => RunBusyAsync(async () =>
    {
        if (_customer is null)
        {
            return;
        }

        if (!DateOnly.TryParseExact(ReceiptDateText, "yyyy/MM/dd", out var receiptDate))
        {
            StatusMessage = "入金日付の形式が不正です（yyyy/MM/dd）。";
            return;
        }

        var lineInputs = Lines.Select(l => new DetailReceiptLineInput(
            l.TargetType,
            l.TargetSalesSlipNumber,
            l.TargetSalesLineNumber,
            l.TargetDetailInvoiceNumber,
            l.ReceiptMethod,
            l.BankAccountCode,
            string.IsNullOrWhiteSpace(l.LineRemarks) ? null : l.LineRemarks)).ToList();

        try
        {
            var detailReceiptNumber = await detailReceiptEntryService.SaveNewAsync(
                _customer.CustomerCode,
                receiptDate,
                string.IsNullOrWhiteSpace(SlipRemarks) ? null : SlipRemarks,
                lineInputs);

            // 登録成功後は画面を起動直後の状態へ戻す（docs/product-spec.md UI/UX節「登録後のリセット」）。
            ClearForm();
            StatusMessage = $"明細入金No. {detailReceiptNumber} を登録しました。";
            NotifyResetToInitialState();
        }
        catch (DetailReceiptEntryException ex)
        {
            StatusMessage = $"登録エラー: {ex.Message}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"登録エラー: {ex.Message}";
        }
    });

    /// <summary>明細入金の訂正・取消はTODO.md 7-5で実装する（枠のみ・使用不可）。</summary>
    [RelayCommand(CanExecute = nameof(CanUseUnimplementedFeature))]
    private void DeleteSlip()
    {
    }

    private void ApplyExisting(string detailReceiptNumber, List<DetailReceiptEntity> lines)
    {
        var header = lines[0];

        DetailReceiptNumberQuery = detailReceiptNumber;
        ReceiptDateText = header.ReceiptDate.ToString("yyyy/MM/dd");
        CustomerCode = header.CustomerCode;
        CustomerName = header.CustomerName;
        SlipRemarks = header.SlipRemarks ?? string.Empty;
        _customer = null;

        ClearLines();
        SalesCandidates.Clear();
        InvoiceCandidates.Clear();
        RebuildCandidateSlips();
        SelectedInvoiceLines.Clear();

        foreach (var line in lines)
        {
            var display = line.TargetType == DetailReceiptTargetType.SalesLine
                ? $"{line.TargetSalesSlipNumber}-{line.TargetSalesLineNumber}"
                : $"{line.TargetDetailInvoiceNumber}（請求書一括）";

            Lines.Add(new DetailReceiptLineViewModel(OnDeleteLine)
            {
                LineNumber = line.LineNumber,
                TargetType = line.TargetType,
                TargetSalesSlipNumber = line.TargetSalesSlipNumber,
                TargetSalesLineNumber = line.TargetSalesLineNumber,
                TargetDetailInvoiceNumber = line.TargetDetailInvoiceNumber,
                TargetDisplay = display,
                ReceiptMethod = line.ReceiptMethod,
                BankAccountCode = line.BankAccountCode,
                Amount = line.AllocatedAmount,
                LineRemarks = line.LineRemarks ?? string.Empty,
            });
        }

        OnPropertyChanged(nameof(ReceiptTotal));
        IsExistingLoaded = true;
        SaveCommand.NotifyCanExecuteChanged();
        StatusMessage = $"明細入金No. {detailReceiptNumber} を読み込みました（読み取り専用。訂正・取消はTODO.md 7-5で対応予定）。";
    }

    private void ClearForm()
    {
        DetailReceiptNumberQuery = string.Empty;
        ReceiptDateText = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy/MM/dd");
        CustomerCode = string.Empty;
        CustomerName = string.Empty;
        SlipRemarks = string.Empty;
        _customer = null;

        ClearLines();
        SalesCandidates.Clear();
        InvoiceCandidates.Clear();
        SelectedSlipLines.Clear();
        SelectedInvoiceLines.Clear();
        SelectedCandidateSlip = null;
        SelectedInvoiceCandidate = null;
        SelectedSlipLine = null;
        RightTabIndex = 0;
        RebuildCandidateSlips();

        OnPropertyChanged(nameof(ReceiptTotal));
        IsExistingLoaded = false;
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void ClearLines() => Lines.Clear();
}

// ReceiptMethodOption は ReceiptEntryViewModel.cs（同名前空間）で定義済みのものを共用する。

/// <summary>売上伝票タブの上段リストの1行（同一売上No.の候補明細をまとめたサマリ）。</summary>
public sealed record DetailReceiptCandidateSlipSummary(
    string SalesSlipNumber, DateOnly SlipDate, int LineCount, decimal TotalAmount);
