using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Entities;

namespace bmcs_app.Domain.Tests.Calculations;

/// <summary>メニュー構成マスタの権限フィルタ（TODO.md 2-7）のテスト。</summary>
public class MenuTreeBuilderTests
{
    private static Menu Category(string code, short order) => new()
    {
        MenuCode = code,
        ParentMenuCode = null,
        MenuName = code,
        DisplayOrder = order,
        RequiredPermissionLevel = null,
        ScreenKey = null,
        CreatedBy = "TEST",
        CreatedAt = default,
        UpdatedBy = "TEST",
        UpdatedAt = default,
    };

    private static Menu Item(string code, string parentCode, short order, byte requiredLevel, string screenKey) => new()
    {
        MenuCode = code,
        ParentMenuCode = parentCode,
        MenuName = code,
        DisplayOrder = order,
        RequiredPermissionLevel = requiredLevel,
        ScreenKey = screenKey,
        CreatedBy = "TEST",
        CreatedAt = default,
        UpdatedBy = "TEST",
        UpdatedAt = default,
    };

    [Fact]
    public void 権限レベルが足りない項目は表示されない()
    {
        var menu = new List<Menu>
        {
            Category("CAT1", 1),
            Item("ITEM_LOW", "CAT1", 1, requiredLevel: 1, screenKey: "low"),
            Item("ITEM_HIGH", "CAT1", 2, requiredLevel: 9, screenKey: "high"),
        };

        var result = MenuTreeBuilder.Build(menu, permissionLevel: 1);

        var category = Assert.Single(result);
        var item = Assert.Single(category.Items);
        Assert.Equal("ITEM_LOW", item.MenuCode);
    }

    [Fact]
    public void 権限レベルが十分なら両方表示される()
    {
        var menu = new List<Menu>
        {
            Category("CAT1", 1),
            Item("ITEM_LOW", "CAT1", 1, requiredLevel: 1, screenKey: "low"),
            Item("ITEM_HIGH", "CAT1", 2, requiredLevel: 9, screenKey: "high"),
        };

        var result = MenuTreeBuilder.Build(menu, permissionLevel: 9);

        var category = Assert.Single(result);
        Assert.Equal(2, category.Items.Count);
    }

    [Fact]
    public void 子が1件も表示できないカテゴリは表示されない()
    {
        var menu = new List<Menu>
        {
            Category("CAT1", 1),
            Item("ITEM_HIGH", "CAT1", 1, requiredLevel: 9, screenKey: "high"),
        };

        var result = MenuTreeBuilder.Build(menu, permissionLevel: 1);

        Assert.Empty(result);
    }

    [Fact]
    public void カテゴリと項目の表示順を守る()
    {
        var menu = new List<Menu>
        {
            Category("CAT2", 2),
            Category("CAT1", 1),
            Item("ITEM_B", "CAT1", 2, requiredLevel: 1, screenKey: "b"),
            Item("ITEM_A", "CAT1", 1, requiredLevel: 1, screenKey: "a"),
            Item("ITEM_C", "CAT2", 1, requiredLevel: 1, screenKey: "c"),
        };

        var result = MenuTreeBuilder.Build(menu, permissionLevel: 1);

        Assert.Equal(["CAT1", "CAT2"], result.Select(c => c.MenuCode));
        Assert.Equal(["ITEM_A", "ITEM_B"], result[0].Items.Select(i => i.MenuCode));
    }
}
