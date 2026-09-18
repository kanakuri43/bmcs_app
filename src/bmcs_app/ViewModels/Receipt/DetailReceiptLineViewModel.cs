using bmcs_app.Application.Receipt;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bmcs_app.ViewModels.Receipt;

/// <summary>
/// 明細入金画面（TODO.md 7-4）の明細行1行分。締め入金（<see cref="ReceiptLineViewModel"/>）と
/// 異なり、行ごとに充当先（売上明細行または明細請求書）を持ち、金額は対象の全額（または残額）で
/// 固定・読み取り専用（利用者は変更できない）。入金方法・入金先口座・行摘要のみ編集できる。
/// </summary>
public partial class DetailReceiptLineViewModel : ObservableObject
{
    private readonly Action<DetailReceiptLineViewModel> _onDelete;

    public DetailReceiptLineViewModel(Action<DetailReceiptLineViewModel> onDelete)
    {
        _onDelete = onDelete;
    }

    [ObservableProperty]
    public partial short LineNumber { get; set; }

    /// <summary>
    /// 訂正（TODO.md 7-5）で、読込時に存在した実際の行番号を保持する。<c>null</c>＝新規登録モードで
    /// 追加した行（<see cref="Common.SlipLineViewModel.PersistedLineNumber"/>と同じ理由）。
    /// 訂正モードでは充当先の追加ができないため、この画面では常に読込時の値がそのまま保たれる
    /// （<see cref="LineNumber"/>は削除のたびに1から詰め直されるため、訂正の保存には使えない）。
    /// </summary>
    public short? PersistedLineNumber { get; set; }

    /// <summary>充当先の種別。</summary>
    public required DetailReceiptTargetType TargetType { get; set; }

    public string? TargetSalesSlipNumber { get; set; }

    public short? TargetSalesLineNumber { get; set; }

    public string? TargetDetailInvoiceNumber { get; set; }

    /// <summary>充当先の表示用文字列（読み取り専用欄）。</summary>
    public required string TargetDisplay { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBankAccountVisible))]
    public partial DepositMethod? DepositMethod { get; set; }

    /// <summary>入金先口座欄の表示要否（入金方法が口座指定を要する場合だけ）。</summary>
    public bool IsBankAccountVisible => DepositMethod?.RequiresBankAccount == true;

    /// <summary>入金方法を変更したら、対象外になった付随欄をクリアする（DBのCHECK制約と対応）。</summary>
    partial void OnDepositMethodChanged(DepositMethod? value)
    {
        if (value?.RequiresBankAccount != true)
        {
            BankAccountCode = null;
        }
    }

    [ObservableProperty]
    public partial string? BankAccountCode { get; set; }

    /// <summary>金額。対象の全額（または残額）で固定。読み取り専用（決定2・2026-09-15確定）。</summary>
    public required decimal Amount { get; set; }

    [ObservableProperty]
    public partial string LineRemarks { get; set; } = string.Empty;

    /// <summary>取込元の売上伝票タブ候補（削除時に候補一覧へ復元するために保持）。</summary>
    public DetailReceiptSalesCandidate? SourceSalesCandidate { get; set; }

    /// <summary>取込元の明細請求書タブ候補（削除時に候補一覧へ復元するために保持）。</summary>
    public DetailReceiptInvoiceCandidate? SourceInvoiceCandidate { get; set; }

    [RelayCommand]
    private void Delete() => _onDelete(this);
}
