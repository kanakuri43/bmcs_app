using bmcs_app.Domain.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bmcs_app.ViewModels.Receipt;

/// <summary>
/// 入金入力画面の明細行1行分。支払手段の内訳（入金方法＋金額）を表す
/// （docs/design_document.md 17章、2026-09-15改訂）。<see cref="Common.SlipLineViewModel"/>と
/// 同じ「親のコールバックを注入され、行自身が削除コマンドを持つ」構造。
/// </summary>
public partial class ReceiptLineViewModel : ObservableObject
{
    private readonly Action<ReceiptLineViewModel> _onDelete;

    public ReceiptLineViewModel(Action<ReceiptLineViewModel> onDelete)
    {
        _onDelete = onDelete;
    }

    [ObservableProperty]
    public partial short LineNumber { get; set; }

    /// <summary>
    /// 訂正（TODO.md 7-5）で、読込時に存在した実際の行番号を保持する。<c>null</c>＝新規追加行
    /// （<see cref="Common.SlipLineViewModel.PersistedLineNumber"/>と同じ理由。主キーの一部である
    /// 実際の行番号とは独立に保持し、行の並べ替え・削除で誤って詰め直さないようにする）。
    /// </summary>
    public short? PersistedLineNumber { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBankAccountVisible))]
    [NotifyPropertyChangedFor(nameof(IsBillDueDateVisible))]
    public partial DepositMethod? DepositMethod { get; set; }

    /// <summary>入金先口座欄の表示要否（入金方法が口座指定を要する場合だけ）。</summary>
    public bool IsBankAccountVisible => DepositMethod?.RequiresBankAccount == true;

    /// <summary>手形期日欄の表示要否（入金方法が期日指定を要する場合だけ）。</summary>
    public bool IsBillDueDateVisible => DepositMethod?.RequiresBillDueDate == true;

    /// <summary>入金方法を変更したら、対象外になった付随欄をクリアする（DBのCHECK制約と対応）。</summary>
    partial void OnDepositMethodChanged(DepositMethod? value)
    {
        if (value?.RequiresBankAccount != true)
        {
            BankAccountCode = null;
        }

        if (value?.RequiresBillDueDate != true)
        {
            BillDueDate = null;
        }
    }

    [ObservableProperty]
    public partial string? BankAccountCode { get; set; }

    /// <summary>手形期日。手形のときだけ入力を要する（未入力＝null）。</summary>
    [ObservableProperty]
    public partial DateTime? BillDueDate { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBlank))]
    public partial decimal Amount { get; set; }

    [ObservableProperty]
    public partial string LineRemarks { get; set; } = string.Empty;

    /// <summary>末尾の空行の判定に使う（金額未入力＝空行）。</summary>
    public bool IsBlank => Amount == 0m;

    [RelayCommand]
    private void Delete() => _onDelete(this);
}
