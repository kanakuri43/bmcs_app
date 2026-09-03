-- =============================================================================
-- 004_add_is_deleted_to_billing_and_detail_invoice.sql
-- billing_tax_unit_invoice / billing_tax_unit_slip / detail_invoice に
-- is_deleted 列を追加する。
--
-- 対象: bmcs_db（172.16.3.171）
--
-- 背景: docs/database-schema.md 2.0 節は「全テーブルが持つ共通カラム」として
-- is_deleted を含む6列を定義し、例外は detail_invoice_sales_line と
-- slip_number_sequence の2つのみとしている。しかし 002_create_voucher_tables.sql
-- 作成時にこの3テーブルへの is_deleted 追加が漏れていた（実装ミス）。
-- Phase 1-6（EF Core エンティティ実装）でエンティティが IsDeleted を持つ前提の
-- ため実際に書き込みを試みて発覚した。
--
-- 注意: 002_create_voucher_tables.sql は改変せず、新しい連番ファイルで追従する
-- （docs/database-schema.md 1章の運用ルール）。
-- =============================================================================

USE bmcs_db;
GO

IF COL_LENGTH('dbo.billing_tax_unit_invoice', 'is_deleted') IS NULL
BEGIN
    ALTER TABLE dbo.billing_tax_unit_invoice
        ADD is_deleted bit NOT NULL CONSTRAINT DF_billing_tax_unit_invoice_is_deleted DEFAULT (0);
END
GO

IF COL_LENGTH('dbo.billing_tax_unit_slip', 'is_deleted') IS NULL
BEGIN
    ALTER TABLE dbo.billing_tax_unit_slip
        ADD is_deleted bit NOT NULL CONSTRAINT DF_billing_tax_unit_slip_is_deleted DEFAULT (0);
END
GO

IF COL_LENGTH('dbo.detail_invoice', 'is_deleted') IS NULL
BEGIN
    ALTER TABLE dbo.detail_invoice
        ADD is_deleted bit NOT NULL CONSTRAINT DF_detail_invoice_is_deleted DEFAULT (0);
END
GO
