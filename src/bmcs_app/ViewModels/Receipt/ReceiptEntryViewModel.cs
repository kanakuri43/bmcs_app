using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using bmcs_app.Application.Billing;
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
/// 入金入力画面。締め得意先（請求単位／伝票単位）専用。都度得意先（内税明細単位）の
/// 入金は明細入金画面が担う。
///
/// 明細行は支払手段の内訳（入金方法＋金額＋行摘要）であり、利用者が直接追加・編集・削除する
/// （売上入力と同じ明細行パターン。docs/design_document.md 17章）。請求への充当は
/// 保存時に<see cref="ReceiptEntryService.SaveNewAsync"/>／<see cref="ReceiptEntryService.UpdateAsync"/>
/// が内部で自動計算するため、この画面には表示しない。利用者に必要なのは充当先ではなく残高であるため、
/// 得意先確定時に請求残高を表示する。
///
/// 振込手数料差額の入力はこの画面のスコープ外。既存伝票の訂正・取消は
/// 伝票No.欄で読み込んだ後、そのまま編集して保存(F10)＝訂正、取消(F8)＝取消として扱う
/// （<see cref="Sales.SalesEntryViewModel"/>と同じパターン）。得意先コードのみ、読み込んだ後は
/// 変更できない（<see cref="IsExistingLoaded"/>）。編集ロック中（<see cref="IsEditLocked"/>、
/// <see cref="ReceiptEntryService.EvaluateEditLockAsync"/>参照）は保存・取消とも不可。
/// </summary>
public partial class ReceiptEntryViewModel(
    ReceiptEntryService receiptEntryService,
    CustomerService customerService,
    BankAccountService bankAccountService,
    DepositMethodService depositMethodService,
    BillingClosedDateService billingClosedDateService,
    WindowService windowService) : ViewModelBase
{
    private Customer? _customer;
    private decimal _outstandingTotal;
    private string? _loadedReceiptSlipNumber;
    private IReadOnlyList<short> _loadedLineNumbers = [];

    /// <summary>
    /// 伝票プレビュー用の入口。得意先元帳からの表示専用で開くとき、
    /// <see cref="Services.WindowService.Show{TWindow, TViewModel}"/> の <c>configure</c> から
    /// ウィンドウ表示前に一度だけ設定する。値は以後変化しないため<c>[ObservableProperty]</c>は使わない。
    /// </summary>
    public string? PreviewSlipNumber { get; set; }

    /// <summary>プレビュー表示中かどうか。</summary>
    public bool IsPreviewMode => PreviewSlipNumber is not null;

    /// <summary>ウィンドウタイトル。</summary>
    public string WindowTitle => IsPreviewMode ? "bmcs_app - 入金入力（プレビュー・編集不可）" : "bmcs_app - 入金入力";

    public ObservableCollection<BankAccount> BankAccounts { get; } = [];

    public ObservableCollection<DepositMethod> DepositMethods { get; } = [];

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
    /// 編集ロック中かどうか（<see cref="ReceiptEntryService.EvaluateEditLockAsync"/>の結果）。ViewModelは判定せず、結果をそのまま表示・反映するだけにする
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
    public partial DateTime? ReceiptDate { get; set; } = DateTime.Today;

    /// <summary>
    /// 登録可能な最小日付（請求締め済みの翌日。制限なしなら<c>null</c>）。得意先確定時に
    /// <see cref="BillingClosedDateService"/>から取得し、<c>DatePicker.DisplayDateStart</c>に
    /// バインドする（画面上の利便性のみを担い、最終的な検証はApplication層が行う。
    /// docs/design_document.md 25章「ジャーナル系の日付制限」）。
    /// </summary>
    [ObservableProperty]
    public partial DateTime? MinimumReceiptDate { get; set; }

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

    private bool CanSave => IsEditable && _customer is not null && Lines.Any(l => !l.IsBlank);

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

        var depositMethods = await depositMethodService.GetDepositMethodsAsync();
        DepositMethods.Clear();
        foreach (var depositMethod in depositMethods)
        {
            DepositMethods.Add(depositMethod);
        }

        Lines.CollectionChanged += OnLinesCollectionChanged;

        if (Lines.Count == 0)
        {
            Lines.Add(CreateLine());
            RenumberLines();
        }

        if (PreviewSlipNumber is { } previewSlipNumber)
        {
            // Loaded → LoadCommand の async void 経路で呼ばれるため、ここで例外を握らないと
            // アプリがクラッシュする。
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

        if (!customer.IsBillingRoot)
        {
            StatusMessage = $"「{customer.CustomerName}」は請求集約元です。入金は請求集約先「{customer.BillingCustomerCode}」で登録してください。";
            return;
        }

        _customer = customer;
        CustomerCode = customer.CustomerCode;
        CustomerName = customer.CustomerName;

        var summary = await receiptEntryService.GetReceivableSummaryAsync(customer.CustomerCode);
        _outstandingTotal = summary.OutstandingTotal;
        RaiseTotalsChanged();

        var dateCorrectionNote = await ApplyMinimumReceiptDateAsync(customer.CustomerCode);
        StatusMessage = dateCorrectionNote is null
            ? $"得意先: {customer.CustomerName}"
            : $"得意先: {customer.CustomerName}（{dateCorrectionNote}）";
        SaveCommand.NotifyCanExecuteChanged();
        RequestFocus("SlipRemarks");
    }

    /// <summary>
    /// 得意先確定時に登録可能な最小日付を取得して<see cref="MinimumReceiptDate"/>へ反映する。新規登録
    /// （<see cref="ApplyCustomerAsync"/>経由）では、現在の<see cref="ReceiptDate"/>が最小日付より前なら
    /// 最小日付へ補正する。訂正モードの読込（<see cref="ApplyExisting"/>、
    /// 既に保存済みの日付を保つべき経路）ではこのメソッドを呼ばない。戻り値は補正した場合のみ通知文言、
    /// それ以外は<c>null</c>。
    /// </summary>
    private async Task<string?> ApplyMinimumReceiptDateAsync(string customerCode)
    {
        var minimumDate = await billingClosedDateService.GetMinimumEntryDateAsync(customerCode);
        MinimumReceiptDate = minimumDate?.ToDateTime(TimeOnly.MinValue);

        if (MinimumReceiptDate is not { } minimum || ReceiptDate is not { } current || current >= minimum)
        {
            return null;
        }

        ReceiptDate = minimum;
        return $"請求締め済みのため入金日付を{minimum:yyyy/MM/dd}に変更しました";
    }

    private ReceiptLineViewModel CreateLine() => new(onDelete: OnDeleteLine)
    {
        DepositMethod = DepositMethods.FirstOrDefault(),
    };

    private void OnDeleteLine(ReceiptLineViewModel line)
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
            // 受注入力・売上入力（商品コード確定時にEnsureTrailingBlankLineを呼ぶ）と同じ操作感にする。
            // 入金明細にはコード欄が無く、IsBlankはAmount==0mで判定するため、金額確定がこの行を
            // 使う意思表示になる。
            EnsureTrailingBlankLine();
            RenumberLines();
            RaiseTotalsChanged();
        }
    }

    /// <summary>
    /// 入金No.欄で Return を押したときの挙動（docs/product-spec.md UI/UX節「ジャーナル系画面の
    /// 伝票No入力欄の挙動」）。空欄なら新規登録モードとして次項目（入金日付）へフォーカス移動する
    /// のみ。入力済みなら既存の入金No.で直接読み込む（訂正・取消モード）。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task LookupAsync() => RunBusyAsync(() => LookupByNumberAsync(ReceiptSlipNumberQuery));

    /// <summary>
    /// 入金No.欄からの読込の本体。<see cref="LookupAsync"/>（対話操作）と<see cref="LoadAsync"/>
    /// （プレビュー）の両方から呼ぶため、<see cref="RunBusyAsync"/>には包まない
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

        if (ReceiptDate is not { } receiptDateValue)
        {
            StatusMessage = "入金日付を入力してください。";
            return;
        }

        if (MinimumReceiptDate is { } minimumReceiptDate && receiptDateValue < minimumReceiptDate)
        {
            StatusMessage = $"請求締め済みのため、入金日付は{minimumReceiptDate:yyyy/MM/dd}以降を指定してください。";
            return;
        }

        var receiptDate = DateOnly.FromDateTime(receiptDateValue);

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

    /// <summary>取消（F8）。</summary>
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
            if (line.DepositMethod is not { } depositMethod)
            {
                error = "入金方法を選択してください。";
                return false;
            }

            DateOnly? billDueDate = null;
            if (depositMethod.RequiresBillDueDate)
            {
                if (line.BillDueDate is not { } billDueDateValue)
                {
                    error = "手形期日を入力してください。";
                    return false;
                }

                billDueDate = DateOnly.FromDateTime(billDueDateValue);
            }

            lineInputs.Add(new ReceiptLineInput(
                depositMethod.DepositMethodCode,
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
            if (line.DepositMethod is not { } depositMethod)
            {
                error = "入金方法を選択してください。";
                return false;
            }

            DateOnly? billDueDate = null;
            if (depositMethod.RequiresBillDueDate)
            {
                if (line.BillDueDate is not { } billDueDateValue)
                {
                    error = "手形期日を入力してください。";
                    return false;
                }

                billDueDate = DateOnly.FromDateTime(billDueDateValue);
            }

            lineCorrections.Add(new ReceiptLineCorrection(
                line.PersistedLineNumber ?? 0,
                depositMethod.DepositMethodCode,
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
        ReceiptDate = header.ReceiptDate.ToDateTime(TimeOnly.MinValue);
        MinimumReceiptDate = customer is null
            ? null
            : (await billingClosedDateService.GetMinimumEntryDateAsync(customer.CustomerCode))
                ?.ToDateTime(TimeOnly.MinValue);
        CustomerCode = header.CustomerCode;
        CustomerName = header.CustomerName;
        SlipRemarks = header.SlipRemarks ?? string.Empty;
        _customer = customer;

        _outstandingTotal = customer is null
            ? 0m
            : (await receiptEntryService.GetReceivableSummaryAsync(customer.CustomerCode)).OutstandingTotal;

        ClearLines();
        foreach (var line in lines)
        {
            var lineVm = CreateLine();
            lineVm.LineNumber = line.LineNumber;
            lineVm.PersistedLineNumber = line.LineNumber;
            lineVm.DepositMethod = await ResolveDepositMethodAsync(line.DepositMethodCode);
            lineVm.BankAccountCode = line.BankAccountCode;
            lineVm.BillDueDate = line.BillDueDate?.ToDateTime(TimeOnly.MinValue);
            lineVm.Amount = line.Amount;
            lineVm.LineRemarks = line.LineRemarks ?? string.Empty;
            Lines.Add(lineVm);
        }
        if (!IsPreviewMode)
        {
            EnsureTrailingBlankLine();
            RenumberLines();
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

    /// <summary>
    /// コードから<see cref="DepositMethods"/>内のインスタンスを引く（ComboBoxのSelectedItemはインスタンス
    /// 参照で一致判定するため）。無効化済みで一覧から外れているコードを参照する過去伝票を読み込む場合は
    /// ここで取得して一覧に加える（<see cref="BankAccounts"/>には無い対応だが、入金方法は必須項目のため
    /// 選択が外れたままにはできない）。
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

    private void ClearForm()
    {
        ReceiptSlipNumberQuery = string.Empty;
        ReceiptDate = DateTime.Today;
        MinimumReceiptDate = null;
        CustomerCode = string.Empty;
        CustomerName = string.Empty;
        SlipRemarks = string.Empty;
        _customer = null;
        _outstandingTotal = 0m;
        _loadedReceiptSlipNumber = null;
        _loadedLineNumbers = [];

        ClearLines();
        Lines.Add(CreateLine());
        RenumberLines();
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
        SaveCommand.NotifyCanExecuteChanged();
    }
}
