-- =============================================================================
-- 028_add_delivery_note_bulk_issue_menu.sql
-- メニューに「納品書一括発行」を追加する（TODO.md 10-2）。
-- 014 を再適用するとメニューマスタ管理画面での編集内容が失われるため、追記は本スクリプトで行う（再実行安全）。
--
-- 適用: sqlcmd -S 172.16.3.171 -U sa -d bmcs_db -C -i scripts\028_add_delivery_note_bulk_issue_menu.sql
-- =============================================================================

USE bmcs_db;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.menus WHERE menu_code = N'MNU_DN_BULK_ISSUE')
    INSERT INTO dbo.menus
        (menu_code, parent_menu_code, menu_name, display_order, required_permission_level, screen_key,
         created_by, created_at, updated_by, updated_at)
    VALUES
        (N'MNU_DN_BULK_ISSUE', N'MNU_ORDER_SALES', N'納品書一括発行', 4, 1, N'delivery_note_bulk_issue',
         N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME());
GO
