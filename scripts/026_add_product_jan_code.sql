-- =============================================================================
-- 026_add_product_jan_code.sql
-- products に JANコード（jan_code）を追加する。商品マスタ検索の対象にする。
--
-- 対象: bmcs_db（172.16.3.171）
-- 参照: docs/database-schema.md 2.2節
-- 適用: sqlcmd -S <サーバ> -U <ユーザー> -d bmcs_db -C -I -i scripts\026_add_product_jan_code.sql
-- =============================================================================

USE bmcs_db;
GO

SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH(N'dbo.products', N'jan_code') IS NULL
BEGIN
    ALTER TABLE dbo.products ADD jan_code varchar(13) NULL;
END
GO
