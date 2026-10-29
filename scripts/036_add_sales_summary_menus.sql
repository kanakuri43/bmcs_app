-- =============================================================================
-- 036_add_sales_summary_menus.sql
-- 分類「分析」に「得意先別売上・粗利」「商品別売上・粗利」を追加する（担当者別売上・粗利の次）。
-- 再実行安全。
--
-- 適用: sqlcmd -S <サーバ> -U <ユーザー> -d bmcs_db -C -I -i scripts\036_add_sales_summary_menus.sql
-- =============================================================================

USE bmcs_db;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.menus WHERE menu_code = N'MNU_CUSTOMER_SALES')
    INSERT INTO dbo.menus
        (menu_code, parent_menu_code, menu_name, display_order, required_permission_level, screen_key,
         created_by, created_at, updated_by, updated_at)
    VALUES
        (N'MNU_CUSTOMER_SALES', N'MNU_ANALYSIS', N'得意先別売上・粗利', 2, 1, N'customer_sales',
         N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME());
GO

IF NOT EXISTS (SELECT 1 FROM dbo.menus WHERE menu_code = N'MNU_PRODUCT_SALES')
    INSERT INTO dbo.menus
        (menu_code, parent_menu_code, menu_name, display_order, required_permission_level, screen_key,
         created_by, created_at, updated_by, updated_at)
    VALUES
        (N'MNU_PRODUCT_SALES', N'MNU_ANALYSIS', N'商品別売上・粗利', 3, 1, N'product_sales',
         N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME());
GO
