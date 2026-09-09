-- =============================================================================
-- 011_add_slip_and_line_remarks.sql
-- ジャーナル系テーブル（sales / receipt / detail_receipt / order_slip）に
-- 伝票摘要（slip_remarks）・行摘要（line_remarks）を追加する。
--
-- 対象: bmcs_db（172.16.3.171）
-- 参照: docs/database-schema.md 1章・2.9〜2.11節・2.15節
--
-- 背景: 各画面の明細行には従来から行摘要欄の枠があったが、対応する列が
-- どのジャーナルテーブルにも存在せず「枠のみ・使用不可」だった
-- （docs/design_document.md 5章）。2026-09-09、ジャーナル系テーブルは
-- 伝票摘要・行摘要の両方を持つ方針を決定。伝票摘要は自由記述のメモで、
-- slip_date / customer_code と同じ「伝票単位の値」として同一伝票の全行に
-- 複写する。ヘッダーのみの集計テーブル（billing / detail_invoice）は
-- ジャーナルではないため対象外（docs/database-schema.md 2.8節）。
-- =============================================================================

USE bmcs_db;
GO

IF COL_LENGTH('dbo.sales', 'slip_remarks') IS NULL
BEGIN
    ALTER TABLE dbo.sales ADD slip_remarks nvarchar(200) NULL;
END
GO

IF COL_LENGTH('dbo.sales', 'line_remarks') IS NULL
BEGIN
    ALTER TABLE dbo.sales ADD line_remarks nvarchar(100) NULL;
END
GO

IF COL_LENGTH('dbo.receipt', 'slip_remarks') IS NULL
BEGIN
    ALTER TABLE dbo.receipt ADD slip_remarks nvarchar(200) NULL;
END
GO

IF COL_LENGTH('dbo.receipt', 'line_remarks') IS NULL
BEGIN
    ALTER TABLE dbo.receipt ADD line_remarks nvarchar(100) NULL;
END
GO

IF COL_LENGTH('dbo.detail_receipt', 'slip_remarks') IS NULL
BEGIN
    ALTER TABLE dbo.detail_receipt ADD slip_remarks nvarchar(200) NULL;
END
GO

IF COL_LENGTH('dbo.detail_receipt', 'line_remarks') IS NULL
BEGIN
    ALTER TABLE dbo.detail_receipt ADD line_remarks nvarchar(100) NULL;
END
GO

IF COL_LENGTH('dbo.order_slip', 'slip_remarks') IS NULL
BEGIN
    ALTER TABLE dbo.order_slip ADD slip_remarks nvarchar(200) NULL;
END
GO

IF COL_LENGTH('dbo.order_slip', 'line_remarks') IS NULL
BEGIN
    ALTER TABLE dbo.order_slip ADD line_remarks nvarchar(100) NULL;
END
GO
