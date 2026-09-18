using System.Collections.ObjectModel;
using bmcs_app.Application.Billing;
using bmcs_app.Application.Common;
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
/// 明細入金画面（TODO.md 7-4・7-5）。都度得意先（内税明細単位）専用。締め得意先（請求単位／伝票単位）の
/// 入金は入金入力画面（TODO.md 7-2）が担う。
///
/// 画面レイアウト・操作方法は旧デモ<c>bmcs_app.LineReceipt</c>を踏襲する（左＝入金登録の明細行、
/// 右＝上段タブ〈売上伝票／明細請求書〉＋下段の選択伝票明細）。ただし充当の粒度はスキーマに
/// 合わせて改めた: 売上伝票タブは売上明細行単位、明細請求書タブは請求書まるごと1行（デモは
/// 請求書タブでも行単位だったが不採用。2026-09-15ユーザー確認。docs/design_document.md 18章）。
/// 金額は常に対象の全額（または残額）で固定・読み取り専用。前受金は無く、充当先が未定の行は
/// 作らない。
///
/// 振込手数料差額の入力（TODO.md 7-3）はこの画面のスコープ外。既存伝票の訂正・取消（TODO.md 7-5）は
/// 伝票No.欄で読み込んだ後、入金方法・入金先口座・行摘要・伝票摘要・入金日付の変更と行の削除のみ
/// 保存(F10)＝訂正として扱う（<see cref="DetailReceiptEntryService"/>のdoc comment参照。**充当先の
/// 追加はできない**ため、読込時は右ペインの候補一覧を表示しない）。取消(F8)＝取消。
/// </summary>
public partial class DetailReceiptEntryViewModel(
    DetailReceiptEntryService detailReceiptEntryService,
    DetailInvoiceService detailInvoiceService,
    CustomerService customerService,
    BankAccountService bankAccountService,
    DepositMethodService depositMethodService,
    WindowService windowService) : ViewModelBase
{
    private Customer? _customer;
    private string? _loadedDetailReceiptNumber;
    private IReadOnlyList<short> _loadedLineNumbers = [];

    /// <summary>
    /// 伝票プレビュー（TODO.md 8-3）用の入口。得意先元帳からの表示専用で開くとき、
    /// <see cref="Services.WindowService.Show{TWindow, TViewModel}"/> の <c>configure</c> から
    /// ウィンドウ表示前に一度だけ設定する。値は以後変化しないため<c>[ObservableProperty]</c>は使わない。
    /// </summary>
    public string? PreviewSlipNumber { get; set; }

    /// <summary>プレビュー表示中かどうか。</summary>
    public bool IsPreviewMode => PreviewSlipNumber is not null;

    /// <summary>ウィンドウタイトル（TODO.md 8-3）。</summary>
    public string WindowTitle => IsPreviewMode ? "bmcs_app - 明細入金（プレビュー・編集不可）" : "bmcs_app - 明細入金";

    public ObservableCollection<BankAccount> BankAccounts { get; } = [];

    /// <summary>
    /// 手形期日を要する入金方法は除外する（detail_receipt は手形期日を保持する列を持たないため。
    /// 決定3・2026-09-15確定。旧enumのハードコード除外を2026-09-18にマスタのフラグ駆動へ移行）。
    /// </summary>
    public ObservableCollection<DepositMethod> DepositMethods { get; } = [];

    public ObservableCollection<DetailReceiptLineViewModel> Lines { get; } = [];

    [ObservableProperty]
    public partial string DetailReceiptNumberQuery { get; set; } = string.Empty;

    /// <summary>
    /// 既存の明細入金を読み込んだ状態かどうか。真の間は得意先コードを変更できない
    /// （<see cref="IsEditLocked"/>と異なり、ロックの有無に関わらず得意先の付け替えは常に禁止する）。
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSlipCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddLineCommand))]
    [NotifyPropertyChangedFor(nameof(IsEditable))]
    [NotifyPropertyChangedFor(nameof(IsHeaderLocked))]
    public partial bool IsExistingLoaded { get; set; }

    /// <summary>
    /// 編集ロック中かどうか（<see cref="DetailReceiptEntryService.EvaluateEditLockAsync"/>の結果。
    /// TODO.md 7-5）。ViewModelは判定せず、結果をそのまま表示・反映するだけにする
    /// （docs/architecture.md 5章）。
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSlipCommand))]
    [NotifyPropertyChangedFor(nameof(IsEditable))]
    [NotifyPropertyChangedFor(nameof(IsHeaderLocked))]
    public partial bool IsEditLocked { get; set; }

    /// <summary>
    /// ヘッダー（入金日付・摘要）・明細行（入金方法・入金先口座・行摘要・削除）の編集可否。
    /// 新規登録時は常に編集可能、既存読込時は編集ロックされていない場合のみ編集可能（＝訂正できる）。
    /// </summary>
    public bool IsEditable => !IsPreviewMode && (!IsExistingLoaded || !IsEditLocked);

    /// <summary>入金日付・摘要のIsReadOnlyバインディング用（得意先コードは<see cref="IsExistingLoaded"/>を直接使う）。</summary>
    public bool IsHeaderLocked => !IsEditable;

    private bool CanEdit => !IsPreviewMode;

    [ObservableProperty]
    public partial DateTime? ReceiptDate { get; set; } = DateTime.Today;

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

    private bool CanSave => IsEditable && _customer is not null && Lines.Count > 0;

    /// <summary>
    /// 候補（売上伝票・明細請求書）から新しい充当先を追加できるかどうか（F2）。新規登録時のみ許可し、
    /// 訂正モードでは編集ロックの有無に関わらず不可（<see cref="DetailReceiptEntryService"/>の
    /// doc comment参照。充当先の追加自体が設計上禁止されているため）。
    /// </summary>
    private bool CanAddNewTarget => CanEdit && _loadedDetailReceiptNumber is null;

    private bool CanDeleteSlip => CanEdit && _loadedDetailReceiptNumber is not null && !IsEditLocked;

    [RelayCommand]
    private Task LoadAsync() => RunBusyAsync(async () =>
    {
        var accounts = await bankAccountService.GetBankAccountsAsync();
        BankAccounts.Clear();
        foreach (var account in accounts)
        {
            BankAccounts.Add(account);
        }

        var depositMethods = await depositMethodService.GetDepositMethodsAsync();
        DepositMethods.Clear();
        foreach (var depositMethod in depositMethods.Where(m => !m.RequiresBillDueDate))
        {
            DepositMethods.Add(depositMethod);
        }

        if (PreviewSlipNumber is { } previewSlipNumber)
        {
            // Loaded → LoadCommand の async void 経路で呼ばれるため、ここで例外を握らないと
            // アプリがクラッシュする（TODO.md 8-3）。
            try
            {
                await LookupByNumberAsync(previewSlipNumber);
            }
            catch (Exception ex)
            {
                StatusMessage = $"プレビューの読込に失敗しました: {ex.Message}";
            }
        }
    });

    /// <summary>新規（F3）。画面を起動直後の状態に戻す。</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void New()
    {
        if (IsPreviewMode)
        {
            return;
        }

        ClearForm();
        StatusMessage = "新規明細入金";
        NotifyResetToInitialState();
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void OpenCustomerSearch()
    {
        var customer = windowService.ShowDialog<CustomerSearchDialog, CustomerSearchDialogViewModel, Customer>(
            vm => vm.RequiredTaxUnit = TaxUnit.Line);
        if (customer is not null)
        {
            _ = RunBusyAsync(() => ApplyCustomerAsync(customer));
        }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
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
        RequestFocus("SlipRemarks");
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
    /// 訂正モードでは使用不可（<see cref="CanAddNewTarget"/>参照）。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAddNewTarget))]
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
        DepositMethod = DepositMethods.FirstOrDefault(),
    };

    private DetailReceiptLineViewModel CreateLineFromInvoiceCandidate(DetailReceiptInvoiceCandidate c) => new(OnDeleteLine)
    {
        TargetType = DetailReceiptTargetType.DetailInvoice,
        TargetDetailInvoiceNumber = c.DetailInvoiceNumber,
        TargetDisplay = $"{c.DetailInvoiceNumber}（請求書一括）",
        Amount = c.TotalAmount,
        SourceInvoiceCandidate = c,
        DepositMethod = DepositMethods.FirstOrDefault(),
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
    /// のみ。入力済みなら既存の明細入金No.で直接読み込む（訂正・取消モード。TODO.md 7-5）。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task LookupAsync() => RunBusyAsync(() => LookupByNumberAsync(DetailReceiptNumberQuery));

    /// <summary>
    /// 明細入金No.欄からの読込の本体。<see cref="LookupAsync"/>（対話操作）と<see cref="LoadAsync"/>
    /// （プレビュー。TODO.md 8-3）の両方から呼ぶため、<see cref="RunBusyAsync"/>には包まない
    /// （呼び出し側がそれぞれ包む）。
    /// </summary>
    private async Task LookupByNumberAsync(string numberQuery)
    {
        var number = numberQuery.Trim();
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

        await ApplyExisting(number, lines);
    }

    /// <summary>明細入金検索モーダルを開く（<c>Space</c>）。選択した番号は <see cref="LookupAsync"/> と同じ経路で読み込む。</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
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

    /// <summary>保存（F10）。<see cref="_loadedDetailReceiptNumber"/>が設定されていれば訂正、無ければ新規登録。</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveAsync() => RunBusyAsync(async () =>
    {
        if (IsPreviewMode || _customer is null)
        {
            return;
        }

        if (ReceiptDate is not { } receiptDateValue)
        {
            StatusMessage = "入金日付を入力してください。";
            return;
        }

        var receiptDate = DateOnly.FromDateTime(receiptDateValue);

        if (Lines.Any(l => l.DepositMethod is null))
        {
            StatusMessage = "入金方法を選択してください。";
            return;
        }

        try
        {
            if (_loadedDetailReceiptNumber is null)
            {
                var lineInputs = Lines.Select(l => new DetailReceiptLineInput(
                    l.TargetType,
                    l.TargetSalesSlipNumber,
                    l.TargetSalesLineNumber,
                    l.TargetDetailInvoiceNumber,
                    l.DepositMethod!.DepositMethodCode,
                    l.BankAccountCode,
                    string.IsNullOrWhiteSpace(l.LineRemarks) ? null : l.LineRemarks)).ToList();

                var detailReceiptNumber = await detailReceiptEntryService.SaveNewAsync(
                    _customer.CustomerCode,
                    receiptDate,
                    string.IsNullOrWhiteSpace(SlipRemarks) ? null : SlipRemarks,
                    lineInputs);

                // 登録成功後は画面を起動直後の状態へ戻す（docs/product-spec.md UI/UX節「登録後のリセット」）。
                ClearForm();
                StatusMessage = $"明細入金No. {detailReceiptNumber} を登録しました。";
            }
            else
            {
                // 訂正では充当先を追加できないため、全行が読込時から存在する行
                // （PersistedLineNumber が設定済み）のはず（このクラスの doc comment参照）。
                var lineCorrections = Lines.Select(l => new DetailReceiptLineCorrection(
                    l.PersistedLineNumber!.Value,
                    l.DepositMethod!.DepositMethodCode,
                    l.BankAccountCode,
                    string.IsNullOrWhiteSpace(l.LineRemarks) ? null : l.LineRemarks)).ToList();

                var correctedDetailReceiptNumber = _loadedDetailReceiptNumber;
                await detailReceiptEntryService.UpdateAsync(
                    correctedDetailReceiptNumber,
                    receiptDate,
                    string.IsNullOrWhiteSpace(SlipRemarks) ? null : SlipRemarks,
                    lineCorrections,
                    _loadedLineNumbers);

                // 新規登録と挙動を揃え、訂正保存だけ伝票を表示し続ける例外を作らない
                // （docs/product-spec.md UI/UX節「登録後のリセット」）。
                ClearForm();
                StatusMessage = $"明細入金No. {correctedDetailReceiptNumber} を訂正しました。";
            }

            NotifyResetToInitialState();
        }
        catch (DetailReceiptEntryException ex)
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

    /// <summary>取消（F8、TODO.md 7-5）。</summary>
    [RelayCommand(CanExecute = nameof(CanDeleteSlip))]
    private Task DeleteSlipAsync() => RunBusyAsync(async () =>
    {
        if (IsPreviewMode || _loadedDetailReceiptNumber is null)
        {
            return;
        }

        try
        {
            await detailReceiptEntryService.CancelSlipAsync(_loadedDetailReceiptNumber);
            var cancelledDetailReceiptNumber = _loadedDetailReceiptNumber;
            New();
            StatusMessage = $"明細入金No. {cancelledDetailReceiptNumber} を取消しました。";
        }
        catch (DetailReceiptEntryException ex)
        {
            StatusMessage = $"取消エラー: {ex.Message}";
        }
        catch (SlipConcurrencyException ex)
        {
            StatusMessage = $"取消エラー: {ex.Message}";
        }
    });

    /// <summary>
    /// 既存の明細入金を読み込む（訂正・取消モード。TODO.md 7-5）。訂正では充当先を追加できない
    /// ため、右ペインの候補一覧は表示しない（<see cref="SalesCandidates"/>／<see cref="InvoiceCandidates"/>
    /// は空のまま）。
    /// </summary>
    private async Task ApplyExisting(string detailReceiptNumber, List<DetailReceiptEntity> lines)
    {
        var header = lines[0];

        var customer = await customerService.GetByCodeAsync(header.CustomerCode);
        var lockResult = await detailReceiptEntryService.EvaluateEditLockAsync(lines);

        DetailReceiptNumberQuery = detailReceiptNumber;
        ReceiptDate = header.ReceiptDate.ToDateTime(TimeOnly.MinValue);
        CustomerCode = header.CustomerCode;
        CustomerName = header.CustomerName;
        SlipRemarks = header.SlipRemarks ?? string.Empty;
        _customer = customer;

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
                PersistedLineNumber = line.LineNumber,
                TargetType = line.TargetType,
                TargetSalesSlipNumber = line.TargetSalesSlipNumber,
                TargetSalesLineNumber = line.TargetSalesLineNumber,
                TargetDetailInvoiceNumber = line.TargetDetailInvoiceNumber,
                TargetDisplay = display,
                DepositMethod = await ResolveDepositMethodAsync(line.DepositMethodCode),
                BankAccountCode = line.BankAccountCode,
                Amount = line.AllocatedAmount,
                LineRemarks = line.LineRemarks ?? string.Empty,
            });
        }

        OnPropertyChanged(nameof(ReceiptTotal));
        _loadedDetailReceiptNumber = detailReceiptNumber;
        _loadedLineNumbers = lines.Select(l => l.LineNumber).ToList();
        IsExistingLoaded = true;
        IsEditLocked = lockResult.IsLocked;
        StatusMessage = IsPreviewMode
            ? $"明細入金No. {detailReceiptNumber} をプレビュー表示中（編集できません）。"
            : lockResult.IsLocked
                ? $"明細入金No. {detailReceiptNumber} を読み込みました（編集不可: {lockResult.Reason}）"
                : $"明細入金No. {detailReceiptNumber} を読み込みました。";
    }

    private void ClearForm()
    {
        DetailReceiptNumberQuery = string.Empty;
        ReceiptDate = DateTime.Today;
        CustomerCode = string.Empty;
        CustomerName = string.Empty;
        SlipRemarks = string.Empty;
        _customer = null;
        _loadedDetailReceiptNumber = null;
        _loadedLineNumbers = [];

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
        IsEditLocked = false;
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void ClearLines() => Lines.Clear();

    /// <summary>
    /// コードから<see cref="DepositMethods"/>内のインスタンスを引く。無効化済みで一覧から外れている
    /// コードを参照する過去伝票を読み込む場合はここで取得して一覧に加える
    /// （<see cref="ReceiptEntryViewModel.ResolveDepositMethodAsync"/>と同じ理由）。
    /// </summary>
    private async Task<DepositMethod?> ResolveDepositMethodAsync(string depositMethodCode)
    {
        var found = DepositMethods.FirstOrDefault(m => m.DepositMethodCode == depositMethodCode);
        if (found is not null)
        {
            return found;
        }

        var fetched = await depositMethodService.GetByCodeAsync(depositMethodCode);
        if (fetched is not null)
        {
            DepositMethods.Add(fetched);
        }

        return fetched;
    }
}

/// <summary>売上伝票タブの上段リストの1行（同一売上No.の候補明細をまとめたサマリ）。</summary>
public sealed record DetailReceiptCandidateSlipSummary(
    string SalesSlipNumber, DateOnly SlipDate, int LineCount, decimal TotalAmount);
