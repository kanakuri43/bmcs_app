-- =============================================================================
-- 029_create_customer_product_price_masters.sql
-- 受注・売上入力の単価決定用に、得意先×商品のマスタを2つ作成する。
--   customer_product_prices : 得意先×商品の単価（得意先の税区分に従う外税／内税単価。そのまま転記する）
--   customer_product_rates  : 得意先×商品の定価掛率（整数のパーセント、0〜100。商品マスタの定価に掛けて単価を算出する）
--
-- 対象: bmcs_db（172.16.3.171）
-- 参照: docs/database-schema.md 2.2節・2.6-3節・2.6-4節
-- 適用: sqlcmd -S <サーバ> -U <ユーザー> -d bmcs_db -C -I -i scripts\029_create_customer_product_price_masters.sql
-- =============================================================================

USE bmcs_db;
GO

SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.customer_product_prices', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.customer_product_prices
    (
        customer_code   varchar(10)     NOT NULL,
        product_code    varchar(20)     NOT NULL,
        unit_price      decimal(15, 4)  NOT NULL,

        is_deleted      bit             NOT NULL CONSTRAINT DF_customer_product_prices_is_deleted DEFAULT (0),
        created_by      varchar(10)     NOT NULL,
        created_at      datetime2(3)    NOT NULL,
        updated_by      varchar(10)     NOT NULL,
        updated_at      datetime2(3)    NOT NULL,
        row_version     rowversion      NOT NULL,

        CONSTRAINT PK_customer_product_prices PRIMARY KEY (customer_code, product_code),
        CONSTRAINT FK_customer_product_prices_customers
            FOREIGN KEY (customer_code) REFERENCES dbo.customers (customer_code),
        CONSTRAINT FK_customer_product_prices_products
            FOREIGN KEY (product_code) REFERENCES dbo.products (product_code)
    );
END
GO

IF OBJECT_ID(N'dbo.customer_product_rates', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.customer_product_rates
    (
        customer_code       varchar(10)     NOT NULL,
        product_code        varchar(20)     NOT NULL,
        catalog_price_rate  int             NOT NULL,

        is_deleted          bit             NOT NULL CONSTRAINT DF_customer_product_rates_is_deleted DEFAULT (0),
        created_by          varchar(10)     NOT NULL,
        created_at          datetime2(3)    NOT NULL,
        updated_by          varchar(10)     NOT NULL,
        updated_at          datetime2(3)    NOT NULL,
        row_version         rowversion      NOT NULL,

        CONSTRAINT PK_customer_product_rates PRIMARY KEY (customer_code, product_code),
        CONSTRAINT FK_customer_product_rates_customers
            FOREIGN KEY (customer_code) REFERENCES dbo.customers (customer_code),
        CONSTRAINT FK_customer_product_rates_products
            FOREIGN KEY (product_code) REFERENCES dbo.products (product_code),
        CONSTRAINT CK_customer_product_rates_rate CHECK (catalog_price_rate BETWEEN 0 AND 100)
    );
END
GO
