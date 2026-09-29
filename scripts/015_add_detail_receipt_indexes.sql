-- =============================================================================
-- 015_add_detail_receipt_indexes.sql
-- detail_receipt に非クラスター化インデックスを3本追加する（TODO.md 7-1）。
--
-- 対象: bmcs_db（172.16.3.171）
-- 参照: docs/database-schema.md 2.11節
--
-- 背景: 010_unify_tax_unit_tables.sql で sales/receipt/billing に主要クエリ用の
-- インデックスを追加したが、detail_receipt は対象外のままPK以外の索引を持たない。
-- 本タスク（消込サービス）で得意先単位に detail_receipt を読む主クエリ、および
-- 既存の FK（FK_detail_receipt_sales／FK_detail_receipt_detail_invoice）の子側索引が
-- 欠落しているため、ここで追加する。
--
-- フィルター付きインデックス（WHERE句付き）は使わない
-- （010_unify_tax_unit_tables.sql 9節と同じ理由。sqlcmd既定のQUOTED_IDENTIFIER OFFでは
-- DMLが失敗するため）。
-- =============================================================================

USE bmcs_db;
GO

-- SettlementService.RecalculateForBillingGroupAsync の主クエリ（得意先×入金日で絞る）。
-- receipt の IX_receipt_customer_code_receipt_date と対になる。
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_detail_receipt_customer_code_receipt_date' AND object_id = OBJECT_ID(N'dbo.detail_receipt'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_detail_receipt_customer_code_receipt_date
        ON dbo.detail_receipt (customer_code, receipt_date);
END
GO

-- FK_detail_receipt_sales (target_sales_slip_number, target_sales_line_number) の子側索引。
-- 現在は索引が無く、売上明細行を直接指定した明細入金の有無確認がスキャンになる。
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_detail_receipt_target_sales' AND object_id = OBJECT_ID(N'dbo.detail_receipt'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_detail_receipt_target_sales
        ON dbo.detail_receipt (target_sales_slip_number, target_sales_line_number);
END
GO

-- FK_detail_receipt_detail_invoice (target_detail_invoice_number) の子側索引。
-- DetailInvoiceService.CancelAsync の「この明細請求書を指定した明細入金があるか」の
-- ガードクエリで使う。
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_detail_receipt_target_detail_invoice' AND object_id = OBJECT_ID(N'dbo.detail_receipt'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_detail_receipt_target_detail_invoice
        ON dbo.detail_receipt (target_detail_invoice_number);
END
GO
