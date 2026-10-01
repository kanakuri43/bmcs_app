using bmcs_app.Domain.Entities;

namespace bmcs_app.Domain.Calculations;

/// <summary>
/// メニュー構成マスタ（menu）から、指定した権限レベルで閲覧可能な階層を組み立てる（TODO.md 2-7）。
/// 権限は子（末端の機能メニュー）にのみ持たせる方針（C-8）に対応し、
/// 「子の <see cref="Menu.RequiredPermissionLevel"/> ≦ 社員の権限レベル」の項目だけを残す。
/// 表示する子が1件も残らない親（カテゴリ見出し）はメニュー自体を表示しない。
/// 現状の運用は「カテゴリ（親、画面なし）→機能（子、画面あり）」の2階層のみを想定する
/// （schema上はさらに深い階層も許容するが、現在の画面構成では使わない）。
/// </summary>
public static class MenuTreeBuilder
{
    public static IReadOnlyList<MenuCategoryNode> Build(IReadOnlyList<Menu> allMenuItems, byte permissionLevel)
    {
        var childrenByParent = allMenuItems
            .Where(m => m.ParentMenuCode is not null)
            .GroupBy(m => m.ParentMenuCode!)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.DisplayOrder).ToList());

        var categories = new List<MenuCategoryNode>();
        foreach (var parent in allMenuItems.Where(m => m.ParentMenuCode is null).OrderBy(m => m.DisplayOrder))
        {
            if (!childrenByParent.TryGetValue(parent.MenuCode, out var children))
            {
                continue;
            }

            var visibleItems = children
                .Where(c => c.ScreenKey is not null && c.RequiredPermissionLevel <= permissionLevel)
                .Select(c => new MenuItemNode(c.MenuCode, c.MenuName, c.ScreenKey!))
                .ToList();

            if (visibleItems.Count > 0)
            {
                categories.Add(new MenuCategoryNode(
                    parent.MenuCode, parent.MenuName, visibleItems, parent.IsDefaultExpanded));
            }
        }

        return categories;
    }
}

public sealed record MenuCategoryNode(
    string MenuCode, string MenuName, IReadOnlyList<MenuItemNode> Items, bool IsDefaultExpanded);

public sealed record MenuItemNode(string MenuCode, string MenuName, string ScreenKey);
