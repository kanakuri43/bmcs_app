-- =============================================================================
-- 034_split_system_admin_menu.sql
-- メインメニューの「管理」を「マスタ管理」に改名し、そこから、プリンタ設定・メニューマスタ管理・クエリ実行を
-- 新設の分類「システム管理」へ移す（再実行安全）。
-- 014 の再適用はメニューマスタ管理画面での編集内容を失わせるため、変更は本スクリプトで行う。
--
-- 適用: sqlcmd -S <サーバ> -U <ユーザー> -d bmcs_db -C -I -i scripts\034_split_system_admin_menu.sql
-- =============================================================================

USE bmcs_db;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.menus WHERE menu_code = N'MNU_SYSTEM')
    INSERT INTO dbo.menus
        (menu_code, parent_menu_code, menu_name, display_order, required_permission_level, screen_key,
         created_by, created_at, updated_by, updated_at)
    VALUES
        (N'MNU_SYSTEM', NULL, N'システム管理', 7, NULL, NULL,
         N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME());
GO

UPDATE m
SET parent_menu_code = N'MNU_SYSTEM', display_order = v.display_order,
    updated_by = N'SEED', updated_at = SYSDATETIME()
FROM dbo.menus AS m
JOIN (VALUES (N'MNU_PRINTER_SETTING', 1), (N'MNU_MENU_MASTER', 2), (N'MNU_QUERY_RUNNER', 3))
     AS v(menu_code, display_order) ON v.menu_code = m.menu_code
WHERE m.parent_menu_code IS NULL OR m.parent_menu_code <> N'MNU_SYSTEM';
GO

UPDATE dbo.menus
SET menu_name = N'マスタ管理', updated_by = N'SEED', updated_at = SYSDATETIME()
WHERE menu_code = N'MNU_MASTER' AND menu_name <> N'マスタ管理';
GO
