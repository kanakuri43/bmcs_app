using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using bmcs_app.Application.Master;
using bmcs_app.Application.Receipt;
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
/// 入金入力画面（TODO.md 7-2）。締め得意先（請求単位／伝票単位）専用。都度得意先（内税明細単位）の
/// 入金は明細入金画面（TODO.md 7-4）が担う。
///
/// 明細行は支払手段の内訳（入金方法＋金額＋行摘要）であり、利用者が直接追加・編集・削除する
/// （売上入力と同じ明細行パターン。docs/design_document.md 17章、2026-09-15改訂）。請求への充当は
/// 保存時に<see cref="ReceiptEntryService.SaveNewAsync"/>が内部で自動計算するため、この画面には
/// 表示しない。利用者に必要なのは充当先ではなく残高であるため、得意先確定時に請求残高を表示する。
///
/// 振込手数料差額の入力（TODO.md 7-3）・既存伝票の訂正／取消（TODO.md 7-5）はこの画面のスコープ外
/// （別タスク）。伝票No.欄で既存の入金を読み込んだ場合は読み取り専用表示にする（明細請求書発行画面と
/// 同じ「既存分は読み取り専用」パターン）。
/// </summary>
public partial class ReceiptEntryViewModel(
    ReceiptEntryService receiptEntryService,
    CustomerService customerService,
    BankAccountService bankAccountService,
    WindowService windowService) : ViewModelBase
{
    private Customer? _customer;
    private decimal _outstandingTotal;

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

    /// <summary>既存の入金を読み込んだ状態かどうか。真のときは読み取り専用（訂正・取消はTODO.md 7-5）。</summary>
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

    /// <summary>得意先確定時に取得した請求残高（今回の入金額を含まない、入金前の残高）。</summary>
    public decimal OutstandingTotal => _outstandingTotal;

    /// <summary>今回の入金後に見込まれる請求残高。</summary>
    public decimal OutstandingAfterReceiptTotal => _outstandingTotal - ReceiptTotal;

    private bool CanSave => !IsExistingLoaded && _customer is not null && Lines.Any(l => !l.IsBlank);

    private bool CanAddLine => IsEditable;

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

        Lines.CollectionChanged += OnLinesCollectionChanged;

        if (Lines.Count == 0)
        {
            Lines.Add(CreateLine());
        }
    });

    /// <summary>新規（F3）。画面を起動直後の状態に戻す。</summary>
    [RelayCommand]
    private void New()
    {
        ClearForm();
        StatusMessage = "新規入金";
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
    /// のみ。入力済みなら既存の入金No.で直接読み込む（読み取り専用表示。訂正・取消はTODO.md 7-5）。
    /// </summary>
    [RelayCommand]
    private Task LookupAsync() => RunBusyAsync(async () =>
    {
        var number = ReceiptSlipNumberQuery.Trim();
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

        ApplyExisting(number, lines);
    });

    /// <summary>入金検索モーダルを開く（<c>Space</c>）。選択した番号は <see cref="LookupAsync"/> と同じ経路で読み込む。</summary>
    [RelayCommand]
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

        if (!TryBuildLineInputs(out var lineInputs, out var error))
        {
            StatusMessage = error;
            return;
        }

        try
        {
            var receiptSlipNumber = await receiptEntryService.SaveNewAsync(
                _customer.CustomerCode,
                receiptDate,
                string.IsNullOrWhiteSpace(SlipRemarks) ? null : SlipRemarks,
                lineInputs);

            // 登録成功後は画面を起動直後の状態へ戻す（docs/product-spec.md UI/UX節「登録後のリセット」）。
            ClearForm();
            StatusMessage = $"入金No. {receiptSlipNumber} を登録しました。";
            NotifyResetToInitialState();
        }
        catch (ReceiptEntryException ex)
        {
            StatusMessage = $"登録エラー: {ex.Message}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"登録エラー: {ex.Message}";
        }
    });

    /// <summary>入金の訂正・取消はTODO.md 7-5で実装する（枠のみ・使用不可）。</summary>
    [RelayCommand(CanExecute = nameof(CanUseUnimplementedFeature))]
    private void DeleteSlip()
    {
    }

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

    private void ApplyExisting(string receiptSlipNumber, List<ReceiptEntity> lines)
    {
        var header = lines[0];

        ReceiptSlipNumberQuery = receiptSlipNumber;
        ReceiptDateText = header.ReceiptDate.ToString("yyyy/MM/dd");
        CustomerCode = header.CustomerCode;
        CustomerName = header.CustomerName;
        SlipRemarks = header.SlipRemarks ?? string.Empty;
        _customer = null;
        _outstandingTotal = 0m;

        ClearLines();
        foreach (var line in lines)
        {
            var lineVm = CreateLine();
            lineVm.LineNumber = line.LineNumber;
            lineVm.ReceiptMethod = line.ReceiptMethod;
            lineVm.BankAccountCode = line.BankAccountCode;
            lineVm.BillDueDateText = line.BillDueDate?.ToString("yyyy/MM/dd") ?? string.Empty;
            lineVm.Amount = line.Amount;
            lineVm.LineRemarks = line.LineRemarks ?? string.Empty;
            Lines.Add(lineVm);
        }

        IsExistingLoaded = true;
        SaveCommand.NotifyCanExecuteChanged();
        StatusMessage = $"入金No. {receiptSlipNumber} を読み込みました（読み取り専用。訂正・取消はTODO.md 7-5で対応予定）。";
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

        ClearLines();
        Lines.Add(CreateLine());
        RaiseTotalsChanged();
        IsExistingLoaded = false;
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
