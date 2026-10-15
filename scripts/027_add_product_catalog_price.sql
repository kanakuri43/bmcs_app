-- =============================================================================
-- 027_add_product_catalog_price.sql
-- products に定価（catalog_price、税抜）を追加する。参照用の情報で、単価の自動決定には使わない。
--
-- 対象: bmcs_db（172.16.3.171）
-- 参照: docs/database-schema.md 2.2節
-- 適用: sqlcmd -S <サーバ> -U <ユーザー> -d bmcs_db -C -I -i scripts\027_add_product_catalog_price.sql
-- =============================================================================

USE bmcs_db;
GO

SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH(N'dbo.products', N'catalog_price') IS NULL
BEGIN
    ALTER TABLE dbo.products ADD catalog_price decimal(15, 4) NOT NULL
        CONSTRAINT DF_products_catalog_price DEFAULT 0;
END
GO
