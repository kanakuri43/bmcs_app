-- =============================================================================
-- 044_widen_product_name.sql
-- 商品名・商品名カナを nvarchar(200) に広げる。
-- 受注・売上の明細（orders / sales）の商品名はマスタのスナップショットなので、同じ長さに広げる。
--
-- 対象: bmcs_db（172.16.3.171）
-- 参照: docs/database-schema.md 2.2節・2.9節・2.15節
-- 適用: sqlcmd -S <サーバ> -U <ユーザー> -d bmcs_db -C -I -i scripts\044_widen_product_name.sql
-- =============================================================================

USE bmcs_db;
GO

SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH(N'dbo.products', N'product_name') < 400
    ALTER TABLE dbo.products ALTER COLUMN product_name nvarchar(200) NOT NULL;
GO

IF COL_LENGTH(N'dbo.products', N'product_name_kana') < 400
    ALTER TABLE dbo.products ALTER COLUMN product_name_kana nvarchar(200) NULL;
GO

IF COL_LENGTH(N'dbo.orders', N'product_name') < 400
    ALTER TABLE dbo.orders ALTER COLUMN product_name nvarchar(200) NOT NULL;
GO

IF COL_LENGTH(N'dbo.sales', N'product_name') < 400
    ALTER TABLE dbo.sales ALTER COLUMN product_name nvarchar(200) NOT NULL;
GO
