-- =============================================================================
-- 032_merge_customer_product_price_menus.sql
-- 「得意先／商品／単価マスタ」と「得意先／商品／定価掛率マスタ」を1画面に統合したため、
-- メニューを「得意先／商品／単価・掛率マスタ」の1つにまとめる（再実行安全）。
-- 014 の再適用はメニューマスタ管理画面での編集内容を失わせるため、変更は本スクリプトで行う。
--
-- 適用: sqlcmd -S <サーバ> -U <ユーザー> -d bmcs_db -C -I -i scripts\032_merge_customer_product_price_menus.sql
-- =============================================================================

USE bmcs_db;
GO

UPDATE dbo.menus
SET menu_name = N'得意先／商品／単価・掛率マスタ', updated_by = N'SEED', updated_at = SYSDATETIME()
WHERE menu_code = N'MNU_CUST_PROD_PRICE'
  AND menu_name <> N'得意先／商品／単価・掛率マスタ';
GO

DELETE FROM dbo.menus WHERE menu_code = N'MNU_CUST_PROD_RATE';
GO
