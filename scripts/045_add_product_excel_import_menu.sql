-- =============================================================================
-- 045_add_product_excel_import_menu.sql
-- メインメニュー「システム設定」に「商品マスタExcel取込」を追加する（権限8）。
-- 014 の再適用はメニューマスタ管理画面での編集内容を失わせるため、専用の連番SQLで追加する（041 と同じ方式）。
--
-- 対象: bmcs_db（172.16.3.171）
-- 参照: docs/design_document.md 38章
-- 適用: sqlcmd -S <サーバ> -U <ユーザー> -d bmcs_db -C -I -i scripts\045_add_product_excel_import_menu.sql
-- =============================================================================

USE bmcs_db;
GO

SET QUOTED_IDENTIFIER ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.menus WHERE menu_code = N'MNU_PRODUCT_IMPORT')
BEGIN
    INSERT INTO dbo.menus
        (menu_code, parent_menu_code, menu_name, display_order, required_permission_level, screen_key,
         created_by, created_at, updated_by, updated_at)
    VALUES
        (N'MNU_PRODUCT_IMPORT', N'MNU_SYSTEM', N'商品マスタExcel取込',
         (SELECT ISNULL(MAX(display_order), 0) + 1 FROM dbo.menus WHERE parent_menu_code = N'MNU_SYSTEM'),
         8, N'product_excel_import', N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME());
END
GO
