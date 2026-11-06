using System.Collections.ObjectModel;
using System.Windows;
using bmcs_app.Application.Common;
using bmcs_app.Application.Master;
using bmcs_app.Domain.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bmcs_app.ViewModels.Master;

/// <summary>
/// 消費税率マスタ画面（管理者専用）。一覧から行を選ぶと修正・削除、選ばずに入力すると追加になる。
/// 施行日は主キーのため、既存行では変更できない。伝票がある期間の行の変更・削除は
/// <see cref="TaxRateService"/> が拒否する。
/// </summary>
public partial class TaxRateMasterViewModel(
    TaxRateService taxRateService,
    TaxRateQueryService taxRateQueryService) : ViewModelBase
{
    public ObservableCollection<TaxRateListItem> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNew))]
    public partial TaxRateListItem? SelectedItem { get; set; }

    /// <summary>一覧で行を選んでいない＝追加。施行日を入力できるのは追加のときだけ。</summary>
    public bool IsNew => SelectedItem is null;

    [ObservableProperty]
    public partial DateTime? EffectiveDate { get; set; }

    [ObservableProperty]
    public partial string StandardTaxRateText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ReducedTaxRateText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    partial void OnSelectedItemChanged(TaxRateListItem? value)
    {
        EffectiveDate = value?.EffectiveDate.ToDateTime(TimeOnly.MinValue);
        StandardTaxRateText = value?.StandardTaxRate.ToString("0.##") ?? string.Empty;
        ReducedTaxRateText = value?.ReducedTaxRate.ToString("0.##") ?? string.Empty;
    }

    /// <summary>画面表示時に読み込む（Window の Loaded から呼ばれる）。</summary>
    [RelayCommand]
    private Task LoadAsync() => RunBusyAsync(() => ReloadAsync(null));

    [RelayCommand]
    private void New() => SelectedItem = null;

    private async Task ReloadAsync(DateOnly? select)
    {
        var rates = await taxRateQueryService.GetAllAsync();

        Items.Clear();
        foreach (var rate in rates.OrderByDescending(r => r.EffectiveDate))
        {
            Items.Add(TaxRateListItem.From(rate));
        }

        SelectedItem = Items.FirstOrDefault(i => i.EffectiveDate == select);
    }

    [RelayCommand]
    private Task SaveAsync() => RunBusyAsync(async () =>
    {
        if (!decimal.TryParse(StandardTaxRateText, out var standard))
        {
            Warn("標準税率は数値で入力してください。");
            return;
        }

        if (!decimal.TryParse(ReducedTaxRateText, out var reduced))
        {
            Warn("軽減税率は数値で入力してください。");
            return;
        }

        var selected = SelectedItem;
        DateOnly effectiveDate;
        if (selected is null)
        {
            if (EffectiveDate is not { } date)
            {
                Warn("施行日を入力してください。");
                return;
            }

            effectiveDate = DateOnly.FromDateTime(date);
        }
        else
        {
            effectiveDate = selected.EffectiveDate;
        }

        await TryAsync(effectiveDate, async () =>
        {
            if (selected is null)
            {
                await taxRateService.CreateAsync(effectiveDate, standard, reduced);
            }
            else
            {
                await taxRateService.UpdateAsync(effectiveDate, standard, reduced, selected.RowVersion);
            }

            await ReloadAsync(effectiveDate);
            StatusMessage = "保存しました。受注入力・売上入力へは画面を開き直すと反映されます。";
        });
    });

    [RelayCommand]
    private Task DeleteAsync() => RunBusyAsync(async () =>
    {
        if (SelectedItem is not { } selected)
        {
            Warn("削除する税率を一覧から選択してください。");
            return;
        }

        var answer = MessageBox.Show(
            $"施行日 {selected.EffectiveDate:yyyy/MM/dd} の税率を削除します。よろしいですか？",
            "bmcs_app", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        await TryAsync(selected.EffectiveDate, async () =>
        {
            await taxRateService.DeactivateAsync(selected.EffectiveDate, selected.RowVersion);
            await ReloadAsync(null);
            StatusMessage = "削除しました。受注入力・売上入力へは画面を開き直すと反映されます。";
        });
    });

    /// <summary>業務ルール違反は警告表示、競合は警告のうえ再読み込みする。</summary>
    private async Task TryAsync(DateOnly effectiveDate, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is TaxRateValidationException or TaxRateConcurrencyException)
        {
            Warn(ex.Message);
            if (ex is TaxRateConcurrencyException)
            {
                await ReloadAsync(effectiveDate);
            }
        }
    }

    private static void Warn(string message)
        => MessageBox.Show(message, "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
}

/// <summary>税率一覧の1行。</summary>
public sealed record TaxRateListItem(
    DateOnly EffectiveDate,
    decimal StandardTaxRate,
    decimal ReducedTaxRate,
    byte[] RowVersion)
{
    public static TaxRateListItem From(TaxRateMaster master) => new(
        master.EffectiveDate, master.StandardTaxRate, master.ReducedTaxRate, master.RowVersion!);
}
