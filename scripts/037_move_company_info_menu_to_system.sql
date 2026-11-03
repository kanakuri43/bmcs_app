-- =============================================================================
-- 037_move_company_info_menu_to_system.sql
-- メインメニューの「自社情報」を、分類「マスタ管理」から「システム管理」（MNU_SYSTEM）の末尾へ移す（再実行安全）。
-- マスタ管理だけ項目が多くタイル型メニューで縦に長くなるため。必要権限レベル（9）は変えない。
-- 014 の再適用はメニューマスタ管理画面での編集内容を失わせるため、変更は本スクリプトで行う。
--
-- 適用: sqlcmd -S <サーバ> -U <ユーザー> -d bmcs_db -C -I -i scripts\037_move_company_info_menu_to_system.sql
-- =============================================================================

USE bmcs_db;
GO

UPDATE m
SET parent_menu_code = N'MNU_SYSTEM',
    display_order = (SELECT ISNULL(MAX(s.display_order), 0) + 1 FROM dbo.menus AS s WHERE s.parent_menu_code = N'MNU_SYSTEM'),
    updated_by = N'SEED', updated_at = SYSDATETIME()
FROM dbo.menus AS m
WHERE m.menu_code = N'MNU_COMPANY_INFO' AND m.parent_menu_code <> N'MNU_SYSTEM';
GO
