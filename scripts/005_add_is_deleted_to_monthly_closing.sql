-- =============================================================================
-- 005_add_is_deleted_to_monthly_closing.sql
-- monthly_closing に is_deleted 列を追加する。
--
-- 対象: bmcs_db（172.16.3.171）
--
-- 背景: 004番と同じ実装漏れが monthly_closing にも見つかった
-- （Phase 1-6 のエンティティ書き込み検証で発覚）。004 は適用済みのため改変せず、
-- 新しい連番ファイルで追従する。
-- =============================================================================

USE bmcs_db;
GO

IF COL_LENGTH('dbo.monthly_closing', 'is_deleted') IS NULL
BEGIN
    ALTER TABLE dbo.monthly_closing
        ADD is_deleted bit NOT NULL CONSTRAINT DF_monthly_closing_is_deleted DEFAULT (0);
END
GO
