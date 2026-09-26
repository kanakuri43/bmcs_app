-- =============================================================================
-- 019_pluralize_table_names.sql
-- テーブル名を単数形から複数形に統一する。EF Core の DbSet 命名（複数形）と
-- DB のテーブル名（単数形）が食い違っていたため。制約名・インデックス名に
-- 含まれる「所有テーブル名」部分も同時に揃える（テーブル名だけ複数形・
-- 制約名は単数形という中途半端な状態を作らない）。
--
-- 対象: bmcs_db（172.16.3.171）
-- 参照: docs/database-schema.md 2章（各テーブル節）, 3.1節（命名規則）
--
-- リネーム対象（テーブル17件、sales は対象外）:
--   bank_account               → bank_accounts
--   billing                    → billings
--   company_info               → company_infos
--   customer                   → customers
--   deposit_method             → deposit_methods
--   detail_invoice             → detail_invoices
--   detail_invoice_sales_line  → detail_invoice_sales_lines
--   detail_receipt             → detail_receipts
--   employee                   → employees
--   menu                       → menus
--   monthly_closing            → monthly_closings
--   order_slip                 → orders   （単純な複数形化ではなく別名。業務上の呼称に合わせる）
--   product                    → products
--   receipt                    → receipts
--   receipt_allocation         → receipt_allocations
--   slip_number_sequence       → slip_number_sequences
--   tax_rate_master            → tax_rates（単純な複数形化ではなく別名。業務上の呼称に合わせる）
--
--   制約: 上記テーブルを所有する PK/CK/DF/UQ 制約58件、FK 23件（末尾の参照先
--         テーブル名も複数形化。自己参照の FK_menu_parent_menu は末尾が役割名の
--         ため不変）
--   インデックス: 名前付きインデックス9件（フィルタ付き一意インデックス
--         UQ_billing_customer_closing_ym_confirmed を含む。制約ではないため
--         'INDEX' で sp_rename する）
--
-- 変更しないもの:
--   - カラム名（customer_code / billing_number 等）
--   - slip_number_sequence.sequence_key のデータ値
--   - sales テーブルおよび sales 所有の全オブジェクト（PK_sales / CK_sales_* 7件 /
--     DF_sales_is_deleted / IX_sales_* 3件）
--
-- 注意:
--   - 001〜018 は改変せず、本ファイルで追従する（docs/database-schema.md 3.2節の運用ルール）。
--   - 実行順序はテーブル → 制約 → FK → インデックスの順。インデックスのリネームは
--     `sp_rename 'dbo.<新テーブル名>.<旧インデックス名>', ...` の形式で新テーブル名を
--     要求するため、テーブルリネームを先に済ませる必要がある。
--   - `sp_rename` によるインデックスリネーム（第3引数 'INDEX'）は本リポジトリで
--     初めて使うイディオム。008 は制約のみで索引リネームの前例がない。
--   - このDBにはビュー・ストアドプロシージャ・関数・トリガー・シノニムが存在しない
--     ため、sp_rename によって壊れる依存オブジェクトはない（2026-09-26 時点で確認済み）。
-- =============================================================================

USE bmcs_db;
GO

-- -----------------------------------------------------------------------------
-- 1. テーブルリネーム（17件）
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.bank_account', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.bank_accounts', N'U') IS NULL
BEGIN
    EXEC sp_rename 'dbo.bank_account', 'bank_accounts';
END
GO

IF OBJECT_ID(N'dbo.billing', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.billings', N'U') IS NULL
BEGIN
    EXEC sp_rename 'dbo.billing', 'billings';
END
GO

IF OBJECT_ID(N'dbo.company_info', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.company_infos', N'U') IS NULL
BEGIN
    EXEC sp_rename 'dbo.company_info', 'company_infos';
END
GO

IF OBJECT_ID(N'dbo.customer', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.customers', N'U') IS NULL
BEGIN
    EXEC sp_rename 'dbo.customer', 'customers';
END
GO

IF OBJECT_ID(N'dbo.deposit_method', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.deposit_methods', N'U') IS NULL
BEGIN
    EXEC sp_rename 'dbo.deposit_method', 'deposit_methods';
END
GO

IF OBJECT_ID(N'dbo.detail_invoice', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.detail_invoices', N'U') IS NULL
BEGIN
    EXEC sp_rename 'dbo.detail_invoice', 'detail_invoices';
END
GO

IF OBJECT_ID(N'dbo.detail_invoice_sales_line', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.detail_invoice_sales_lines', N'U') IS NULL
BEGIN
    EXEC sp_rename 'dbo.detail_invoice_sales_line', 'detail_invoice_sales_lines';
END
GO

IF OBJECT_ID(N'dbo.detail_receipt', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.detail_receipts', N'U') IS NULL
BEGIN
    EXEC sp_rename 'dbo.detail_receipt', 'detail_receipts';
END
GO

IF OBJECT_ID(N'dbo.employee', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.employees', N'U') IS NULL
BEGIN
    EXEC sp_rename 'dbo.employee', 'employees';
END
GO

IF OBJECT_ID(N'dbo.menu', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.menus', N'U') IS NULL
BEGIN
    EXEC sp_rename 'dbo.menu', 'menus';
END
GO

IF OBJECT_ID(N'dbo.monthly_closing', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.monthly_closings', N'U') IS NULL
BEGIN
    EXEC sp_rename 'dbo.monthly_closing', 'monthly_closings';
END
GO

IF OBJECT_ID(N'dbo.order_slip', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.orders', N'U') IS NULL
BEGIN
    EXEC sp_rename 'dbo.order_slip', 'orders';
END
GO

IF OBJECT_ID(N'dbo.product', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.products', N'U') IS NULL
BEGIN
    EXEC sp_rename 'dbo.product', 'products';
END
GO

IF OBJECT_ID(N'dbo.receipt', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.receipts', N'U') IS NULL
BEGIN
    EXEC sp_rename 'dbo.receipt', 'receipts';
END
GO

IF OBJECT_ID(N'dbo.receipt_allocation', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.receipt_allocations', N'U') IS NULL
BEGIN
    EXEC sp_rename 'dbo.receipt_allocation', 'receipt_allocations';
END
GO

IF OBJECT_ID(N'dbo.slip_number_sequence', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.slip_number_sequences', N'U') IS NULL
BEGIN
    EXEC sp_rename 'dbo.slip_number_sequence', 'slip_number_sequences';
END
GO

IF OBJECT_ID(N'dbo.tax_rate_master', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.tax_rates', N'U') IS NULL
BEGIN
    EXEC sp_rename 'dbo.tax_rate_master', 'tax_rates';
END
GO

-- -----------------------------------------------------------------------------
-- 2. PK / CK / DF / UQ 制約のリネーム（58件、テーブル単位でグループ化）
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.PK_bank_account', N'PK') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.CK_bank_account_type', 'CK_bank_accounts_type', 'OBJECT';
    EXEC sp_rename 'dbo.DF_bank_account_is_deleted', 'DF_bank_accounts_is_deleted', 'OBJECT';
    EXEC sp_rename 'dbo.PK_bank_account', 'PK_bank_accounts', 'OBJECT';
END
GO

IF OBJECT_ID(N'dbo.PK_billing', N'PK') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.CK_billing_status', 'CK_billings_status', 'OBJECT';
    EXEC sp_rename 'dbo.CK_billing_tax_unit', 'CK_billings_tax_unit', 'OBJECT';
    EXEC sp_rename 'dbo.DF_billing_is_deleted', 'DF_billings_is_deleted', 'OBJECT';
    EXEC sp_rename 'dbo.UQ_billing_number_tax_unit', 'UQ_billings_number_tax_unit', 'OBJECT';
    EXEC sp_rename 'dbo.PK_billing', 'PK_billings', 'OBJECT';
END
GO

IF OBJECT_ID(N'dbo.PK_company_info', N'PK') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.CK_company_info_single_row', 'CK_company_infos_single_row', 'OBJECT';
    EXEC sp_rename 'dbo.DF_company_info_is_deleted', 'DF_company_infos_is_deleted', 'OBJECT';
    EXEC sp_rename 'dbo.PK_company_info', 'PK_company_infos', 'OBJECT';
END
GO

IF OBJECT_ID(N'dbo.PK_customer', N'PK') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.CK_customer_closing_day', 'CK_customers_closing_day', 'OBJECT';
    EXEC sp_rename 'dbo.CK_customer_rounding_type', 'CK_customers_rounding_type', 'OBJECT';
    EXEC sp_rename 'dbo.CK_customer_tax_unit', 'CK_customers_tax_unit', 'OBJECT';
    EXEC sp_rename 'dbo.CK_customer_tax_unit_closing_day', 'CK_customers_tax_unit_closing_day', 'OBJECT';
    EXEC sp_rename 'dbo.DF_customer_is_deleted', 'DF_customers_is_deleted', 'OBJECT';
    EXEC sp_rename 'dbo.UQ_customer_code_tax_unit', 'UQ_customers_code_tax_unit', 'OBJECT';
    EXEC sp_rename 'dbo.PK_customer', 'PK_customers', 'OBJECT';
END
GO

IF OBJECT_ID(N'dbo.PK_deposit_method', N'PK') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.CK_deposit_method_requires', 'CK_deposit_methods_requires', 'OBJECT';
    EXEC sp_rename 'dbo.DF_deposit_method_is_deleted', 'DF_deposit_methods_is_deleted', 'OBJECT';
    EXEC sp_rename 'dbo.PK_deposit_method', 'PK_deposit_methods', 'OBJECT';
END
GO

IF OBJECT_ID(N'dbo.PK_detail_invoice', N'PK') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.CK_detail_invoice_status', 'CK_detail_invoices_status', 'OBJECT';
    EXEC sp_rename 'dbo.DF_detail_invoice_is_deleted', 'DF_detail_invoices_is_deleted', 'OBJECT';
    EXEC sp_rename 'dbo.PK_detail_invoice', 'PK_detail_invoices', 'OBJECT';
END
GO

IF OBJECT_ID(N'dbo.PK_detail_invoice_sales_line', N'PK') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.UQ_detail_invoice_sales_line_sales_line', 'UQ_detail_invoice_sales_lines_sales_line', 'OBJECT';
    EXEC sp_rename 'dbo.PK_detail_invoice_sales_line', 'PK_detail_invoice_sales_lines', 'OBJECT';
END
GO

IF OBJECT_ID(N'dbo.PK_detail_receipt', N'PK') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.CK_detail_receipt_allocation_status', 'CK_detail_receipts_allocation_status', 'OBJECT';
    EXEC sp_rename 'dbo.CK_detail_receipt_target', 'CK_detail_receipts_target', 'OBJECT';
    EXEC sp_rename 'dbo.CK_detail_receipt_target_type', 'CK_detail_receipts_target_type', 'OBJECT';
    EXEC sp_rename 'dbo.DF_detail_receipt_is_deleted', 'DF_detail_receipts_is_deleted', 'OBJECT';
    EXEC sp_rename 'dbo.PK_detail_receipt', 'PK_detail_receipts', 'OBJECT';
END
GO

IF OBJECT_ID(N'dbo.PK_employee', N'PK') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.DF_employee_is_deleted', 'DF_employees_is_deleted', 'OBJECT';
    EXEC sp_rename 'dbo.PK_employee', 'PK_employees', 'OBJECT';
END
GO

IF OBJECT_ID(N'dbo.PK_menu', N'PK') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.CK_menu_leaf', 'CK_menus_leaf', 'OBJECT';
    EXEC sp_rename 'dbo.DF_menu_is_deleted', 'DF_menus_is_deleted', 'OBJECT';
    EXEC sp_rename 'dbo.PK_menu', 'PK_menus', 'OBJECT';
END
GO

IF OBJECT_ID(N'dbo.PK_monthly_closing', N'PK') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.CK_monthly_closing_status', 'CK_monthly_closings_status', 'OBJECT';
    EXEC sp_rename 'dbo.CK_monthly_closing_tax_unit', 'CK_monthly_closings_tax_unit', 'OBJECT';
    EXEC sp_rename 'dbo.DF_monthly_closing_is_deleted', 'DF_monthly_closings_is_deleted', 'OBJECT';
    EXEC sp_rename 'dbo.PK_monthly_closing', 'PK_monthly_closings', 'OBJECT';
END
GO

IF OBJECT_ID(N'dbo.PK_order_slip', N'PK') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.CK_order_slip_order_status', 'CK_orders_order_status', 'OBJECT';
    EXEC sp_rename 'dbo.CK_order_slip_tax_category', 'CK_orders_tax_category', 'OBJECT';
    EXEC sp_rename 'dbo.DF_order_slip_is_deleted', 'DF_orders_is_deleted', 'OBJECT';
    EXEC sp_rename 'dbo.PK_order_slip', 'PK_orders', 'OBJECT';
END
GO

IF OBJECT_ID(N'dbo.PK_product', N'PK') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.CK_product_tax_category', 'CK_products_tax_category', 'OBJECT';
    EXEC sp_rename 'dbo.DF_product_is_deleted', 'DF_products_is_deleted', 'OBJECT';
    EXEC sp_rename 'dbo.PK_product', 'PK_products', 'OBJECT';
END
GO

IF OBJECT_ID(N'dbo.PK_receipt', N'PK') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.CK_receipt_allocation_status', 'CK_receipts_allocation_status', 'OBJECT';
    EXEC sp_rename 'dbo.CK_receipt_bank_account_bill_due_date_exclusive', 'CK_receipts_bank_account_bill_due_date_exclusive', 'OBJECT';
    EXEC sp_rename 'dbo.CK_receipt_tax_unit', 'CK_receipts_tax_unit', 'OBJECT';
    EXEC sp_rename 'dbo.DF_receipt_is_deleted', 'DF_receipts_is_deleted', 'OBJECT';
    EXEC sp_rename 'dbo.PK_receipt', 'PK_receipts', 'OBJECT';
END
GO

IF OBJECT_ID(N'dbo.PK_receipt_allocation', N'PK') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.CK_receipt_allocation_tax_unit', 'CK_receipt_allocations_tax_unit', 'OBJECT';
    EXEC sp_rename 'dbo.DF_receipt_allocation_is_deleted', 'DF_receipt_allocations_is_deleted', 'OBJECT';
    EXEC sp_rename 'dbo.PK_receipt_allocation', 'PK_receipt_allocations', 'OBJECT';
END
GO

IF OBJECT_ID(N'dbo.PK_slip_number_sequence', N'PK') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.PK_slip_number_sequence', 'PK_slip_number_sequences', 'OBJECT';
END
GO

IF OBJECT_ID(N'dbo.PK_tax_rate_master', N'PK') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.DF_tax_rate_master_is_deleted', 'DF_tax_rates_is_deleted', 'OBJECT';
    EXEC sp_rename 'dbo.PK_tax_rate_master', 'PK_tax_rates', 'OBJECT';
END
GO

-- -----------------------------------------------------------------------------
-- 3. 外部キーのリネーム（23件。末尾の参照先テーブル名も複数形化。
--    FK_menu_parent_menu は末尾 parent_menu が役割名のため不変）
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.FK_billing_customer', N'F') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.FK_billing_customer', 'FK_billings_customers', 'OBJECT';
END
GO

IF OBJECT_ID(N'dbo.FK_customer_employee', N'F') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.FK_customer_employee', 'FK_customers_employees', 'OBJECT';
END
GO

IF OBJECT_ID(N'dbo.FK_detail_invoice_customer', N'F') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.FK_detail_invoice_customer', 'FK_detail_invoices_customers', 'OBJECT';
END
GO

IF OBJECT_ID(N'dbo.FK_detail_invoice_sales_line_detail_invoice', N'F') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.FK_detail_invoice_sales_line_detail_invoice', 'FK_detail_invoice_sales_lines_detail_invoices', 'OBJECT';
END
GO

IF OBJECT_ID(N'dbo.FK_detail_invoice_sales_line_sales', N'F') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.FK_detail_invoice_sales_line_sales', 'FK_detail_invoice_sales_lines_sales', 'OBJECT';
END
GO

IF OBJECT_ID(N'dbo.FK_detail_receipt_bank_account', N'F') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.FK_detail_receipt_bank_account', 'FK_detail_receipts_bank_accounts', 'OBJECT';
END
GO

IF OBJECT_ID(N'dbo.FK_detail_receipt_customer', N'F') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.FK_detail_receipt_customer', 'FK_detail_receipts_customers', 'OBJECT';
END
GO

IF OBJECT_ID(N'dbo.FK_detail_receipt_deposit_method', N'F') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.FK_detail_receipt_deposit_method', 'FK_detail_receipts_deposit_methods', 'OBJECT';
END
GO

IF OBJECT_ID(N'dbo.FK_detail_receipt_detail_invoice', N'F') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.FK_detail_receipt_detail_invoice', 'FK_detail_receipts_detail_invoices', 'OBJECT';
END
GO

IF OBJECT_ID(N'dbo.FK_detail_receipt_sales', N'F') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.FK_detail_receipt_sales', 'FK_detail_receipts_sales', 'OBJECT';
END
GO

IF OBJECT_ID(N'dbo.FK_menu_parent_menu', N'F') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.FK_menu_parent_menu', 'FK_menus_parent_menu', 'OBJECT';
END
GO

IF OBJECT_ID(N'dbo.FK_monthly_closing_customer', N'F') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.FK_monthly_closing_customer', 'FK_monthly_closings_customers', 'OBJECT';
END
GO

IF OBJECT_ID(N'dbo.FK_order_slip_customer', N'F') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.FK_order_slip_customer', 'FK_orders_customers', 'OBJECT';
END
GO

IF OBJECT_ID(N'dbo.FK_order_slip_product', N'F') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.FK_order_slip_product', 'FK_orders_products', 'OBJECT';
END
GO

IF OBJECT_ID(N'dbo.FK_receipt_allocation_billing', N'F') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.FK_receipt_allocation_billing', 'FK_receipt_allocations_billings', 'OBJECT';
END
GO

IF OBJECT_ID(N'dbo.FK_receipt_allocation_customer', N'F') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.FK_receipt_allocation_customer', 'FK_receipt_allocations_customers', 'OBJECT';
END
GO

IF OBJECT_ID(N'dbo.FK_receipt_bank_account', N'F') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.FK_receipt_bank_account', 'FK_receipts_bank_accounts', 'OBJECT';
END
GO

IF OBJECT_ID(N'dbo.FK_receipt_customer', N'F') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.FK_receipt_customer', 'FK_receipts_customers', 'OBJECT';
END
GO

IF OBJECT_ID(N'dbo.FK_receipt_deposit_method', N'F') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.FK_receipt_deposit_method', 'FK_receipts_deposit_methods', 'OBJECT';
END
GO

IF OBJECT_ID(N'dbo.FK_sales_billing', N'F') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.FK_sales_billing', 'FK_sales_billings', 'OBJECT';
END
GO

IF OBJECT_ID(N'dbo.FK_sales_customer', N'F') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.FK_sales_customer', 'FK_sales_customers', 'OBJECT';
END
GO

IF OBJECT_ID(N'dbo.FK_sales_order_slip', N'F') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.FK_sales_order_slip', 'FK_sales_orders', 'OBJECT';
END
GO

IF OBJECT_ID(N'dbo.FK_sales_product', N'F') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.FK_sales_product', 'FK_sales_products', 'OBJECT';
END
GO

-- -----------------------------------------------------------------------------
-- 4. インデックスのリネーム（9件。新テーブル名を指定して sp_rename する）
-- -----------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.billings') AND name = N'IX_billing_closing_year_month')
BEGIN
    EXEC sp_rename 'dbo.billings.IX_billing_closing_year_month', 'IX_billings_closing_year_month', 'INDEX';
END
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.billings') AND name = N'IX_billing_customer_code_closing_year_month')
BEGIN
    EXEC sp_rename 'dbo.billings.IX_billing_customer_code_closing_year_month', 'IX_billings_customer_code_closing_year_month', 'INDEX';
END
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.billings') AND name = N'UQ_billing_customer_closing_ym_confirmed')
BEGIN
    EXEC sp_rename 'dbo.billings.UQ_billing_customer_closing_ym_confirmed', 'UQ_billings_customer_closing_ym_confirmed', 'INDEX';
END
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.detail_receipts') AND name = N'IX_detail_receipt_customer_code_receipt_date')
BEGIN
    EXEC sp_rename 'dbo.detail_receipts.IX_detail_receipt_customer_code_receipt_date', 'IX_detail_receipts_customer_code_receipt_date', 'INDEX';
END
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.detail_receipts') AND name = N'IX_detail_receipt_target_detail_invoice')
BEGIN
    EXEC sp_rename 'dbo.detail_receipts.IX_detail_receipt_target_detail_invoice', 'IX_detail_receipts_target_detail_invoice', 'INDEX';
END
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.detail_receipts') AND name = N'IX_detail_receipt_target_sales')
BEGIN
    EXEC sp_rename 'dbo.detail_receipts.IX_detail_receipt_target_sales', 'IX_detail_receipts_target_sales', 'INDEX';
END
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.receipt_allocations') AND name = N'IX_receipt_allocation_billing_number')
BEGIN
    EXEC sp_rename 'dbo.receipt_allocations.IX_receipt_allocation_billing_number', 'IX_receipt_allocations_billing_number', 'INDEX';
END
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.receipt_allocations') AND name = N'IX_receipt_allocation_customer_code')
BEGIN
    EXEC sp_rename 'dbo.receipt_allocations.IX_receipt_allocation_customer_code', 'IX_receipt_allocations_customer_code', 'INDEX';
END
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.receipts') AND name = N'IX_receipt_customer_code_receipt_date')
BEGIN
    EXEC sp_rename 'dbo.receipts.IX_receipt_customer_code_receipt_date', 'IX_receipts_customer_code_receipt_date', 'INDEX';
END
GO
