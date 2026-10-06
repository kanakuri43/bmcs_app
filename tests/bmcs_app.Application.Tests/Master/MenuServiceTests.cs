using bmcs_app.Application.Master;
using Microsoft.Extensions.DependencyInjection;

namespace bmcs_app.Application.Tests.Master;

/// <summary>メニュー構成マスタの結合テスト。開発用ライブDB（172.16.3.171）に対して実行する。更新系テストは元の値へ戻す。</summary>
public class MenuServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private const string ChildCode = "MNU_PRINTER_SETTING";
    private const string ParentCode = "MNU_MASTER";

    [Fact]
    public async Task メニューを読み込め初期展開の列も取得できる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<MenuService>();

        var menus = await service.GetMenuTreeAsync();

        // is_default_expanded 列がエンティティと対応していれば、カテゴリ（親）の行を問題なく読める。
        Assert.Contains(menus, m => m.ParentMenuCode is null);
    }

    [Fact]
    public async Task 子の表示名と必要権限を更新でき元に戻せる()
    {
        var original = await GetAsync(ChildCode);
        try
        {
            var updated = await UpdateAsync(ChildCode, "更新テスト", original.DisplayOrder, 9, true, original.RowVersion!);

            Assert.Equal("更新テスト", updated.MenuName);
            Assert.Equal((byte)9, (await GetAsync(ChildCode)).RequiredPermissionLevel);
        }
        finally
        {
            var current = await GetAsync(ChildCode);
            await UpdateAsync(ChildCode, original.MenuName, original.DisplayOrder, original.RequiredPermissionLevel, true, current.RowVersion!);
        }
    }

    [Fact]
    public async Task 親の初期展開を更新でき元に戻せる()
    {
        var original = await GetAsync(ParentCode);
        try
        {
            await UpdateAsync(ParentCode, original.MenuName, original.DisplayOrder, null, !original.IsDefaultExpanded, original.RowVersion!);

            Assert.Equal(!original.IsDefaultExpanded, (await GetAsync(ParentCode)).IsDefaultExpanded);
        }
        finally
        {
            var current = await GetAsync(ParentCode);
            await UpdateAsync(ParentCode, original.MenuName, original.DisplayOrder, null, original.IsDefaultExpanded, current.RowVersion!);
        }
    }

    [Fact]
    public async Task 古いRowVersionでの更新は競合になる()
    {
        var original = await GetAsync(ChildCode);
        try
        {
            await UpdateAsync(ChildCode, original.MenuName, original.DisplayOrder, original.RequiredPermissionLevel, true, original.RowVersion!);

            await Assert.ThrowsAsync<MenuConcurrencyException>(() =>
                UpdateAsync(ChildCode, original.MenuName, original.DisplayOrder, original.RequiredPermissionLevel, true, original.RowVersion!));
        }
        finally
        {
            var current = await GetAsync(ChildCode);
            await UpdateAsync(ChildCode, original.MenuName, original.DisplayOrder, original.RequiredPermissionLevel, true, current.RowVersion!);
        }
    }

    [Theory]
    [InlineData(ChildCode, "", 1, 1)]          // 表示名が空
    [InlineData(ChildCode, "名前", 0, 1)]      // 表示順が0
    [InlineData(ChildCode, "名前", 1, 0)]      // 子の権限レベルが範囲外（下限）
    [InlineData(ChildCode, "名前", 1, 10)]     // 子の権限レベルが範囲外（上限）
    [InlineData(ChildCode, "名前", 1, null)]   // 子の権限レベルが未設定
    [InlineData(ParentCode, "名前", 1, 1)]     // 親に権限レベルは設定できない
    public async Task 不正な入力は検証エラーになる(string menuCode, string name, short order, int? level)
    {
        var original = await GetAsync(menuCode);

        await Assert.ThrowsAsync<MenuValidationException>(() =>
            UpdateAsync(menuCode, name, order, (byte?)level, true, original.RowVersion!));
    }

    private async Task<Domain.Entities.Menu> GetAsync(string menuCode)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<MenuService>();
        return (await service.GetMenuTreeAsync()).Single(m => m.MenuCode == menuCode);
    }

    private async Task<Domain.Entities.Menu> UpdateAsync(
        string menuCode, string name, short order, byte? level, bool expanded, byte[] rowVersion)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<MenuService>();
        return await service.UpdateAsync(menuCode, name, order, level, expanded, rowVersion);
    }
}
