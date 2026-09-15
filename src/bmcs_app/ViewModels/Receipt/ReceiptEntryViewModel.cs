using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using bmcs_app.Application.Common;
using bmcs_app.Application.Master;
using bmcs_app.Application.Receipt;
using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Services;
using bmcs_app.ViewModels.Common;
using bmcs_app.Views.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReceiptEntity = bmcs_app.Domain.Entities.Receipt;

namespace bmcs_app.ViewModels.Receipt;

/// <summary>
/// 入金入力画面（TODO.md 7-2・7-5）。締め得意先（請求単位／伝票単位）専用。都度得意先（内税明細単位）の
/// 入金は明細入金画面（TODO.md 7-4）が担う。
///
/// 明細行は支払手段の内訳（入金方法＋金額＋行摘要）であり、利用者が直接追加・編集・削除する
/// （売上入力と同じ明細行パターン。docs/design_document.md 17章、2026-09-15改訂）。請求への充当は
/// 保存時に<see cref="ReceiptEntryService.SaveNewAsync"/>／<see cref="ReceiptEntryService.UpdateAsync"/>
/// が内部で自動計算するため、この画面には表示しない。利用者に必要なのは充当先ではなく残高であるため、
/// 得意先確定時に請求残高を表示する。
///
/// 振込手数料差額の入力（TODO.md 7-3）はこの画面のスコープ外。既存伝票の訂正・取消（TODO.md 7-5）は
/// 伝票No.欄で読み込んだ後、そのまま編集して保存(F10)＝訂正、取消(F8)＝取消として扱う
/// （<see cref="Sales.SalesEntryViewModel"/>と同じパターン）。得意先コードのみ、読み込んだ後は
/// 変更できない（<see cref="IsExistingLoaded"/>）。編集ロック中（<see cref="IsEditLocked"/>、
/// <see cref="ReceiptEntryService.EvaluateEditLockAsync"/>参照）は保存・取消とも不可。
/// </summary>
public partial class ReceiptEntryViewModel(
    ReceiptEntryService receiptEntryService,
    CustomerService customerService,
    BankAccountService bankAccountService,
    WindowService windowService) : ViewModelBase
{
    private Customer? _customer;
    private decimal _outstandingTotal;
    private string? _loadedReceiptSlipNumber;
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
    public string WindowTitle => IsPreviewMode ? "bmcs_app - 入金入力（プレビュー・編集不可）" : "bmcs_app - 入金入力";

    public ObservableCollection<BankAccount> BankAccounts { get; } = [];

    public ObservableCollection<ReceiptMethodOption> ReceiptMethodOptions { get; } =
    [
        new(ReceiptMethod.Cash, "現金"),
        new(ReceiptMethod.BankTransfer, "振込"),
        new(ReceiptMethod.PromissoryNote, "手形"),
        new(ReceiptMethod.Offset, "相殺"),
    ];

    public ObservableCollection<ReceiptLineViewModel> Lines { get; } = [];

    [ObservableProperty]
    public partial string ReceiptSlipNumberQuery { get; set; } = string.Empty;

    /// <summary>
    /// 既存の入金を読み込んだ状態かどうか。真の間は得意先コードを変更できない
    /// （<see cref="IsEditLocked"/>と異なり、ロックの有無に関わらず得意先の付け替えは常に禁止する）。
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSlipCommand))]
    [NotifyPropertyChangedFor(nameof(IsEditable))]
    [NotifyPropertyChangedFor(nameof(IsHeaderLocked))]
    public partial bool IsExistingLoaded { get; set; }

    /// <summary>
    /// 編集ロック中かどうか（<see cref="ReceiptEntryService.EvaluateEditLockAsync"/>の結果。
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
    /// ヘッダー（入金日付・摘要）・明細の編集可否。新規登録時は常に編集可能、既存読込時は
    /// 編集ロックされていない場合のみ編集可能（＝訂正できる）。
    /// </summary>
    public bool IsEditable => !IsPreviewMode && (!IsExistingLoaded || !IsEditLocked);

    /// <summary>入金日付・摘要のIsReadOnlyバインディング用（得意先コードは<see cref="IsExistingLoaded"/>を直接使う）。</summary>
    public bool IsHeaderLocked => !IsEditable;

    private bool CanEdit => !IsPreviewMode;

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

    /// <summary>得意先確定時に取得した請求残高（今回の入金額を含まない、入金前の残高）。</summary>
    public decimal OutstandingTotal => _outstandingTotal;

    /// <summary>今回の入金後に見込まれる請求残高。</summary>
    public decimal OutstandingAfterReceiptTotal => _outstandingTotal - ReceiptTotal;

    private bool CanSave => IsEditable && _customer is not null && Lines.Any(l => !l.IsBlank);

    private bool CanAddLine => IsEditable;

    private bool CanDeleteSlip => CanEdit && _loadedReceiptSlipNumber is not null && !IsEditLocked;

    [RelayCommand]
    private Task LoadAsync() => RunBusyAsync(async () =>
    {
        var accounts = await bankAccountService.GetBankAccountsAsync();
        BankAccounts.Clear();
        foreach (var account in accounts)
        {
            BankAccounts.Add(account);
        }

        Lines.CollectionChanged += OnLinesCollectionChanged;

        if (Lines.Count == 0)
        {
            Lines.Add(CreateLine());
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
        StatusMessage = "新規入金";
        NotifyResetToInitialState();
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void OpenCustomerSearch()
    {
        var customer = windowService.ShowDialog<CustomerSearchDialog, CustomerSearchDialogViewModel, Customer>();
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
        if (customer.TaxUnit == TaxUnit.Line)
        {
            StatusMessage = "都度得意先（内税明細単位）はこの画面では入金登録できません。明細入金画面をご利用ください。";
            return;
        }

        _customer = customer;
        CustomerCode = customer.CustomerCode;
        CustomerName = customer.CustomerName;

        var summary = await receiptEntryService.GetReceivableSummaryAsync(customer.CustomerCode);
        _outstandingTotal = summary.OutstandingTotal;
        RaiseTotalsChanged();

        StatusMessage = $"得意先: {customer.CustomerName}";
        SaveCommand.NotifyCanExecuteChanged();
    }

    /// <summary>行追加（F2）。</summary>
    [RelayCommand(CanExecute = nameof(CanAddLine))]
    private void AddLine() => Lines.Add(CreateLine());

    private ReceiptLineViewModel CreateLine() => new(onDelete: OnDeleteLine);

    private void OnDeleteLine(ReceiptLineViewModel line)
    {
        if (Lines.Count <= 1)
        {
            return;
        }

        Lines.Remove(line);
        EnsureTrailingBlankLine();
    }

    private void EnsureTrailingBlankLine()
    {
        if (Lines.Count == 0 || !Lines[^1].IsBlank)
        {
            Lines.Add(CreateLine());
        }
    }

    private void OnLinesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (ReceiptLineViewModel line in e.NewItems)
            {
                line.PropertyChanged += OnLinePropertyChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (ReceiptLineViewModel line in e.OldItems)
            {
                line.PropertyChanged -= OnLinePropertyChanged;
            }
        }

        RaiseTotalsChanged();
    }

    private void OnLinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReceiptLineViewModel.Amount))
        {
            RaiseTotalsChanged();
        }
    }

    /// <summary>
    /// 入金No.欄で Return を押したときの挙動（docs/product-spec.md UI/UX節「ジャーナル系画面の
    /// 伝票No入力欄の挙動」）。空欄なら新規登録モードとして次項目（入金日付）へフォーカス移動する
    /// のみ。入力済みなら既存の入金No.で直接読み込む（訂正・取消モード。TODO.md 7-5）。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task LookupAsync() => RunBusyAsync(() => LookupByNumberAsync(ReceiptSlipNumberQuery));

    /// <summary>
    /// 入金No.欄からの読込の本体。<see cref="LookupAsync"/>（対話操作）と<see cref="LoadAsync"/>
    /// （プレビュー。TODO.md 8-3）の両方から呼ぶため、<see cref="RunBusyAsync"/>には包まない
    /// （呼び出し側がそれぞれ包む。二重に包むと<see cref="ViewModelBase.RunBusyAsync"/>の
    /// 再入防止で内側が無視される）。
    /// </summary>
    private async Task LookupByNumberAsync(string numberQuery)
    {
        var number = numberQuery.Trim();
        if (string.IsNullOrWhiteSpace(number))
        {
            RequestFocus("ReceiptDate");
            return;
        }

        var lines = await receiptEntryService.GetByNumberAsync(number);
        if (lines.Count == 0)
        {
            // 該当が無ければエラー表示のみに留め、新規登録モードへは進まない
            // （入力済みの得意先・明細行等を破棄しない。docs/product-spec.md UI/UX節）。
            StatusMessage = $"入金No.「{number}」は見つかりません。";
            return;
        }

        await ApplyExisting(number, lines);
    }

    /// <summary>入金検索モーダルを開く（<c>Space</c>）。選択した番号は <see cref="LookupAsync"/> と同じ経路で読み込む。</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void OpenReceiptSlipSearch()
    {
        var receiptSlipNumber = windowService.ShowDialog<SlipSearchDialog, SlipSearchDialogViewModel, string>(
            vm => vm.Target = SlipSearchTarget.Receipt);

        if (!string.IsNullOrWhiteSpace(receiptSlipNumber))
        {
            ReceiptSlipNumberQuery = receiptSlipNumber;
            _ = LookupAsync();
        }
    }

    /// <summary>保存（F10）。<see cref="_loadedReceiptSlipNumber"/>が設定されていれば訂正、無ければ新規登録。</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveAsync() => RunBusyAsync(async () =>
    {
        if (IsPreviewMode || _customer is null)
        {
            return;
        }

        if (!DateOnly.TryParseExact(ReceiptDateText, "yyyy/MM/dd", out var receiptDate))
        {
            StatusMessage = "入金日付の形式が不正です（yyyy/MM/dd）。";
            return;
        }

        try
        {
            if (_loadedReceiptSlipNumber is null)
            {
                if (!TryBuildLineInputs(out var lineInputs, out var error))
                {
                    StatusMessage = error;
                    return;
                }

                var receiptSlipNumber = await receiptEntryService.SaveNewAsync(
                    _customer.CustomerCode,
                    receiptDate,
                    string.IsNullOrWhiteSpace(SlipRemarks) ? null : SlipRemarks,
                    lineInputs);

                // 登録成功後は画面を起動直後の状態へ戻す（docs/product-spec.md UI/UX節「登録後のリセット」）。
                ClearForm();
                StatusMessage = $"入金No. {receiptSlipNumber} を登録しました。";
            }
            else
            {
                if (!TryBuildLineCorrections(out var lineCorrections, out var error))
                {
                    StatusMessage = error;
                    return;
                }

                var correctedReceiptSlipNumber = _loadedReceiptSlipNumber;
                await receiptEntryService.UpdateAsync(
                    correctedReceiptSlipNumber,
                    receiptDate,
                    string.IsNullOrWhiteSpace(SlipRemarks) ? null : SlipRemarks,
                    lineCorrections,
                    _loadedLineNumbers);

                // 新規登録と挙動を揃え、訂正保存だけ伝票を表示し続ける例外を作らない
                // （docs/product-spec.md UI/UX節「登録後のリセット」、SalesEntryViewModelと同じ方針）。
                ClearForm();
                StatusMessage = $"入金No. {correctedReceiptSlipNumber} を訂正しました。";
            }

            NotifyResetToInitialState();
        }
        catch (ReceiptEntryException ex)
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
        if (IsPreviewMode || _loadedReceiptSlipNumber is null)
        {
            return;
        }

        try
        {
            await receiptEntryService.CancelSlipAsync(_loadedReceiptSlipNumber);
            var cancelledReceiptSlipNumber = _loadedReceiptSlipNumber;
            New();
            StatusMessage = $"入金No. {cancelledReceiptSlipNumber} を取消しました。";
        }
        catch (ReceiptEntryException ex)
        {
            StatusMessage = $"取消エラー: {ex.Message}";
        }
        catch (SlipConcurrencyException ex)
        {
            StatusMessage = $"取消エラー: {ex.Message}";
        }
    });

    private bool TryBuildLineInputs(out List<ReceiptLineInput> lineInputs, out string error)
    {
        lineInputs = [];
        error = string.Empty;

        foreach (var line in Lines.Where(l => !l.IsBlank))
        {
            DateOnly? billDueDate = null;
            if (line.ReceiptMethod == ReceiptMethod.PromissoryNote)
            {
                if (!DateOnly.TryParseExact(line.BillDueDateText, "yyyy/MM/dd", out var parsed))
                {
                    error = "手形期日の形式が不正です（yyyy/MM/dd）。";
                    return false;
                }

                billDueDate = parsed;
            }

            lineInputs.Add(new ReceiptLineInput(
                line.ReceiptMethod,
                line.BankAccountCode,
                billDueDate,
                line.Amount,
                string.IsNullOrWhiteSpace(line.LineRemarks) ? null : line.LineRemarks));
        }

        if (lineInputs.Count == 0)
        {
            error = "明細行を1件以上入力してください。";
            return false;
        }

        return true;
    }

    /// <summary>訂正用（<see cref="ReceiptEntryService.UpdateAsync"/>への入力）。<see cref="TryBuildLineInputs"/>と
    /// 同じ検証を行いつつ、既存行は<see cref="ReceiptLineViewModel.PersistedLineNumber"/>を、
    /// 新規行は<c>0</c>を付与する。</summary>
    private bool TryBuildLineCorrections(out List<ReceiptLineCorrection> lineCorrections, out string error)
    {
        lineCorrections = [];
        error = string.Empty;

        foreach (var line in Lines.Where(l => !l.IsBlank))
        {
            DateOnly? billDueDate = null;
            if (line.ReceiptMethod == ReceiptMethod.PromissoryNote)
            {
                if (!DateOnly.TryParseExact(line.BillDueDateText, "yyyy/MM/dd", out var parsed))
                {
                    error = "手形期日の形式が不正です（yyyy/MM/dd）。";
                    return false;
                }

                billDueDate = parsed;
            }

            lineCorrections.Add(new ReceiptLineCorrection(
                line.PersistedLineNumber ?? 0,
                line.ReceiptMethod,
                line.BankAccountCode,
                billDueDate,
                line.Amount,
                string.IsNullOrWhiteSpace(line.LineRemarks) ? null : line.LineRemarks));
        }

        if (lineCorrections.Count == 0)
        {
            error = "明細行を1件以上入力してください。";
            return false;
        }

        return true;
    }

    private async Task ApplyExisting(string receiptSlipNumber, List<ReceiptEntity> lines)
    {
        var header = lines[0];

        var customer = await customerService.GetByCodeAsync(header.CustomerCode);
        var lockResult = await receiptEntryService.EvaluateEditLockAsync(lines);

        ReceiptSlipNumberQuery = receiptSlipNumber;
        ReceiptDateText = header.ReceiptDate.ToString("yyyy/MM/dd");
        CustomerCode = header.CustomerCode;
        CustomerName = header.CustomerName;
        SlipRemarks = header.SlipRemarks ?? string.Empty;
        _customer = customer;

        // 得意先確定時の請求残高表示をそのまま流用する。この伝票自身の充当額を除外していないため、
        // 訂正で金額を変えた場合の「入金後残高」の見え方は目安に留まる（新規登録画面の情報表示を
        // そのまま転用したもので、7-5の完了条件には影響しない）。
        _outstandingTotal = customer is null
            ? 0m
            : (await receiptEntryService.GetReceivableSummaryAsync(customer.CustomerCode)).OutstandingTotal;

        ClearLines();
        foreach (var line in lines)
        {
            var lineVm = CreateLine();
            lineVm.LineNumber = line.LineNumber;
            lineVm.PersistedLineNumber = line.LineNumber;
            lineVm.ReceiptMethod = line.ReceiptMethod;
            lineVm.BankAccountCode = line.BankAccountCode;
            lineVm.BillDueDateText = line.BillDueDate?.ToString("yyyy/MM/dd") ?? string.Empty;
            lineVm.Amount = line.Amount;
            lineVm.LineRemarks = line.LineRemarks ?? string.Empty;
            Lines.Add(lineVm);
        }
        if (!IsPreviewMode)
        {
            EnsureTrailingBlankLine();
        }

        _loadedReceiptSlipNumber = receiptSlipNumber;
        _loadedLineNumbers = lines.Select(l => l.LineNumber).ToList();
        IsExistingLoaded = true;
        IsEditLocked = lockResult.IsLocked;
        RaiseTotalsChanged();
        StatusMessage = IsPreviewMode
            ? $"入金No. {receiptSlipNumber} をプレビュー表示中（編集できません）。"
            : lockResult.IsLocked
                ? $"入金No. {receiptSlipNumber} を読み込みました（編集不可: {lockResult.Reason}）"
                : $"入金No. {receiptSlipNumber} を読み込みました。";
    }

    private void ClearForm()
    {
        ReceiptSlipNumberQuery = string.Empty;
        ReceiptDateText = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy/MM/dd");
        CustomerCode = string.Empty;
        CustomerName = string.Empty;
        SlipRemarks = string.Empty;
        _customer = null;
        _outstandingTotal = 0m;
        _loadedReceiptSlipNumber = null;
        _loadedLineNumbers = [];

        ClearLines();
        Lines.Add(CreateLine());
        RaiseTotalsChanged();
        IsExistingLoaded = false;
        IsEditLocked = false;
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void ClearLines()
    {
        foreach (var line in Lines)
        {
            line.PropertyChanged -= OnLinePropertyChanged;
        }

        Lines.Clear();
    }

    private void RaiseTotalsChanged()
    {
        OnPropertyChanged(nameof(ReceiptTotal));
        OnPropertyChanged(nameof(OutstandingTotal));
        OnPropertyChanged(nameof(OutstandingAfterReceiptTotal));
        SaveCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>入金方法の選択肢（ComboBox表示用）。</summary>
public sealed record ReceiptMethodOption(ReceiptMethod Value, string Display);
