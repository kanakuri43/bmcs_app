using System.Collections.ObjectModel;
using System.Windows;
using bmcs_app.Application.Billing;
using bmcs_app.Application.Master;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Reports;
using bmcs_app.Services;
using bmcs_app.ViewModels.Common;
using bmcs_app.Views.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bmcs_app.ViewModels.Billing;

/// <summary>
/// 明細請求書発行画面（TODO.md 6-3）。都度得意先（内税明細単位）の未請求かつ消込完了でない
/// 売上明細行を選び、明細請求書を発行する。左＝取込候補／右＝請求書明細の2ペイン構成
/// （デモ<c>bmcs_app.LineInvoice</c>のレイアウトを踏襲。差異は docs/design_document.md 11章）。
/// 既存の明細請求書番号を読み込んだ場合は読み取り専用表示にする（訂正の概念が無いため）。
/// </summary>
public partial class DetailInvoiceIssueViewModel(
    DetailInvoiceService detailInvoiceService,
    CustomerService customerService,
    WindowService windowService) : ViewModelBase
{
    private Customer? _customer;

    [ObservableProperty]
    public partial string DetailInvoiceNumberQuery { get; set; } = string.Empty;

    /// <summary>既存の明細請求書を読み込んだ状態かどうか。真のときは読み取り専用（訂正不可）。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCandidateCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveLineCommand))]
    [NotifyCanExecuteChangedFor(nameof(IssueCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(PrintCommand))]
    public partial bool IsExistingLoaded { get; set; }

    /// <summary>読み込んだ既存明細請求書の状態（未読込時はnull）。取消(F8)・印刷(F11)の有効判定に使う。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(PrintCommand))]
    public partial DetailInvoiceStatus? LoadedInvoiceStatus { get; set; }

    [ObservableProperty]
    public partial DateTime? IssueDate { get; set; } = DateTime.Today;

    [ObservableProperty]
    public partial string CustomerCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CustomerName { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(IssueCommand))]
    public partial string AddresseeName { get; set; } = string.Empty;

    public ObservableCollection<DetailInvoiceSalesLineItem> Candidates { get; } = [];

    public ObservableCollection<DetailInvoiceSalesLineItem> Lines { get; } = [];

    [ObservableProperty]
    public partial DetailInvoiceSalesLineItem? SelectedCandidate { get; set; }

    [ObservableProperty]
    public partial DetailInvoiceSalesLineItem? SelectedLine { get; set; }

    public decimal TaxExcludedTotal => Lines.Sum(l => l.Amount - l.TaxAmount);

    public decimal TaxTotal => Lines.Sum(l => l.TaxAmount);

    public decimal GrandTotal => Lines.Sum(l => l.Amount);

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    private string? _loadedInvoiceNumber;

    private bool CanEdit => !IsExistingLoaded;

    private bool CanIssue => !IsExistingLoaded && _customer is not null && Lines.Count > 0
        && !string.IsNullOrWhiteSpace(AddresseeName);

    private bool CanCancel => IsExistingLoaded && LoadedInvoiceStatus == DetailInvoiceStatus.Issued;

    /// <summary>新規（F3）。画面を起動直後の状態に戻す。</summary>
    [RelayCommand]
    private void New()
    {
        ClearForm();
        StatusMessage = "新規明細請求書";
    }

    /// <summary>
    /// 明細請求書番号欄で Return を押したときの挙動。空欄なら新規登録モードとして次項目
    /// （請求日付）へフォーカス移動するのみ。入力済みなら既存の明細請求書番号で直接読み込む
    /// （読み取り専用表示。docs/product-spec.md UI/UX節「ジャーナル系画面の伝票No入力欄の挙動」）。
    /// </summary>
    [RelayCommand]
    private Task LookupAsync() => RunBusyAsync(async () =>
    {
        var number = DetailInvoiceNumberQuery.Trim();
        if (string.IsNullOrWhiteSpace(number))
        {
            RequestFocus("IssueDate");
            return;
        }

        try
        {
            var view = await detailInvoiceService.GetByNumberAsync(number);
            if (view is null)
            {
                // 該当が無ければエラー表示のみに留め、新規登録モードへは進まない
                // （入力済みの得意先・宛名・明細を破棄しない。docs/product-spec.md UI/UX節）。
                StatusMessage = $"明細請求書番号「{number}」は見つかりません。";
                return;
            }

            ApplyView(view);
        }
        catch (Exception ex)
        {
            StatusMessage = $"取得エラー: {ex.Message}";
        }
    });

    /// <summary>明細請求書検索モーダルを開く（<c>Space</c>）。選択した番号は <see cref="LookupAsync"/> と同じ経路で読み込む。</summary>
    [RelayCommand]
    private void OpenDetailInvoiceSearch()
    {
        var detailInvoiceNumber = windowService.ShowDialog<SlipSearchDialog, SlipSearchDialogViewModel, string>(
            vm => vm.Target = SlipSearchTarget.DetailInvoice);

        if (!string.IsNullOrWhiteSpace(detailInvoiceNumber))
        {
            DetailInvoiceNumberQuery = detailInvoiceNumber;
            _ = LookupAsync();
        }
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

        try
        {
            var customer = await customerService.GetByCodeAsync(CustomerCode);
            if (customer is null)
            {
                StatusMessage = $"得意先コード「{CustomerCode}」が見つかりません。";
                return;
            }

            await ApplyCustomerAsync(customer);
        }
        catch (Exception ex)
        {
            StatusMessage = $"取得エラー: {ex.Message}";
        }
    });

    private async Task ApplyCustomerAsync(Customer customer)
    {
        if (customer.TaxUnit != TaxUnit.Line)
        {
            StatusMessage = "内税明細単位（都度得意先）以外は明細請求書の対象外です。";
            return;
        }

        _customer = customer;
        CustomerCode = customer.CustomerCode;
        CustomerName = customer.CustomerName;

        // 宛名は得意先名を初期値として転記し、手入力で上書き可能にする
        // （C-9・2026-09-10確定。子得意先マスタは持たず都度書き換え方式に一本化）。
        if (string.IsNullOrWhiteSpace(AddresseeName))
        {
            AddresseeName = customer.CustomerName;
        }

        try
        {
            await LoadCandidatesAsync();
            StatusMessage = $"得意先: {customer.CustomerName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"取得エラー: {ex.Message}";
        }

        IssueCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadCandidatesAsync()
    {
        Candidates.Clear();
        if (_customer is null)
        {
            return;
        }

        var alreadyOnSlip = Lines.Select(l => (l.SalesSlipNumber, l.SalesLineNumber)).ToHashSet();
        var candidates = await detailInvoiceService.GetCandidatesAsync(_customer.CustomerCode);
        foreach (var candidate in candidates.Where(c => !alreadyOnSlip.Contains((c.SalesSlipNumber, c.SalesLineNumber))))
        {
            Candidates.Add(candidate);
        }
    }

    /// <summary>候補行を請求書明細へ追加する。<see cref="bmcs_app.Behaviors.RowActivationBehavior"/>の
    /// Enter／ダブルクリックと、「選択した行を追加」ボタン（<see cref="SelectedCandidate"/>）の両方から呼ぶ。</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void AddCandidate(object? item)
    {
        if (item is not DetailInvoiceSalesLineItem line)
        {
            return;
        }

        Candidates.Remove(line);
        Lines.Add(line);
        RaiseTotalsChanged();
        IssueCommand.NotifyCanExecuteChanged();
    }

    /// <summary>請求書明細から候補へ戻す。Enter／ダブルクリックと「除外」ボタンの両方から呼ぶ。</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void RemoveLine(object? item)
    {
        if (item is not DetailInvoiceSalesLineItem line)
        {
            return;
        }

        Lines.Remove(line);
        Candidates.Add(line);
        RaiseTotalsChanged();
        IssueCommand.NotifyCanExecuteChanged();
    }

    /// <summary>発行（F10）。</summary>
    [RelayCommand(CanExecute = nameof(CanIssue))]
    private Task IssueAsync() => RunBusyAsync(async () =>
    {
        if (_customer is null || Lines.Count == 0)
        {
            return;
        }

        if (IssueDate is not { } issueDateValue)
        {
            StatusMessage = "請求日付を入力してください。";
            return;
        }

        var issueDate = DateOnly.FromDateTime(issueDateValue);

        var lineKeys = Lines.Select(l => (l.SalesSlipNumber, l.SalesLineNumber)).ToList();

        try
        {
            var issued = await detailInvoiceService.IssueAsync(
                _customer.CustomerCode, AddresseeName.Trim(), issueDate, lineKeys);

            var printMessage = await PromptAndPrintAsync(issued.DetailInvoiceNumber);

            // 発行成功後は画面を起動直後の状態へ戻す（docs/product-spec.md UI/UX節「登録後のリセット」）。
            ClearForm();
            StatusMessage = printMessage is null
                ? $"明細請求書 {issued.DetailInvoiceNumber} を発行しました。"
                : $"明細請求書 {issued.DetailInvoiceNumber} を発行しました。　{printMessage}";
            NotifyResetToInitialState();
        }
        catch (DetailInvoiceException ex)
        {
            StatusMessage = $"発行エラー: {ex.Message}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"発行エラー: {ex.Message}";
        }
    });

    /// <summary>取消（F8）。発行済みの明細請求書を取消し、紐付いていた売上を未請求へ戻す。</summary>
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private Task CancelAsync() => RunBusyAsync(async () =>
    {
        if (_loadedInvoiceNumber is null)
        {
            return;
        }

        var confirm = MessageBox.Show(
            $"明細請求書 {_loadedInvoiceNumber}（{CustomerCode} 宛名: {AddresseeName}）を取消しますか？\n" +
            "取消すると、紐付いていた売上明細行がすべて未請求に戻ります。",
            "bmcs_app", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var cancelled = await detailInvoiceService.CancelAsync(_loadedInvoiceNumber);
            var cancelledNumber = cancelled.DetailInvoiceNumber;

            // 取消後は画面を起動直後の状態へ戻す（docs/product-spec.md UI/UX節「登録後のリセット」）。
            ClearForm();
            StatusMessage = $"明細請求書 {cancelledNumber} を取消しました。";
            NotifyResetToInitialState();
        }
        catch (DetailInvoiceException ex)
        {
            StatusMessage = $"取消エラー: {ex.Message}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"取消エラー: {ex.Message}";
        }
    });

    /// <summary>
    /// 印刷（F11、TODO.md 10-5）。発行済みを読み込んでいる場合のみ有効（取消済みは連携行が
    /// 物理削除され明細0件になるため対象外。<c>docs/design_document.md</c> 12-1節）。
    /// 印刷履歴は記録しない（納品書と異なり「（再発行）」表示も行わない）。
    /// </summary>
    private bool CanPrint => IsExistingLoaded && LoadedInvoiceStatus == DetailInvoiceStatus.Issued;

    [RelayCommand(CanExecute = nameof(CanPrint))]
    private async Task PrintAsync()
    {
        if (_loadedInvoiceNumber is null)
        {
            return;
        }

        var message = await PrintDetailInvoiceAsync(_loadedInvoiceNumber);
        if (message is not null)
        {
            StatusMessage = message;
        }
    }

    /// <summary>発行直後に「印刷しますか？」を確認し、Yesならプレビュー・印刷まで行う。</summary>
    private async Task<string?> PromptAndPrintAsync(string detailInvoiceNumber)
    {
        var confirm = MessageBox.Show(
            $"明細請求書 {detailInvoiceNumber} を印刷しますか？",
            "bmcs_app", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return null;
        }

        return await PrintDetailInvoiceAsync(detailInvoiceNumber);
    }

    private async Task<string?> PrintDetailInvoiceAsync(string detailInvoiceNumber)
    {
        DetailInvoiceData data;
        try
        {
            data = await detailInvoiceService.GetPrintDataAsync(detailInvoiceNumber);
        }
        catch (DetailInvoiceException ex)
        {
            return $"印刷エラー: {ex.Message}";
        }

        windowService.ShowDialog<ReportPreviewDialog, ReportPreviewDialogViewModel, bool>(
            vm => vm.Initialize(ReportKind.DetailInvoice, $"明細請求書 {detailInvoiceNumber}",
                () => new DetailInvoiceDocumentBuilder(data).Build()));

        return null;
    }

    private void ApplyView(DetailInvoiceView view)
    {
        var header = view.Header;

        DetailInvoiceNumberQuery = header.DetailInvoiceNumber;
        IssueDate = header.IssueDate.ToDateTime(TimeOnly.MinValue);
        CustomerCode = header.CustomerCode;
        CustomerName = header.CustomerName;
        AddresseeName = header.AddresseeName;
        _customer = null;
        _loadedInvoiceNumber = header.DetailInvoiceNumber;

        Candidates.Clear();
        Lines.Clear();
        foreach (var line in view.Lines)
        {
            Lines.Add(line);
        }

        RaiseTotalsChanged();
        IsExistingLoaded = true;
        LoadedInvoiceStatus = header.InvoiceStatus;

        StatusMessage = header.InvoiceStatus == DetailInvoiceStatus.Cancelled
            ? $"取消済みです（{header.CancelledAt:yyyy/MM/dd HH:mm} {header.CancelledBy}）。"
            : $"発行済みです（{header.IssuedAt:yyyy/MM/dd HH:mm} {header.IssuedBy}）。";
    }

    private void ClearForm()
    {
        DetailInvoiceNumberQuery = string.Empty;
        IssueDate = DateTime.Today;
        CustomerCode = string.Empty;
        CustomerName = string.Empty;
        AddresseeName = string.Empty;
        _customer = null;
        _loadedInvoiceNumber = null;

        Candidates.Clear();
        Lines.Clear();
        SelectedCandidate = null;
        SelectedLine = null;

        RaiseTotalsChanged();
        IsExistingLoaded = false;
        LoadedInvoiceStatus = null;
    }

    private void RaiseTotalsChanged()
    {
        OnPropertyChanged(nameof(TaxExcludedTotal));
        OnPropertyChanged(nameof(TaxTotal));
        OnPropertyChanged(nameof(GrandTotal));
    }
}
