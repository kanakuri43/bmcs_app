-- =============================================================================
-- 007_add_tax_rate_master_and_rename_tax_columns.sql
-- 税率マスタ（tax_rate_master）の新設、商品マスタの単価を外税単価／内税単価に分割、
-- 請求データ・明細請求書の税率別内訳カラムをハードコードしない名称にリネーム。
--
-- 対象: bmcs_db（172.16.3.171）
-- 参照: docs/database-schema.md 1章, 2.2節, 2.3節, 2.12節, 2.13節
--
-- 背景:
--   1. 税率は将来改定されうるため、Domain 層の定数ではなく施行日付きの
--      税率マスタで管理する（税率マスタは作らない、という旧方針を撤回）。
--   2. 内税/外税は商品側の属性ではなく得意先の税区分（tax_unit）で一意に
--      決まるが、商品マスタ側は外税・内税それぞれの単価を持つ必要がある
--      ため、standard_unit_price を2カラムに分割する。
--   3. taxable_10_amount 等、カラム識別子に具体的な税率（%）をハードコード
--      しない（税率改定で名称が実態と食い違うため）。
--
-- 注意: 001_create_master_tables.sql / 002_create_voucher_tables.sql /
-- 006_remove_non_taxable_category.sql は改変せず、本ファイルで追従する
-- （docs/database-schema.md 3章の運用ルール）。
-- =============================================================================

USE bmcs_db;
GO

-- -----------------------------------------------------------------------------
-- 1. tax_rate_master（税率マスタ）の新設
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.tax_rate_master', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tax_rate_master
    (
        effective_date     date            NOT NULL,
        standard_tax_rate  decimal(5, 2)   NOT NULL,
        reduced_tax_rate   decimal(5, 2)   NOT NULL,

        is_deleted         bit             NOT NULL CONSTRAINT DF_tax_rate_master_is_deleted DEFAULT (0),
        created_by         varchar(10)     NOT NULL,
        created_at         datetime2(3)    NOT NULL,
        updated_by         varchar(10)     NOT NULL,
        updated_at         datetime2(3)    NOT NULL,
        row_version        rowversion      NOT NULL,

        CONSTRAINT PK_tax_rate_master PRIMARY KEY (effective_date)
    );
END
GO

-- 現行の税率（2019-10-01施行、標準10%／軽減8%）を初期データとして投入する。
IF NOT EXISTS (SELECT 1 FROM dbo.tax_rate_master WHERE effective_date = '2019-10-01')
BEGIN
    INSERT INTO dbo.tax_rate_master
        (effective_date, standard_tax_rate, reduced_tax_rate, created_by, created_at, updated_by, updated_at)
    VALUES
        ('2019-10-01', 10.00, 8.00, N'SYSTEM', SYSDATETIME(), N'SYSTEM', SYSDATETIME());
END
GO

-- -----------------------------------------------------------------------------
-- 2. product（商品マスタ）: standard_unit_price を外税単価／内税単価に分割
-- -----------------------------------------------------------------------------
IF COL_LENGTH('dbo.product', 'standard_unit_price_excl_tax') IS NULL
BEGIN
    ALTER TABLE dbo.product ADD standard_unit_price_excl_tax decimal(15, 4) NULL;
END
GO

IF COL_LENGTH('dbo.product', 'standard_unit_price_incl_tax') IS NULL
BEGIN
    ALTER TABLE dbo.product ADD standard_unit_price_incl_tax decimal(15, 4) NULL;
END
GO

-- 既存の standard_unit_price を外税単価としてそのまま引き継ぎ、
-- 内税単価は税率マスタの現行税率から算出する（非課税は同額）。
IF COL_LENGTH('dbo.product', 'standard_unit_price') IS NOT NULL
BEGIN
    DECLARE @standard_rate decimal(5, 2), @reduced_rate decimal(5, 2);

    SELECT TOP (1) @standard_rate = standard_tax_rate, @reduced_rate = reduced_tax_rate
    FROM dbo.tax_rate_master
    WHERE effective_date <= CAST(SYSDATETIME() AS date)
    ORDER BY effective_date DESC;

    UPDATE dbo.product
    SET standard_unit_price_excl_tax = standard_unit_price,
        standard_unit_price_incl_tax = CASE tax_category
            WHEN 1 THEN standard_unit_price * (1 + @standard_rate / 100)
            WHEN 2 THEN standard_unit_price * (1 + @reduced_rate / 100)
            ELSE standard_unit_price
        END
    WHERE standard_unit_price_excl_tax IS NULL;
END
GO

ALTER TABLE dbo.product ALTER COLUMN standard_unit_price_excl_tax decimal(15, 4) NOT NULL;
GO
ALTER TABLE dbo.product ALTER COLUMN standard_unit_price_incl_tax decimal(15, 4) NOT NULL;
GO

IF COL_LENGTH('dbo.product', 'standard_unit_price') IS NOT NULL
BEGIN
    ALTER TABLE dbo.product DROP COLUMN standard_unit_price;
END
GO

-- -----------------------------------------------------------------------------
-- 3. 税率別内訳カラムのリネーム（識別子に税率(%)をハードコードしない）
--    taxable_10_amount / tax_10_amount / reduced_8_amount / tax_8_amount
--    → standard_rate_taxable_amount / standard_rate_tax_amount /
--      reduced_rate_taxable_amount / reduced_rate_tax_amount
-- -----------------------------------------------------------------------------
IF COL_LENGTH('dbo.billing_tax_unit_invoice', 'taxable_10_amount') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.billing_tax_unit_invoice.taxable_10_amount', 'standard_rate_taxable_amount', 'COLUMN';
    EXEC sp_rename 'dbo.billing_tax_unit_invoice.tax_10_amount', 'standard_rate_tax_amount', 'COLUMN';
    EXEC sp_rename 'dbo.billing_tax_unit_invoice.reduced_8_amount', 'reduced_rate_taxable_amount', 'COLUMN';
    EXEC sp_rename 'dbo.billing_tax_unit_invoice.tax_8_amount', 'reduced_rate_tax_amount', 'COLUMN';
END
GO

IF COL_LENGTH('dbo.billing_tax_unit_slip', 'taxable_10_amount') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.billing_tax_unit_slip.taxable_10_amount', 'standard_rate_taxable_amount', 'COLUMN';
    EXEC sp_rename 'dbo.billing_tax_unit_slip.tax_10_amount', 'standard_rate_tax_amount', 'COLUMN';
    EXEC sp_rename 'dbo.billing_tax_unit_slip.reduced_8_amount', 'reduced_rate_taxable_amount', 'COLUMN';
    EXEC sp_rename 'dbo.billing_tax_unit_slip.tax_8_amount', 'reduced_rate_tax_amount', 'COLUMN';
END
GO

IF COL_LENGTH('dbo.detail_invoice', 'taxable_10_amount') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.detail_invoice.taxable_10_amount', 'standard_rate_taxable_amount', 'COLUMN';
    EXEC sp_rename 'dbo.detail_invoice.tax_10_amount', 'standard_rate_tax_amount', 'COLUMN';
    EXEC sp_rename 'dbo.detail_invoice.reduced_8_amount', 'reduced_rate_taxable_amount', 'COLUMN';
    EXEC sp_rename 'dbo.detail_invoice.tax_8_amount', 'reduced_rate_tax_amount', 'COLUMN';
END
GO
