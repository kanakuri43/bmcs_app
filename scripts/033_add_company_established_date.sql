-- =============================================================================
-- 033_add_company_established_date.sql
-- company_infos に設立年月日（established_date）を追加する（NULL許容、再実行安全）。
--
-- 対象: bmcs_db（172.16.3.171）
-- 参照: docs/database-schema.md 2.5節
-- 適用: sqlcmd -S <サーバ> -U <ユーザー> -d bmcs_db -C -I -i scripts\033_add_company_established_date.sql
-- =============================================================================

USE bmcs_db;
GO

SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH(N'dbo.company_infos', N'established_date') IS NULL
BEGIN
    ALTER TABLE dbo.company_infos ADD established_date date NULL;
END
GO
