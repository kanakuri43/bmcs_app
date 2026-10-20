-- =============================================================================
-- 031_add_customer_product_rate_menu.sql
-- メニューのマスタ管理に「得意先／商品／定価掛率マスタ」を追加する。
-- 014 を再適用するとメニューマスタ管理画面での編集内容が失われるため、追記は本スクリプトで行う（再実行安全）。
--
-- 適用: sqlcmd -S <サーバ> -U <ユーザー> -d bmcs_db -C -I -i scripts_add_customer_product_price_menu.sql
-- =============================================================================

USE bmcs_db;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.menus WHERE menu_code = N'MNU_CUST_PROD_RATE')
    INSERT INTO dbo.menus
        (menu_code, parent_menu_code, menu_name, display_order, required_permission_level, screen_key,
         created_by, created_at, updated_by, updated_at)
    VALUES
        (N'MNU_CUST_PROD_RATE', N'MNU_MASTER', N'得意先／商品／定価掛率マスタ', 12, 1, N'customer_product_rate_master',
         N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME());
GO
