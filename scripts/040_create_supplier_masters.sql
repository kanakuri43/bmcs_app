-- =============================================================================
-- 040_create_supplier_masters.sql
-- 同じ商品でも仕入先によって原価が異なるため、仕入先マスタと仕入先別の原価マスタを作成する。
--   suppliers               : 仕入先マスタ（基本項目のみ）
--   product_supplier_costs  : 商品×仕入先の原価（伝票明細の原価の転記元）
--   products.main_supplier_code : 主仕入先（伝票明細の仕入先の初期値）
--
-- 対象: bmcs_db（172.16.3.171）
-- 参照: docs/database-schema.md 2.2節・2.6-5節・2.6-6節
-- 適用: sqlcmd -S <サーバ> -U <ユーザー> -d bmcs_db -C -I -i scripts\040_create_supplier_masters.sql
-- =============================================================================

USE bmcs_db;
GO

SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.suppliers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.suppliers
    (
        supplier_code       varchar(10)     NOT NULL,
        supplier_name       nvarchar(60)    NOT NULL,
        supplier_name_kana  nvarchar(60)    NULL,
        postal_code         varchar(8)      NULL,
        address1            nvarchar(100)   NULL,
        address2            nvarchar(100)   NULL,
        phone_number        varchar(20)     NULL,
        fax_number          varchar(20)     NULL,
        contact_person_name nvarchar(40)    NULL,
        remarks             nvarchar(100)   NULL,

        is_deleted          bit             NOT NULL CONSTRAINT DF_suppliers_is_deleted DEFAULT (0),
        created_by          varchar(10)     NOT NULL,
        created_at          datetime2(3)    NOT NULL,
        updated_by          varchar(10)     NOT NULL,
        updated_at          datetime2(3)    NOT NULL,
        row_version         rowversion      NOT NULL,

        CONSTRAINT PK_suppliers PRIMARY KEY (supplier_code)
    );
END
GO

IF OBJECT_ID(N'dbo.product_supplier_costs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.product_supplier_costs
    (
        product_code    varchar(20)     NOT NULL,
        supplier_code   varchar(10)     NOT NULL,
        cost_price      decimal(15, 4)  NOT NULL,

        is_deleted      bit             NOT NULL CONSTRAINT DF_product_supplier_costs_is_deleted DEFAULT (0),
        created_by      varchar(10)     NOT NULL,
        created_at      datetime2(3)    NOT NULL,
        updated_by      varchar(10)     NOT NULL,
        updated_at      datetime2(3)    NOT NULL,
        row_version     rowversion      NOT NULL,

        CONSTRAINT PK_product_supplier_costs PRIMARY KEY (product_code, supplier_code),
        CONSTRAINT FK_product_supplier_costs_products
            FOREIGN KEY (product_code) REFERENCES dbo.products (product_code),
        CONSTRAINT FK_product_supplier_costs_suppliers
            FOREIGN KEY (supplier_code) REFERENCES dbo.suppliers (supplier_code),
        CONSTRAINT CK_product_supplier_costs_cost CHECK (cost_price >= 0)
    );
END
GO

IF COL_LENGTH(N'dbo.products', N'main_supplier_code') IS NULL
BEGIN
    ALTER TABLE dbo.products ADD main_supplier_code varchar(10) NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_products_suppliers')
BEGIN
    ALTER TABLE dbo.products WITH CHECK
        ADD CONSTRAINT FK_products_suppliers
        FOREIGN KEY (main_supplier_code) REFERENCES dbo.suppliers (supplier_code);
END
GO
