namespace bmcs_app.Domain.Entities;

/// <summary>
/// メニュー構成マスタ（menu）。親子階層構造。
/// 権限は子（末端の機能メニュー）にのみ持たせ、親（分類の見出し）には持たせない
/// （<see cref="RequiredPermissionLevel"/> と <see cref="ScreenKey"/> は DB の CHECK 制約で対で強制される）。
/// </summary>
public class Menu : AuditableEntity
{
    public required string MenuCode { get; set; }

    /// <summary>親メニュー（自己参照）。NULL＝最上位。</summary>
    public string? ParentMenuCode { get; set; }

    public required string MenuName { get; set; }

    public required short DisplayOrder { get; set; }

    /// <summary>最小必須権限。子のみ設定し、親は NULL。</summary>
    public byte? RequiredPermissionLevel { get; set; }

    /// <summary>起動する画面の識別子。親は NULL。</summary>
    public string? ScreenKey { get; set; }

    /// <summary>
    /// メインメニューでカテゴリ（親）を初期状態で開くか。親の行だけが参照し、子の行の値は使われない。
    /// </summary>
    public bool IsDefaultExpanded { get; set; } = true;
}
