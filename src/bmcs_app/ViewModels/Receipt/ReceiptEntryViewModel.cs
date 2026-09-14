using System.Collections.ObjectModel;
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
/// 得意先・入金額を入力すると、確定済み請求へ古い順に自動配分した明細行を自動表示する
/// （<see cref="ReceiptEntryService.PreviewAllocationAsync"/>。docs/product-spec.md 共通業務ルール4。
/// <see cref="Billing.BillingClosingViewModel"/>の「条件変更時に自動でプレビュー再取得」と同じ方式）。
/// 明細行は自動配分の結果を表示するだけで、行の手動追加・編集・削除は行わない（古い順の自動配分が
/// 業務ルールそのものであり、画面での上書きを許すとその前提が崩れるため）。
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

    public ObservableCollection<BankAccount> BankAccounts { get; } = [];

    public ObservableCollection<ReceiptMethodOption> ReceiptMethodOptions { get; } =
    [
        new(ReceiptMethod.Cash, "現金"),
        new(ReceiptMethod.BankTransfer, "振込"),
        new(ReceiptMethod.PromissoryNote, "手形"),
        new(ReceiptMethod.Offset, "相殺"),
    ];

    public ObservableCollection<ReceiptAllocationLine> Lines { get; } = [];

    [ObservableProperty]
    public partial string ReceiptSlipNumberQuery { get; set; } = string.Empty;

    /// <summary>既存の入金を読み込んだ状態かどうか。真のときは読み取り専用（訂正・取消はTODO.md 7-5）。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyPropertyChangedFor(nameof(IsEditable))]
    public partial bool IsExistingLoaded { get; set; }

    /// <summary>入金方法・入金先口座欄の編集可否（<see cref="IsExistingLoaded"/>の否定）。</summary>
    public bool IsEditable => !IsExistingLoaded;

    [ObservableProperty]
    public partial string ReceiptDateText { get; set; } = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy/MM/dd");

    [ObservableProperty]
    public partial string CustomerCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CustomerName { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBankTransferSelected))]
    public partial ReceiptMethod ReceiptMethod { get; set; } = ReceiptMethod.BankTransfer;

    /// <summary>入金先口座欄の表示可否（振込のときだけ表示する）。</summary>
    public bool IsBankTransferSelected => ReceiptMethod == ReceiptMethod.BankTransfer;

    [ObservableProperty]
    public partial string? BankAccountCode { get; set; }

    /// <summary>
    /// 入金額。変更すると自動的にその金額での配分をプレビュー取得する
    /// （<see cref="OnReceiptAmountTextChanged"/>。XAML側は<c>UpdateSourceTrigger=LostFocus</c>のため
    /// 1文字入力するごとにDB照会は走らない。<see cref="Billing.BillingClosingViewModel"/>と同じ方式）。
    /// </summary>
    [ObservableProperty]
    public partial string ReceiptAmountText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SlipRemarks { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    /// <summary>請求に充当された額の合計（前受・過入金の行を除く）。</summary>
    public decimal AllocatedToBillingTotal => Lines.Where(l => l.BillingNumber is not null).Sum(l => l.AllocatedAmount);

    /// <summary>前受・過入金（充当先未定）の額。</summary>
    public decimal UnallocatedTotal => Lines.Where(l => l.BillingNumber is null).Sum(l => l.AllocatedAmount);

    private bool CanSave => !IsExistingLoaded && _customer is not null && Lines.Count > 0;

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
        StatusMessage = $"得意先: {customer.CustomerName}";

        await RefreshAllocationPreviewAsync();
        SaveCommand.NotifyCanExecuteChanged();
    }

    partial void OnReceiptAmountTextChanged(string value) => _ = RefreshAllocationPreviewAsync();

    /// <summary>
    /// 得意先・入金額の両方が確定していれば、確定済み請求への古い順の自動配分をプレビュー取得する
    /// （保存しない。<see cref="ReceiptEntryService.PreviewAllocationAsync"/>）。
    /// </summary>
    private Task RefreshAllocationPreviewAsync() => RunBusyAsync(async () =>
    {
        if (IsExistingLoaded || _customer is null || !TryParseReceiptAmount(out var amount) || amount <= 0m)
        {
            Lines.Clear();
            RaiseTotalsChanged();
            SaveCommand.NotifyCanExecuteChanged();
            return;
        }

        try
        {
            var allocations = await receiptEntryService.PreviewAllocationAsync(_customer.CustomerCode, amount);
            Lines.Clear();
            foreach (var line in allocations)
            {
                Lines.Add(line);
            }
        }
        catch (ReceiptEntryException ex)
        {
            Lines.Clear();
            StatusMessage = $"配分エラー: {ex.Message}";
        }

        RaiseTotalsChanged();
        SaveCommand.NotifyCanExecuteChanged();
    });

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
            // （入力済みの得意先・入金額等を破棄しない。docs/product-spec.md UI/UX節）。
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

        if (!TryParseReceiptAmount(out var amount))
        {
            StatusMessage = "入金額の形式が不正です。";
            return;
        }

        try
        {
            var receiptSlipNumber = await receiptEntryService.SaveNewAsync(
                _customer.CustomerCode,
                receiptDate,
                ReceiptMethod,
                ReceiptMethod == ReceiptMethod.BankTransfer ? BankAccountCode : null,
                amount,
                string.IsNullOrWhiteSpace(SlipRemarks) ? null : SlipRemarks);

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

    private bool TryParseReceiptAmount(out decimal amount) =>
        decimal.TryParse(ReceiptAmountText.Replace(",", string.Empty), out amount);

    private void ApplyExisting(string receiptSlipNumber, List<ReceiptEntity> lines)
    {
        var header = lines[0];

        ReceiptSlipNumberQuery = receiptSlipNumber;
        ReceiptDateText = header.ReceiptDate.ToString("yyyy/MM/dd");
        CustomerCode = header.CustomerCode;
        CustomerName = header.CustomerName;
        ReceiptMethod = header.ReceiptMethod;
        BankAccountCode = header.BankAccountCode;
        ReceiptAmountText = header.ReceiptAmount.ToString("N0");
        SlipRemarks = header.SlipRemarks ?? string.Empty;
        _customer = null;

        Lines.Clear();
        foreach (var line in lines)
        {
            Lines.Add(new ReceiptAllocationLine(line.BillingNumber, null, null, line.AllocatedAmount));
        }

        RaiseTotalsChanged();
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
        ReceiptMethod = ReceiptMethod.BankTransfer;
        BankAccountCode = null;
        ReceiptAmountText = string.Empty;
        SlipRemarks = string.Empty;
        _customer = null;

        Lines.Clear();
        RaiseTotalsChanged();
        IsExistingLoaded = false;
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void RaiseTotalsChanged()
    {
        OnPropertyChanged(nameof(AllocatedToBillingTotal));
        OnPropertyChanged(nameof(UnallocatedTotal));
    }
}

/// <summary>入金方法の選択肢（ComboBox表示用）。</summary>
public sealed record ReceiptMethodOption(ReceiptMethod Value, string Display);
