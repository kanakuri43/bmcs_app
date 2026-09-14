using System.Windows;
using bmcs_app.Application.Billing;
using bmcs_app.Domain.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BillingEntity = bmcs_app.Domain.Entities.Billing;

namespace bmcs_app.ViewModels.Billing;

/// <summary>
/// 締め解除処理画面（TODO.md 6-2）。請求締め処理（<see cref="BillingClosingViewModel"/>）とは
/// 別画面（別ウィンドウ）とする（C-8・2026-09-10確定。管理者権限のみの操作を画面分離で表現する。
/// 権限判定自体は0-7未着手のため、本画面では行わない）。
/// 一覧は持たず、請求番号を直接入力して読み込む（得意先マスタ・商品マスタと同じコード直接入力方式）。
/// </summary>
public partial class BillingReleaseViewModel(BillingReleaseService billingReleaseService) : ViewModelBase
{
    [ObservableProperty]
    public partial string BillingNumberQuery { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReleaseCommand))]
    public partial bool IsLoaded { get; set; }

    [ObservableProperty]
    public partial string CustomerCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CustomerName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial TaxUnit TaxUnit { get; set; }

    [ObservableProperty]
    public partial string ClosingYearMonth { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string BillingDateText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial decimal PreviousBalance { get; set; }

    [ObservableProperty]
    public partial decimal ReceiptAmount { get; set; }

    [ObservableProperty]
    public partial decimal SalesAmount { get; set; }

    [ObservableProperty]
    public partial decimal TaxAmount { get; set; }

    [ObservableProperty]
    public partial decimal CurrentBillingAmount { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReleaseCommand))]
    public partial BillingStatus BillingStatus { get; set; }

    [ObservableProperty]
    public partial string ConfirmedAtText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ConfirmedBy { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ReleasedAtText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ReleasedBy { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    private string? _loadedBillingNumber;

    /// <summary>読み込み済みかつ確定済みのときだけ解除できる。</summary>
    private bool CanRelease => IsLoaded && BillingStatus == BillingStatus.Confirmed;

    /// <summary>請求番号欄で Enter を押したときに、入力済み番号で直接読み込む。</summary>
    [RelayCommand]
    private Task LookupAsync() => RunBusyAsync(async () =>
    {
        var billingNumber = BillingNumberQuery.Trim();
        if (string.IsNullOrWhiteSpace(billingNumber))
        {
            return;
        }

        try
        {
            var billing = await billingReleaseService.GetByNumberAsync(billingNumber);
            if (billing is null)
            {
                ClearForm();
                BillingNumberQuery = billingNumber;
                StatusMessage = $"請求番号「{billingNumber}」は見つかりません。";
                return;
            }

            ApplyBilling(billing);
        }
        catch (Exception ex)
        {
            StatusMessage = $"取得エラー: {ex.Message}";
        }
    });

    [RelayCommand(CanExecute = nameof(CanRelease))]
    private Task ReleaseAsync() => RunBusyAsync(async () =>
    {
        if (_loadedBillingNumber is null)
        {
            return;
        }

        var confirm = MessageBox.Show(
            $"請求番号 {_loadedBillingNumber}（{CustomerCode} {CustomerName}）の締めを解除しますか？\n" +
            "解除すると、この請求に集計済みの売上がすべて未請求に戻ります。",
            "bmcs_app", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var released = await billingReleaseService.ReleaseAsync(_loadedBillingNumber);
            var releasedBillingNumber = released.BillingNumber;

            // 解除後は画面を起動直後の状態へ戻す（docs/product-spec.md UI/UX節「登録後のリセット」）。
            ClearForm();
            StatusMessage = $"請求番号 {releasedBillingNumber} の締めを解除しました。";
            NotifyResetToInitialState();
        }
        catch (BillingReleaseException ex)
        {
            StatusMessage = $"解除エラー: {ex.Message}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"解除エラー: {ex.Message}";
        }
    });

    private void ApplyBilling(BillingEntity billing)
    {
        _loadedBillingNumber = billing.BillingNumber;
        BillingNumberQuery = billing.BillingNumber;
        CustomerCode = billing.CustomerCode;
        CustomerName = billing.CustomerName;
        TaxUnit = billing.TaxUnit;
        ClosingYearMonth = billing.ClosingYearMonth;
        BillingDateText = billing.BillingDate.ToString("yyyy/MM/dd");
        PreviousBalance = billing.PreviousBalance;
        ReceiptAmount = billing.ReceiptAmount;
        SalesAmount = billing.SalesAmount;
        TaxAmount = billing.TaxAmount;
        CurrentBillingAmount = billing.CurrentBillingAmount;
        ConfirmedAtText = billing.ConfirmedAt.ToString("yyyy/MM/dd HH:mm");
        ConfirmedBy = billing.ConfirmedBy;
        ReleasedAtText = billing.ReleasedAt?.ToString("yyyy/MM/dd HH:mm") ?? string.Empty;
        ReleasedBy = billing.ReleasedBy ?? string.Empty;
        IsLoaded = true;
        BillingStatus = billing.BillingStatus;
        StatusMessage = billing.BillingStatus == BillingStatus.Released
            ? "既に解除済みです。"
            : string.Empty;
    }

    private void ClearForm()
    {
        _loadedBillingNumber = null;
        BillingNumberQuery = string.Empty;
        CustomerCode = string.Empty;
        CustomerName = string.Empty;
        TaxUnit = default;
        ClosingYearMonth = string.Empty;
        BillingDateText = string.Empty;
        PreviousBalance = 0m;
        ReceiptAmount = 0m;
        SalesAmount = 0m;
        TaxAmount = 0m;
        CurrentBillingAmount = 0m;
        ConfirmedAtText = string.Empty;
        ConfirmedBy = string.Empty;
        ReleasedAtText = string.Empty;
        ReleasedBy = string.Empty;
        IsLoaded = false;
        BillingStatus = default;
    }
}
