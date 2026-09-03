-- =============================================================================
-- 006_remove_non_taxable_category.sql
-- 税種別区分から「不課税」(4) を削除し、課税10%／軽減8%／非課税の3区分のみとする。
--
-- 対象: bmcs_db（172.16.3.171）
--
-- 背景: 税種別区分は正しくは「非課税」のみが存在し、「不課税」という区分は
-- 実在しない誤りだった（業務側からの指摘）。以下を行う。
--   1. 既存データに残る tax_category = 4（不課税）の行を削除する
--      （新しい CHECK 制約を追加する前に、違反データを取り除く必要がある）。
--   2. product / order_slip / sales_tax_unit_invoice / sales_tax_unit_slip /
--      sales_tax_unit_line の tax_category CHECK 制約を IN (1,2,3,4) から
--      IN (1,2,3) へ締め直す。
--   3. billing_tax_unit_invoice / billing_tax_unit_slip / detail_invoice の
--      non_taxable_amount 列（不課税の対価額）を削除する。
--
-- 注意: 001_create_master_tables.sql / 002_create_voucher_tables.sql は
-- 改変せず、新しい連番ファイルで追従する（docs/database-schema.md 1章の運用ルール）。
-- =============================================================================

USE bmcs_db;
GO

-- -----------------------------------------------------------------------------
-- 1. 既存の tax_category = 4（不課税）データを削除する
--    現状は開発用シードデータの PRD004（「海外送料（不課税）」）のみが該当し、
--    他テーブルからは参照されていない。
-- -----------------------------------------------------------------------------
DELETE FROM dbo.product WHERE tax_category = 4;
GO
DELETE FROM dbo.order_slip WHERE tax_category = 4;
GO
DELETE FROM dbo.sales_tax_unit_invoice WHERE tax_category = 4;
GO
DELETE FROM dbo.sales_tax_unit_slip WHERE tax_category = 4;
GO
DELETE FROM dbo.sales_tax_unit_line WHERE tax_category = 4;
GO

-- -----------------------------------------------------------------------------
-- 2. tax_category CHECK 制約を IN (1, 2, 3) に締め直す
-- -----------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_product_tax_category')
BEGIN
    ALTER TABLE dbo.product DROP CONSTRAINT CK_product_tax_category;
END
GO
ALTER TABLE dbo.product WITH CHECK
    ADD CONSTRAINT CK_product_tax_category CHECK (tax_category IN (1, 2, 3));
GO

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_order_slip_tax_category')
BEGIN
    ALTER TABLE dbo.order_slip DROP CONSTRAINT CK_order_slip_tax_category;
END
GO
ALTER TABLE dbo.order_slip WITH CHECK
    ADD CONSTRAINT CK_order_slip_tax_category CHECK (tax_category IN (1, 2, 3));
GO

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_sales_tax_unit_invoice_tax_category')
BEGIN
    ALTER TABLE dbo.sales_tax_unit_invoice DROP CONSTRAINT CK_sales_tax_unit_invoice_tax_category;
END
GO
ALTER TABLE dbo.sales_tax_unit_invoice WITH CHECK
    ADD CONSTRAINT CK_sales_tax_unit_invoice_tax_category CHECK (tax_category IN (1, 2, 3));
GO

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_sales_tax_unit_slip_tax_category')
BEGIN
    ALTER TABLE dbo.sales_tax_unit_slip DROP CONSTRAINT CK_sales_tax_unit_slip_tax_category;
END
GO
ALTER TABLE dbo.sales_tax_unit_slip WITH CHECK
    ADD CONSTRAINT CK_sales_tax_unit_slip_tax_category CHECK (tax_category IN (1, 2, 3));
GO

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_sales_tax_unit_line_tax_category')
BEGIN
    ALTER TABLE dbo.sales_tax_unit_line DROP CONSTRAINT CK_sales_tax_unit_line_tax_category;
END
GO
ALTER TABLE dbo.sales_tax_unit_line WITH CHECK
    ADD CONSTRAINT CK_sales_tax_unit_line_tax_category CHECK (tax_category IN (1, 2, 3));
GO

-- -----------------------------------------------------------------------------
-- 3. non_taxable_amount 列（不課税の対価額）を削除する
-- -----------------------------------------------------------------------------
IF COL_LENGTH('dbo.billing_tax_unit_invoice', 'non_taxable_amount') IS NOT NULL
BEGIN
    ALTER TABLE dbo.billing_tax_unit_invoice DROP COLUMN non_taxable_amount;
END
GO

IF COL_LENGTH('dbo.billing_tax_unit_slip', 'non_taxable_amount') IS NOT NULL
BEGIN
    ALTER TABLE dbo.billing_tax_unit_slip DROP COLUMN non_taxable_amount;
END
GO

IF COL_LENGTH('dbo.detail_invoice', 'non_taxable_amount') IS NOT NULL
BEGIN
    ALTER TABLE dbo.detail_invoice DROP COLUMN non_taxable_amount;
END
GO
