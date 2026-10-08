-- =============================================================================
-- 025_drop_copier_machine_model.sql
-- copier_machines.machine_model（機種名）を削除する。
-- 機種名はCSVから取り込み、売上の社外摘要にCSVの値を使うため、マスタでは持たない。
--
-- 対象: bmcs_db（172.16.3.171）
-- 参照: docs/database-schema.md 2.6-2節
-- 適用: sqlcmd -S <サーバ> -U <ユーザー> -d bmcs_db -C -I -i scripts\025_drop_copier_machine_model.sql
-- =============================================================================

USE bmcs_db;
GO

SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH(N'dbo.copier_machines', N'machine_model') IS NOT NULL
BEGIN
    -- 列に紐づくDEFAULT制約があれば先に落とす
    DECLARE @df sysname = (
        SELECT dc.name
        FROM sys.default_constraints dc
        JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
        WHERE dc.parent_object_id = OBJECT_ID(N'dbo.copier_machines') AND c.name = N'machine_model');
    IF @df IS NOT NULL
        EXEC (N'ALTER TABLE dbo.copier_machines DROP CONSTRAINT ' + @df);

    ALTER TABLE dbo.copier_machines DROP COLUMN machine_model;
END
GO
