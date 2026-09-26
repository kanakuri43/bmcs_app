-- =============================================================================
-- reset_test_data.sql
-- マスタ・ジャーナル・締め集計データを全件クリアするスクリプト。
--
-- 【重要】これは開発用DBのリセット用であり、本番環境には絶対に流さない。
--
-- 対象: bmcs_db（172.16.3.171）
--
-- scripts/ 配下の連番ファイル（DDL）とは異なり、DMLのみのユーティリティスクリプト
-- （seed_dev_data.sql と同様の位置づけ）。何度でも再実行してよい。
--
-- 対象テーブル（マスタ・ジャーナル・締め集計。FKの逆順で削除）:
--   employees, customers, products, bank_accounts,
--   orders, sales, receipts, receipt_allocations, detail_receipts,
--   billings, detail_invoices, detail_invoice_sales_lines, monthly_closings
--
-- 対象外（意図的に残す）:
--   company_infos（自社情報。指示により維持）
--   menus（メニュー構成マスタ。開発・本番共通の実データであり、テストデータではない。
--         014_seed_menu_structure.sql が正の投入元）
--   tax_rates（税率マスタ。実運用の参照データであり、テストデータではない）
--   deposit_methods（入金方法マスタ。tax_ratesと同じく実運用の参照データであり、
--                   テストデータではない。018_create_deposit_method_master.sql が正の投入元）
--
-- 採番（slip_number_sequences）は行を削除せず current_value のみ 0 にリセットする
-- （伝票種別ごとに1行を永続保持する運用のため。2.17節）。
--
-- 適用: sqlcmd -S 172.16.3.171 -U sa -d bmcs_db -C -I -i scripts\reset_test_data.sql
-- =============================================================================

USE bmcs_db;
GO

SET QUOTED_IDENTIFIER ON;
GO

-- -----------------------------------------------------------------------------
-- 1. ジャーナル・締め集計（FKの逆順で削除）
-- -----------------------------------------------------------------------------
DELETE FROM dbo.detail_invoice_sales_lines;
DELETE FROM dbo.detail_receipts;
DELETE FROM dbo.detail_invoices;
DELETE FROM dbo.receipt_allocations;
DELETE FROM dbo.receipts;
DELETE FROM dbo.sales;
DELETE FROM dbo.billings;
DELETE FROM dbo.orders;
DELETE FROM dbo.monthly_closings;
GO

-- -----------------------------------------------------------------------------
-- 2. マスタ（company_infos・menus・tax_rates は対象外）
-- -----------------------------------------------------------------------------
DELETE FROM dbo.customers;
DELETE FROM dbo.products;
DELETE FROM dbo.bank_accounts;
DELETE FROM dbo.employees;
GO

-- -----------------------------------------------------------------------------
-- 3. 採番リセット（行は残し current_value のみ0に戻す）
-- -----------------------------------------------------------------------------
UPDATE dbo.slip_number_sequences SET current_value = 0;
GO
