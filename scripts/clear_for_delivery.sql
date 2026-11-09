-- =============================================================================
-- clear_for_delivery.sql
-- 納品時に、検証で入れた伝票・締め・取引系マスタを全件クリアするスクリプト。
--
-- 【重要】本番を空にする破壊的スクリプト。納品時の1回だけ流す。
--         実行前に接続先サーバ・データベース名を必ず確認し、バックアップを取る。
--
-- クリアする:
--   ジャーナル: orders, sales, receipts, receipt_allocations, detail_receipts,
--               detail_invoices, detail_invoice_sales_lines, copier_import_histories
--   締め:       billings, monthly_closings
--   採番:       slip_number_sequences（行は残し current_value を0に戻す）
--   マスタ:     customers, products, copier_machines,
--               customer_product_prices, customer_product_rates,
--               product_supplier_costs（products へのFKがあるため一緒に消す）
--
-- クリアしない:
--   employees（社員）, company_infos（自社情報）, bank_accounts（銀行）, menus（メニュー）,
--   suppliers（仕入先）, tax_rates（税率）, deposit_methods（入金方法）
--   プリンタ設定は端末ローカルの bmcs_config.json に保存しておりDBには無い。
--
-- 全体を1トランザクションで実行し、途中で失敗したら全て戻す。
--
-- 適用: sqlcmd -S <サーバ> -U <ユーザー> -d bmcs_db -C -I -i scripts\clear_for_delivery.sql
-- =============================================================================

USE bmcs_db;
GO

SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

-- 1. ジャーナル・締め（FKの逆順で削除）
DELETE FROM dbo.detail_invoice_sales_lines;
DELETE FROM dbo.detail_receipts;
DELETE FROM dbo.detail_invoices;
DELETE FROM dbo.receipt_allocations;
DELETE FROM dbo.receipts;
DELETE FROM dbo.sales;
DELETE FROM dbo.billings;
DELETE FROM dbo.orders;
DELETE FROM dbo.monthly_closings;
DELETE FROM dbo.copier_import_histories;

-- 2. マスタ
DELETE FROM dbo.copier_machines;
DELETE FROM dbo.customer_product_rates;
DELETE FROM dbo.customer_product_prices;
DELETE FROM dbo.product_supplier_costs;
DELETE FROM dbo.customers;
DELETE FROM dbo.products;

-- 3. 採番リセット
UPDATE dbo.slip_number_sequences SET current_value = 0;

COMMIT TRANSACTION;
GO
