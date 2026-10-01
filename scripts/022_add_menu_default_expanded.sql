-- =============================================================================
-- 022_add_menu_default_expanded.sql
-- メニュー構成マスタ（menus）に、メインメニューのカテゴリ（親）を初期状態で
-- 開くか閉じるかを指定する列（is_default_expanded）を追加する。
--
-- 対象: bmcs_db
-- 参照: docs/database-schema.md 2.7節
--
-- 既定値は 1（開く）。既存の行はすべて 1 になるため、適用しただけでは
-- メインメニューの見え方は変わらない。閉じたいカテゴリは、次のように 0 にする。
--   UPDATE dbo.menus SET is_default_expanded = 0 WHERE menu_code = N'MNU_MASTER';
-- この列は親（カテゴリ）の行だけが参照する。子（機能メニュー）の行の値は使われない。
-- =============================================================================

USE bmcs_db;
GO

IF COL_LENGTH('dbo.menus', 'is_default_expanded') IS NULL
BEGIN
    ALTER TABLE dbo.menus
        ADD is_default_expanded bit NOT NULL
            CONSTRAINT DF_menus_is_default_expanded DEFAULT 1;
END
GO
