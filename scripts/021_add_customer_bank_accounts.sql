-- =============================================================================
-- 021_add_customer_bank_accounts.sql
-- 得意先マスタに振込先口座（最大2件、bank_account_code1／bank_account_code2）を
-- 追加し、請求書・明細請求書へ得意先ごとに印字できるようにする。
--
-- 対象: bmcs_db（172.16.3.171）
-- 参照: docs/database-schema.md 1-1節・2.9節、docs/report-spec.md 2-2節
--
-- 背景: 従来は bank_accounts.is_print_on_invoice が真の口座を全得意先共通で
-- 請求書フッターに印字していたが、得意先ごとに使い分けたいという業務要件
-- （2026-09-29確定）により、得意先マスタから bank_accounts を最大2件紐づける
-- 方式に置き換える。全社共通印字の方式は完全に廃止するため、
-- is_print_on_invoice はもう参照されなくなり削除する。
-- =============================================================================

USE bmcs_db;
GO

-- -----------------------------------------------------------------------------
-- 1. customers.bank_account_code1 / bank_account_code2 を追加する
--    0〜2件のため両方 NULL 許容（020_add_billing_customer_code.sql のような
--    backfill は不要）。
-- -----------------------------------------------------------------------------
IF COL_LENGTH('dbo.customers', 'bank_account_code1') IS NULL
BEGIN
    ALTER TABLE dbo.customers ADD bank_account_code1 varchar(10) NULL;
END
GO

IF COL_LENGTH('dbo.customers', 'bank_account_code2') IS NULL
BEGIN
    ALTER TABLE dbo.customers ADD bank_account_code2 varchar(10) NULL;
END
GO

-- -----------------------------------------------------------------------------
-- 2. 同一口座の二重紐づけを禁止する CHECK 制約
-- -----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_customers_bank_account_distinct')
BEGIN
    ALTER TABLE dbo.customers WITH CHECK
        ADD CONSTRAINT CK_customers_bank_account_distinct
            CHECK (bank_account_code1 IS NULL OR bank_account_code2 IS NULL OR bank_account_code1 <> bank_account_code2);
END
GO

-- -----------------------------------------------------------------------------
-- 3. bank_accounts への FK
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.FK_customers_bank_account1', N'F') IS NULL
BEGIN
    ALTER TABLE dbo.customers WITH CHECK
        ADD CONSTRAINT FK_customers_bank_account1
            FOREIGN KEY (bank_account_code1) REFERENCES dbo.bank_accounts (bank_account_code);
END
GO

IF OBJECT_ID(N'dbo.FK_customers_bank_account2', N'F') IS NULL
BEGIN
    ALTER TABLE dbo.customers WITH CHECK
        ADD CONSTRAINT FK_customers_bank_account2
            FOREIGN KEY (bank_account_code2) REFERENCES dbo.bank_accounts (bank_account_code);
END
GO

-- -----------------------------------------------------------------------------
-- 4. is_print_on_invoice を削除する（全社共通印字の廃止。得意先単位の紐づけに
--    完全移行したため、この列を参照するコードはもう存在しない）。
-- -----------------------------------------------------------------------------
IF COL_LENGTH('dbo.bank_accounts', 'is_print_on_invoice') IS NOT NULL
BEGIN
    ALTER TABLE dbo.bank_accounts DROP COLUMN is_print_on_invoice;
END
GO
