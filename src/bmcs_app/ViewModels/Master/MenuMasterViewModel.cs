using System.Collections.ObjectModel;
using System.Windows;
using bmcs_app.Application.Master;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bmcs_app.ViewModels.Master;

/// <summary>
/// メニュー構成マスタ画面（管理者専用、メニュー単位の権限レベル判定）。編集できるのは既存項目の
/// 表示名・表示順・必要権限レベル（子のみ）・初期展開（親のみ）だけで、項目の追加・削除や
/// screen_key・親子関係の変更は scripts/ の SQL で行う。
/// </summary>
public partial class MenuMasterViewModel(MenuService menuService) : ViewModelBase
{
    public ObservableCollection<MenuListItem> Items { get; } = [];

    [ObservableProperty]
    public partial MenuListItem? SelectedItem { get; set; }

    [ObservableProperty]
    public partial string MenuName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DisplayOrderText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PermissionLevelText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsDefaultExpanded { get; set; }

    /// <summary>選択中が分類（親）か。親は初期展開だけ、子は必要権限レベルだけを持つ。</summary>
    [ObservableProperty]
    public partial bool IsParentSelected { get; set; }

    [ObservableProperty]
    public partial bool IsChildSelected { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    partial void OnSelectedItemChanged(MenuListItem? value)
    {
        IsParentSelected = value is { IsParent: true };
        IsChildSelected = value is { IsParent: false };
        MenuName = value?.MenuName ?? string.Empty;
        DisplayOrderText = value?.DisplayOrder.ToString() ?? string.Empty;
        PermissionLevelText = value?.RequiredPermissionLevel?.ToString() ?? string.Empty;
        IsDefaultExpanded = value?.IsDefaultExpanded ?? false;
    }

    /// <summary>画面表示時に全メニューを読み込む（Window の Loaded から呼ばれる）。</summary>
    [RelayCommand]
    private Task LoadAsync() => RunBusyAsync(() => ReloadAsync(SelectedItem?.MenuCode));

    private async Task ReloadAsync(string? selectMenuCode)
    {
        var menus = await menuService.GetMenuTreeAsync();

        Items.Clear();
        foreach (var parent in menus.Where(m => m.ParentMenuCode is null).OrderBy(m => m.DisplayOrder))
        {
            Items.Add(MenuListItem.From(parent, isParent: true));
            foreach (var child in menus.Where(m => m.ParentMenuCode == parent.MenuCode).OrderBy(m => m.DisplayOrder))
            {
                Items.Add(MenuListItem.From(child, isParent: false));
            }
        }

        SelectedItem = Items.FirstOrDefault(i => i.MenuCode == selectMenuCode);
    }

    [RelayCommand]
    private Task SaveAsync() => RunBusyAsync(async () =>
    {
        if (SelectedItem is not { RowVersion: { } rowVersion } selected)
        {
            Warn("更新するメニューを一覧から選択してください。");
            return;
        }

        if (!short.TryParse(DisplayOrderText, out var displayOrder))
        {
            Warn("表示順は数値で入力してください。");
            return;
        }

        byte? permissionLevel = null;
        if (!selected.IsParent)
        {
            if (!byte.TryParse(PermissionLevelText, out var level))
            {
                Warn("必要権限レベルは数値で入力してください。");
                return;
            }

            permissionLevel = level;
        }

        try
        {
            await menuService.UpdateAsync(selected.MenuCode, MenuName, displayOrder, permissionLevel, IsDefaultExpanded, rowVersion);
            await ReloadAsync(selected.MenuCode);
            StatusMessage = "保存しました。メインメニューへは次回起動時に反映されます。";
        }
        catch (Exception ex) when (ex is MenuValidationException or MenuConcurrencyException)
        {
            Warn(ex.Message);
            if (ex is MenuConcurrencyException)
            {
                await ReloadAsync(selected.MenuCode);
            }
        }
    });

    private static void Warn(string message)
        => MessageBox.Show(message, "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
}

/// <summary>メニュー一覧の1行。子は表示名の前にインデントを付ける。</summary>
public sealed record MenuListItem(
    string MenuCode,
    string MenuName,
    short DisplayOrder,
    byte? RequiredPermissionLevel,
    string? ScreenKey,
    bool IsDefaultExpanded,
    bool IsParent,
    byte[]? RowVersion)
{
    public string Label => IsParent ? MenuName : $"　{MenuName}";

    public static MenuListItem From(bmcs_app.Domain.Entities.Menu menu, bool isParent) => new(
        menu.MenuCode, menu.MenuName, menu.DisplayOrder, menu.RequiredPermissionLevel,
        menu.ScreenKey, menu.IsDefaultExpanded, isParent, menu.RowVersion);
}
