using bmcs_app.Application.Master;
using Microsoft.Extensions.DependencyInjection;

namespace bmcs_app.Application.Tests.Master;

/// <summary>メニュー構成マスタの参照の結合テスト。開発用ライブDB（172.16.3.171）に対して読み取りだけを行う。</summary>
public class MenuServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    [Fact]
    public async Task メニューを読み込め初期展開の列も取得できる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<MenuService>();

        var menus = await service.GetMenuTreeAsync();

        // is_default_expanded 列がエンティティと対応していれば、カテゴリ（親）の行を問題なく読める。
        Assert.Contains(menus, m => m.ParentMenuCode is null);
    }
}
