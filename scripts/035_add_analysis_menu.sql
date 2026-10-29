-- =============================================================================
-- 035_add_analysis_menu.sql
-- メインメニューに分類「分析」を新設し、その中に「担当者別売上・粗利」を追加する（TODO.md 9-4）。
-- 「分析」は「月次」の次に置き、以降の分類（マスタ管理・システム管理）の表示順を1つずつ下げる。
-- 014 の再適用はメニューマスタ管理画面での編集内容を失わせるため、変更は本スクリプトで行う（再実行安全）。
--
-- 適用: sqlcmd -S <サーバ> -U <ユーザー> -d bmcs_db -C -I -i scripts\035_add_analysis_menu.sql
-- =============================================================================

USE bmcs_db;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.menus WHERE menu_code = N'MNU_ANALYSIS')
BEGIN
    UPDATE dbo.menus
    SET display_order = display_order + 1, updated_by = N'SEED', updated_at = SYSDATETIME()
    WHERE parent_menu_code IS NULL
      AND display_order > (SELECT display_order FROM dbo.menus WHERE menu_code = N'MNU_MONTHLY');

    INSERT INTO dbo.menus
        (menu_code, parent_menu_code, menu_name, display_order, required_permission_level, screen_key,
         created_by, created_at, updated_by, updated_at)
    SELECT N'MNU_ANALYSIS', NULL, N'分析', display_order + 1, NULL, NULL,
           N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME()
    FROM dbo.menus WHERE menu_code = N'MNU_MONTHLY';
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.menus WHERE menu_code = N'MNU_EMPLOYEE_SALES')
    INSERT INTO dbo.menus
        (menu_code, parent_menu_code, menu_name, display_order, required_permission_level, screen_key,
         created_by, created_at, updated_by, updated_at)
    VALUES
        (N'MNU_EMPLOYEE_SALES', N'MNU_ANALYSIS', N'担当者別売上・粗利', 1, 1, N'employee_sales',
         N'SEED', SYSDATETIME(), N'SEED', SYSDATETIME());
GO
