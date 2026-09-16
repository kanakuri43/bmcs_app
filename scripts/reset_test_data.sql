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
--   employee, customer, product, bank_account,
--   order_slip, sales, receipt, receipt_allocation, detail_receipt,
--   billing, detail_invoice, detail_invoice_sales_line, monthly_closing
--
-- 対象外（意図的に残す）:
--   company_info（自社情報。指示により維持）
--   menu（メニュー構成マスタ。開発・本番共通の実データであり、テストデータではない。
--         014_seed_menu_structure.sql が正の投入元）
--   tax_rate_master（税率マスタ。実運用の参照データであり、テストデータではない）
--
-- 採番（slip_number_sequence）は行を削除せず current_value のみ 0 にリセットする
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
DELETE FROM dbo.detail_invoice_sales_line;
DELETE FROM dbo.detail_receipt;
DELETE FROM dbo.detail_invoice;
DELETE FROM dbo.receipt_allocation;
DELETE FROM dbo.receipt;
DELETE FROM dbo.sales;
DELETE FROM dbo.billing;
DELETE FROM dbo.order_slip;
DELETE FROM dbo.monthly_closing;
GO

-- -----------------------------------------------------------------------------
-- 2. マスタ（company_info・menu・tax_rate_master は対象外）
-- -----------------------------------------------------------------------------
DELETE FROM dbo.customer;
DELETE FROM dbo.product;
DELETE FROM dbo.bank_account;
DELETE FROM dbo.employee;
GO

-- -----------------------------------------------------------------------------
-- 3. 採番リセット（行は残し current_value のみ0に戻す）
-- -----------------------------------------------------------------------------
UPDATE dbo.slip_number_sequence SET current_value = 0;
GO
