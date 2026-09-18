-- =============================================================================
-- 017_add_internal_remarks.sql
-- 受注（order_slip）・売上（sales）に社内摘要（internal_remarks）を追加する。
--
-- 対象: bmcs_db（172.16.3.171）
-- 参照: docs/database-schema.md 1章・2.9節・2.15節
--
-- 背景: 既存の摘要（slip_remarks）は納品書に印字される前提のため、社内向けの
-- メモを書く場所がなかった。画面表示のみで帳票には印字しない社内摘要を、
-- slip_remarks と同じ「伝票単位の値（全明細行へ複写）」として追加する
-- （2026-09-18決定）。対象は受注・売上のみで、入金（receipt）・明細入金
-- （detail_receipt）は対象外。
-- =============================================================================

USE bmcs_db;
GO

IF COL_LENGTH('dbo.order_slip', 'internal_remarks') IS NULL
BEGIN
    ALTER TABLE dbo.order_slip ADD internal_remarks nvarchar(200) NULL;
END
GO

IF COL_LENGTH('dbo.sales', 'internal_remarks') IS NULL
BEGIN
    ALTER TABLE dbo.sales ADD internal_remarks nvarchar(200) NULL;
END
GO
