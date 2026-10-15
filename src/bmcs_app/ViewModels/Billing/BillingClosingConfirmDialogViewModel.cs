using System.Collections.ObjectModel;
using System.Windows;
using bmcs_app.Application.Billing;
using bmcs_app.Domain.Calculations;
using bmcs_app.Services;
using bmcs_app.ViewModels.Common;
using bmcs_app.Views.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bmcs_app.ViewModels.Billing;

/// <summary>締確定ダイアログの実行結果（キャンセル・失敗時は結果なし）。</summary>
public sealed record BillingClosingOutcome(DateTime ClosingDate, int ConfirmedCount, int SkippedCount);

/// <summary>
/// 締確定ダイアログ。対象を「締め日区分」または「個別指定（複数得意先）」のラジオボタンで選び、
/// 請求日（締め切り日）を指定して締めを実行する。実行に成功したらダイアログを閉じ、
/// 呼び出し元（<see cref="BillingClosingViewModel"/>）が請求日の一覧を再表示する。
/// </summary>
public partial class BillingClosingConfirmDialogViewModel(
    BillingClosingService billingClosingService,
    WindowService windowService) : DialogViewModelBase<BillingClosingOutcome>
{
    public ObservableCollection<ClosingDayOption> ClosingDayOptions { get; } = [];

    [ObservableProperty]
    public partial ClosingDayOption? SelectedClosingDay { get; set; }

    /// <summary>請求日（＝締め切り日）。呼び出し元が<c>configure</c>で既定値を渡す。</summary>
    [ObservableProperty]
    public partial DateTime? ClosingDate { get; set; } = DateTime.Today;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsByClosingDay))]
    public partial bool IsIndividual { get; set; }

    /// <summary>ラジオボタン「締日」側（<see cref="IsIndividual"/>の裏返し）。</summary>
    public bool IsByClosingDay
    {
        get => !IsIndividual;
        set => IsIndividual = !value;
    }

    /// <summary>個別指定で選択済みの得意先コード。</summary>
    public ObservableCollection<string> SelectedCustomerCodes { get; } = [];

    public string SelectedCustomerSummary => SelectedCustomerCodes.Count == 0
        ? "未選択"
        : $"{SelectedCustomerCodes.Count}件選択中";

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [RelayCommand]
    private Task LoadAsync() => RunBusyAsync(async () =>
    {
        foreach (var closingDay in await billingClosingService.GetClosingDayOptionsAsync())
        {
            ClosingDayOptions.Add(new ClosingDayOption(closingDay));
        }

        SelectedClosingDay = ClosingDayOptions.FirstOrDefault();
    });

    /// <summary>締め日区分を選んだら、請求日の年月はそのまま・日だけをその締め日区分に合わせて補正する。</summary>
    partial void OnSelectedClosingDayChanged(ClosingDayOption? value)
    {
        if (value is not null)
        {
            var reference = DateOnly.FromDateTime(ClosingDate ?? DateTime.Today);
            ClosingDate = ClosingDateResolver.Resolve(reference.Year, reference.Month, value.Value).ToDateTime(TimeOnly.MinValue);
        }
    }

    [RelayCommand]
    private void SelectCustomers()
    {
        var codes = windowService.ShowDialog<ClosingCustomerSelectDialog, ClosingCustomerSelectDialogViewModel, List<string>>(
            vm => vm.InitialCodes = SelectedCustomerCodes.ToList());
        if (codes is null)
        {
            return;
        }

        SelectedCustomerCodes.Clear();
        foreach (var code in codes)
        {
            SelectedCustomerCodes.Add(code);
        }

        OnPropertyChanged(nameof(SelectedCustomerSummary));
    }

    [RelayCommand]
    private Task ExecuteAsync() => RunBusyAsync(async () =>
    {
        if (ClosingDate is not { } date)
        {
            StatusMessage = "請求日を入力してください。";
            return;
        }

        string target;
        if (IsIndividual)
        {
            if (SelectedCustomerCodes.Count == 0)
            {
                StatusMessage = "対象得意先を選択してください。";
                return;
            }

            target = $"個別指定（{SelectedCustomerCodes.Count}件）";
        }
        else
        {
            if (SelectedClosingDay is null)
            {
                StatusMessage = "締め日を選択してください。";
                return;
            }

            target = $"締め日区分 {SelectedClosingDay.Display}";
        }

        var confirm = MessageBox.Show(
            $"{target}・請求日 {date:yyyy/MM/dd} で請求締めを確定しますか？\n" +
            "確定前のプレビューはありません。実行すると対象得意先の売上・入金がまとめて請求データとして確定します。",
            "bmcs_app", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var closingDate = DateOnly.FromDateTime(date);
            var results = IsIndividual
                ? await billingClosingService.ConfirmAsync(SelectedCustomerCodes.ToList(), closingDate)
                : await billingClosingService.ConfirmAsync(SelectedClosingDay!.Value, closingDate);

            CloseWith(new BillingClosingOutcome(
                date, results.Count(r => r.SkipReason is null), results.Count(r => r.SkipReason is not null)));
        }
        catch (Exception ex)
        {
            StatusMessage = $"確定エラー: {ex.Message}";
        }
    });
}
