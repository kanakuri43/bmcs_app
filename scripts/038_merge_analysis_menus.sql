-- =============================================================================
-- 038_merge_analysis_menus.sql
-- 分類「分析」の3項目（担当者別・得意先別・商品別の売上・粗利）を、タブで切り替える1画面
-- 「売上・粗利分析」（screen_key = sales_analysis）にまとめる（再実行安全）。
-- 014 の再適用はメニューマスタ管理画面での編集内容を失わせるため、変更は本スクリプトで行う。
--
-- 適用: sqlcmd -S <サーバ> -U <ユーザー> -d bmcs_db -C -I -i scripts\038_merge_analysis_menus.sql
-- =============================================================================

USE bmcs_db;
GO

-- 担当者別の行を新画面の項目として使い回し、残り2項目を削除する。
UPDATE dbo.menus
SET menu_code = N'MNU_SALES_ANALYSIS', menu_name = N'売上・粗利分析', display_order = 1,
    screen_key = N'sales_analysis', updated_by = N'SEED', updated_at = SYSDATETIME()
WHERE menu_code = N'MNU_EMPLOYEE_SALES'
  AND NOT EXISTS (SELECT 1 FROM dbo.menus WHERE menu_code = N'MNU_SALES_ANALYSIS');
GO

DELETE FROM dbo.menus WHERE menu_code IN (N'MNU_EMPLOYEE_SALES', N'MNU_CUSTOMER_SALES', N'MNU_PRODUCT_SALES');
GO
