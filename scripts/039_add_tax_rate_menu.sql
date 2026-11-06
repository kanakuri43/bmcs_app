-- =============================================================================
-- 039_add_tax_rate_menu.sql
-- メインメニューの「システム管理」（MNU_SYSTEM）の末尾に「消費税率マスタ管理」を追加する（再実行安全）。
-- 必要権限レベルは 9（管理者専用）。
-- 014 の再適用はメニューマスタ管理画面での編集内容を失わせるため、追加は本スクリプトで行う。
--
-- 適用: sqlcmd -S <サーバ> -U <ユーザー> -d bmcs_db -C -I -i scripts\039_add_tax_rate_menu.sql
-- =============================================================================

USE bmcs_db;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.menus WHERE menu_code = N'MNU_TAX_RATE')
    INSERT INTO dbo.menus
        (menu_code, parent_menu_code, menu_name, display_order, required_permission_level, screen_key,
         created_by, created_at, updated_by, updated_at)
    VALUES
        (N'MNU_TAX_RATE', N'MNU_SYSTEM', N'消費税率マスタ管理',
         (SELECT ISNULL(MAX(display_order), 0) + 1 FROM dbo.menus WHERE parent_menu_code = N'MNU_SYSTEM'),
         9, N'tax_rate_master',
         N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME());
GO
