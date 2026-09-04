-- =============================================================================
-- 008_rename_payment_to_receipt.sql
-- 自社が受け取る「入金」を表すテーブル・カラムの命名を payment → receipt に統一する。
-- 「payment（支払）」は自社から他社への支払いを連想させ、実際の意味（自社への入金）
-- と逆であったため。
--
-- 対象: bmcs_db（172.16.3.171）
-- 参照: docs/database-schema.md 2.10節, 2.11節, 3.1節
--
-- リネーム対象:
--   テーブル: payment_tax_unit_invoice → receipt_tax_unit_invoice
--             payment_tax_unit_slip    → receipt_tax_unit_slip
--             detail_payment           → detail_receipt
--   カラム:   payment_slip_number  → receipt_slip_number
--             detail_payment_number → detail_receipt_number
--             payment_date         → receipt_date
--             payment_method       → receipt_method
--             payment_amount       → receipt_amount
--             （billing_tax_unit_invoice / billing_tax_unit_slip の payment_amount も含む）
--   制約:     テーブル名を含む PK/FK/CK/DF 制約名一式
--   データ:   slip_number_sequence.sequence_key の 'payment_slip' / 'detail_payment'
--
-- 注意:
--   - 001〜007 は改変せず、本ファイルで追従する（docs/database-schema.md 3章の運用ルール）。
--   - payment_method カラムは CHECK 制約から参照されているため、SQL Server の制約上
--     sp_rename でそのままリネームできない（Msg 15336）。制約を一旦 DROP し、
--     カラムリネーム後に新しいカラム名で ADD し直す。
--   - ALTER TABLE ADD CONSTRAINT は同一バッチ内でリネーム直後のカラムを解決できない
--     （コンパイル時に旧スキーマで名前解決されるため）ため、GO でバッチを分ける。
-- =============================================================================

USE bmcs_db;
GO

-- -----------------------------------------------------------------------------
-- 1. payment_tax_unit_invoice → receipt_tax_unit_invoice
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.payment_tax_unit_invoice', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.receipt_tax_unit_invoice', N'U') IS NULL
BEGIN
    EXEC sp_rename 'dbo.payment_tax_unit_invoice', 'receipt_tax_unit_invoice';
END
GO

IF OBJECT_ID(N'dbo.CK_payment_tax_unit_invoice_method', N'C') IS NOT NULL
BEGIN
    ALTER TABLE dbo.receipt_tax_unit_invoice DROP CONSTRAINT CK_payment_tax_unit_invoice_method;
END
GO

IF COL_LENGTH('dbo.receipt_tax_unit_invoice', 'payment_slip_number') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.receipt_tax_unit_invoice.payment_slip_number', 'receipt_slip_number', 'COLUMN';
    EXEC sp_rename 'dbo.receipt_tax_unit_invoice.payment_date', 'receipt_date', 'COLUMN';
    EXEC sp_rename 'dbo.receipt_tax_unit_invoice.payment_method', 'receipt_method', 'COLUMN';
    EXEC sp_rename 'dbo.receipt_tax_unit_invoice.payment_amount', 'receipt_amount', 'COLUMN';
END
GO

IF OBJECT_ID(N'dbo.CK_receipt_tax_unit_invoice_method', N'C') IS NULL
BEGIN
    ALTER TABLE dbo.receipt_tax_unit_invoice ADD CONSTRAINT CK_receipt_tax_unit_invoice_method CHECK (receipt_method IN (1, 2, 3, 4));
END
GO

IF OBJECT_ID(N'dbo.DF_payment_tax_unit_invoice_is_deleted', N'D') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.DF_payment_tax_unit_invoice_is_deleted', 'DF_receipt_tax_unit_invoice_is_deleted', 'OBJECT';
    EXEC sp_rename 'dbo.PK_payment_tax_unit_invoice', 'PK_receipt_tax_unit_invoice', 'OBJECT';
    EXEC sp_rename 'dbo.FK_payment_tax_unit_invoice_customer', 'FK_receipt_tax_unit_invoice_customer', 'OBJECT';
    EXEC sp_rename 'dbo.FK_payment_tax_unit_invoice_bank_account', 'FK_receipt_tax_unit_invoice_bank_account', 'OBJECT';
    EXEC sp_rename 'dbo.FK_payment_tax_unit_invoice_billing', 'FK_receipt_tax_unit_invoice_billing', 'OBJECT';
    EXEC sp_rename 'dbo.CK_payment_tax_unit_invoice_allocation_status', 'CK_receipt_tax_unit_invoice_allocation_status', 'OBJECT';
END
GO

-- -----------------------------------------------------------------------------
-- 2. payment_tax_unit_slip → receipt_tax_unit_slip
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.payment_tax_unit_slip', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.receipt_tax_unit_slip', N'U') IS NULL
BEGIN
    EXEC sp_rename 'dbo.payment_tax_unit_slip', 'receipt_tax_unit_slip';
END
GO

IF OBJECT_ID(N'dbo.CK_payment_tax_unit_slip_method', N'C') IS NOT NULL
BEGIN
    ALTER TABLE dbo.receipt_tax_unit_slip DROP CONSTRAINT CK_payment_tax_unit_slip_method;
END
GO

IF COL_LENGTH('dbo.receipt_tax_unit_slip', 'payment_slip_number') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.receipt_tax_unit_slip.payment_slip_number', 'receipt_slip_number', 'COLUMN';
    EXEC sp_rename 'dbo.receipt_tax_unit_slip.payment_date', 'receipt_date', 'COLUMN';
    EXEC sp_rename 'dbo.receipt_tax_unit_slip.payment_method', 'receipt_method', 'COLUMN';
    EXEC sp_rename 'dbo.receipt_tax_unit_slip.payment_amount', 'receipt_amount', 'COLUMN';
END
GO

IF OBJECT_ID(N'dbo.CK_receipt_tax_unit_slip_method', N'C') IS NULL
BEGIN
    ALTER TABLE dbo.receipt_tax_unit_slip ADD CONSTRAINT CK_receipt_tax_unit_slip_method CHECK (receipt_method IN (1, 2, 3, 4));
END
GO

IF OBJECT_ID(N'dbo.DF_payment_tax_unit_slip_is_deleted', N'D') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.DF_payment_tax_unit_slip_is_deleted', 'DF_receipt_tax_unit_slip_is_deleted', 'OBJECT';
    EXEC sp_rename 'dbo.PK_payment_tax_unit_slip', 'PK_receipt_tax_unit_slip', 'OBJECT';
    EXEC sp_rename 'dbo.FK_payment_tax_unit_slip_customer', 'FK_receipt_tax_unit_slip_customer', 'OBJECT';
    EXEC sp_rename 'dbo.FK_payment_tax_unit_slip_bank_account', 'FK_receipt_tax_unit_slip_bank_account', 'OBJECT';
    EXEC sp_rename 'dbo.FK_payment_tax_unit_slip_billing', 'FK_receipt_tax_unit_slip_billing', 'OBJECT';
    EXEC sp_rename 'dbo.CK_payment_tax_unit_slip_allocation_status', 'CK_receipt_tax_unit_slip_allocation_status', 'OBJECT';
END
GO

-- -----------------------------------------------------------------------------
-- 3. detail_payment → detail_receipt
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.detail_payment', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.detail_receipt', N'U') IS NULL
BEGIN
    EXEC sp_rename 'dbo.detail_payment', 'detail_receipt';
END
GO

IF OBJECT_ID(N'dbo.CK_detail_payment_method', N'C') IS NOT NULL
BEGIN
    ALTER TABLE dbo.detail_receipt DROP CONSTRAINT CK_detail_payment_method;
END
GO

IF COL_LENGTH('dbo.detail_receipt', 'detail_payment_number') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.detail_receipt.detail_payment_number', 'detail_receipt_number', 'COLUMN';
    EXEC sp_rename 'dbo.detail_receipt.payment_date', 'receipt_date', 'COLUMN';
    EXEC sp_rename 'dbo.detail_receipt.payment_method', 'receipt_method', 'COLUMN';
    EXEC sp_rename 'dbo.detail_receipt.payment_amount', 'receipt_amount', 'COLUMN';
END
GO

IF OBJECT_ID(N'dbo.CK_detail_receipt_method', N'C') IS NULL
BEGIN
    ALTER TABLE dbo.detail_receipt ADD CONSTRAINT CK_detail_receipt_method CHECK (receipt_method IN (1, 2, 3, 4));
END
GO

IF OBJECT_ID(N'dbo.DF_detail_payment_is_deleted', N'D') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.DF_detail_payment_is_deleted', 'DF_detail_receipt_is_deleted', 'OBJECT';
    EXEC sp_rename 'dbo.PK_detail_payment', 'PK_detail_receipt', 'OBJECT';
    EXEC sp_rename 'dbo.FK_detail_payment_customer', 'FK_detail_receipt_customer', 'OBJECT';
    EXEC sp_rename 'dbo.FK_detail_payment_bank_account', 'FK_detail_receipt_bank_account', 'OBJECT';
    EXEC sp_rename 'dbo.FK_detail_payment_sales_line', 'FK_detail_receipt_sales_line', 'OBJECT';
    EXEC sp_rename 'dbo.FK_detail_payment_detail_invoice', 'FK_detail_receipt_detail_invoice', 'OBJECT';
    EXEC sp_rename 'dbo.CK_detail_payment_allocation_status', 'CK_detail_receipt_allocation_status', 'OBJECT';
    EXEC sp_rename 'dbo.CK_detail_payment_target_type', 'CK_detail_receipt_target_type', 'OBJECT';
    EXEC sp_rename 'dbo.CK_detail_payment_target', 'CK_detail_receipt_target', 'OBJECT';
END
GO

-- -----------------------------------------------------------------------------
-- 4. billing_tax_unit_invoice / billing_tax_unit_slip の payment_amount → receipt_amount
-- -----------------------------------------------------------------------------
IF COL_LENGTH('dbo.billing_tax_unit_invoice', 'payment_amount') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.billing_tax_unit_invoice.payment_amount', 'receipt_amount', 'COLUMN';
END
GO

IF COL_LENGTH('dbo.billing_tax_unit_slip', 'payment_amount') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.billing_tax_unit_slip.payment_amount', 'receipt_amount', 'COLUMN';
END
GO

-- -----------------------------------------------------------------------------
-- 5. slip_number_sequence.sequence_key の値更新
-- -----------------------------------------------------------------------------
UPDATE dbo.slip_number_sequence SET sequence_key = N'receipt_slip' WHERE sequence_key = N'payment_slip';
UPDATE dbo.slip_number_sequence SET sequence_key = N'detail_receipt' WHERE sequence_key = N'detail_payment';
GO
