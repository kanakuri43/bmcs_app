namespace bmcs_app;

/// <summary>
/// 起動パラメータで渡される権限レベル
/// --level=1: フル（すべて有効）
/// --level=2: 標準（売上系のみ）
/// --level=3: 限定（参照・照会のみ）
/// </summary>
public enum PermissionLevel
{
    Full     = 1,
    Standard = 2,
    Limited  = 3,
}

public static class PermissionPolicy
{
    // null = 全許可
    private static readonly HashSet<string> StandardAllowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "bmcs_app.Order.exe",
        "bmcs_app.Sales.exe",
        "bmcs_app.Receipt.exe",
        "bmcs_app.Search.exe",
        "bmcs_app.Closing.exe",
    };

    private static readonly HashSet<string> LimitedAllowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "bmcs_app.Search.exe",
        "bmcs_app.PurchaseSearch.exe",
        "bmcs_app.Inventory.exe",
    };

    public static HashSet<string>? GetAllowedExes(PermissionLevel level) => level switch
    {
        PermissionLevel.Full     => null,
        PermissionLevel.Standard => StandardAllowed,
        PermissionLevel.Limited  => LimitedAllowed,
        _                        => null,
    };

    public static bool IsSettingsEnabled(PermissionLevel level) =>
        level == PermissionLevel.Full;
}
