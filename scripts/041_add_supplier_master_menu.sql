-- =============================================================================
-- 041_add_supplier_master_menu.sql
-- メインメニュー「マスタ管理」に「仕入先マスタ」を追加する。
-- 014 の再適用はメニューマスタ管理画面での編集内容を失わせるため、専用の連番SQLで追加する（039 と同じ方式）。
--
-- 対象: bmcs_db（172.16.3.171）
-- 参照: docs/design_document.md 36章
-- 適用: sqlcmd -S <サーバ> -U <ユーザー> -d bmcs_db -C -I -i scripts\041_add_supplier_master_menu.sql
-- =============================================================================

USE bmcs_db;
GO

SET QUOTED_IDENTIFIER ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.menus WHERE menu_code = N'MNU_SUPPLIER_MASTER')
BEGIN
    INSERT INTO dbo.menus
        (menu_code, parent_menu_code, menu_name, display_order, required_permission_level, screen_key,
         created_by, created_at, updated_by, updated_at)
    VALUES
        (N'MNU_SUPPLIER_MASTER', N'MNU_MASTER', N'仕入先マスタ',
         (SELECT ISNULL(MAX(display_order), 0) + 1 FROM dbo.menus WHERE parent_menu_code = N'MNU_MASTER'),
         1, N'supplier_master', N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME());
END
GO
