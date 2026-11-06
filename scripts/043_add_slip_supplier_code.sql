-- =============================================================================
-- 043_add_slip_supplier_code.sql
-- 受注・売上の明細（orders / sales）に、仕入先コードのスナップショットを追加する。
-- 明細の原価は、この仕入先の原価（product_supplier_costs）があればそれ、なければ商品マスタの標準原価を転記する。
-- 既存行は NULL（仕入先なし）のまま。売上・粗利分析では「（仕入先なし）」に集計される。
--
-- 対象: bmcs_db（172.16.3.171）
-- 参照: docs/database-schema.md 2.9節・2.15節
-- 適用: sqlcmd -S <サーバ> -U <ユーザー> -d bmcs_db -C -I -i scripts\043_add_slip_supplier_code.sql
-- =============================================================================

USE bmcs_db;
GO

SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH(N'dbo.orders', N'supplier_code') IS NULL
BEGIN
    ALTER TABLE dbo.orders ADD supplier_code varchar(10) NULL;
END
GO

IF COL_LENGTH(N'dbo.sales', N'supplier_code') IS NULL
BEGIN
    ALTER TABLE dbo.sales ADD supplier_code varchar(10) NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_orders_suppliers')
BEGIN
    ALTER TABLE dbo.orders WITH CHECK
        ADD CONSTRAINT FK_orders_suppliers
        FOREIGN KEY (supplier_code) REFERENCES dbo.suppliers (supplier_code);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_sales_suppliers')
BEGIN
    ALTER TABLE dbo.sales WITH CHECK
        ADD CONSTRAINT FK_sales_suppliers
        FOREIGN KEY (supplier_code) REFERENCES dbo.suppliers (supplier_code);
END
GO

-- 売上・粗利分析の仕入先別集計（暦月の売上を仕入先で集計する）用。
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_sales_supplier_code' AND object_id = OBJECT_ID(N'dbo.sales'))
BEGIN
    CREATE INDEX IX_sales_supplier_code ON dbo.sales (supplier_code);
END
GO
