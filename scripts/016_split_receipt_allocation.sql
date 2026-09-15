-- -----------------------------------------------------------------------------
-- 016_split_receipt_allocation.sql
--
-- receipt の明細行の軸を「請求への充当1件」から「支払手段の内訳」へ変更する
-- （docs/design_document.md 17章、2026-09-15改訂。ユーザーへの業務実態確認の結果、
-- 締め得意先の入金入力画面では充当先は利用者にとって重要でなく、支払手段（現金／振込／
-- 手形／相殺）を行ごとに選べる必要があると判明したため）。
--
-- 充当（billing_number／allocated_amount／fee_adjustment_amount）は receipt_allocation
-- テーブルへ分離し、画面には表示しない内部データとする。
--
-- 冪等に作成できるよう IF ガードを付ける（他スクリプトと同じ方針）。
-- -----------------------------------------------------------------------------

-- -----------------------------------------------------------------------------
-- 1. receipt_allocation の新設
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.receipt_allocation', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.receipt_allocation
    (
        receipt_slip_number    varchar(20)     NOT NULL,
        line_number             smallint        NOT NULL,
        customer_code            varchar(10)     NOT NULL,
        tax_unit                  tinyint         NOT NULL,
        -- 充当先の請求データ。NULL = 前受・過入金（充当先未定）。
        billing_number             varchar(20)     NULL,
        allocated_amount             decimal(15, 2)  NOT NULL,
        fee_adjustment_amount          decimal(15, 2)  NOT NULL,

        is_deleted                       bit             NOT NULL CONSTRAINT DF_receipt_allocation_is_deleted DEFAULT (0),
        created_by                       varchar(10)     NOT NULL,
        created_at                       datetime2(3)    NOT NULL,
        updated_by                       varchar(10)     NOT NULL,
        updated_at                       datetime2(3)    NOT NULL,
        row_version                      rowversion      NOT NULL,

        CONSTRAINT PK_receipt_allocation PRIMARY KEY (receipt_slip_number, line_number),

        CONSTRAINT FK_receipt_allocation_customer
            FOREIGN KEY (customer_code, tax_unit) REFERENCES dbo.customer (customer_code, tax_unit),
        -- billing_number が NULL の行（前受・過入金）は MATCH SIMPLE により検査対象外。
        CONSTRAINT FK_receipt_allocation_billing
            FOREIGN KEY (billing_number, tax_unit) REFERENCES dbo.billing (billing_number, tax_unit),

        CONSTRAINT CK_receipt_allocation_tax_unit CHECK (tax_unit IN (1, 2))
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_receipt_allocation_billing_number' AND object_id = OBJECT_ID(N'dbo.receipt_allocation'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_receipt_allocation_billing_number
        ON dbo.receipt_allocation (billing_number);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_receipt_allocation_customer_code' AND object_id = OBJECT_ID(N'dbo.receipt_allocation'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_receipt_allocation_customer_code
        ON dbo.receipt_allocation (customer_code);
END
GO

-- -----------------------------------------------------------------------------
-- 2. receipt へ amount／bill_due_date を追加し、既存データを移送する
-- -----------------------------------------------------------------------------
IF COL_LENGTH('dbo.receipt', 'amount') IS NULL
BEGIN
    ALTER TABLE dbo.receipt ADD amount decimal(15, 2) NULL;
END
GO

IF COL_LENGTH('dbo.receipt', 'bill_due_date') IS NULL
BEGIN
    ALTER TABLE dbo.receipt ADD bill_due_date date NULL;
END
GO

-- 既存行（旧仕様＝明細行が充当1件）は「1行1充当」だったため receipt_amount をそのまま
-- amount へ複写しても合計は変わらない。同時に、その行が表していた充当情報を
-- receipt_allocation へ移送する（line_number は receipt 側と同じ値を使う。旧データの
-- 行番号体系がそのまま「充当順」だったため）。
IF COL_LENGTH('dbo.receipt', 'receipt_amount') IS NOT NULL
BEGIN
    UPDATE dbo.receipt SET amount = receipt_amount WHERE amount IS NULL;

    INSERT INTO dbo.receipt_allocation
        (receipt_slip_number, line_number, customer_code, tax_unit, billing_number,
         allocated_amount, fee_adjustment_amount, created_by, created_at, updated_by, updated_at)
    SELECT
        r.receipt_slip_number, r.line_number, r.customer_code, r.tax_unit, r.billing_number,
        r.allocated_amount, r.fee_adjustment_amount, r.created_by, r.created_at, r.updated_by, r.updated_at
    FROM dbo.receipt AS r
    WHERE NOT EXISTS (
        SELECT 1 FROM dbo.receipt_allocation AS ra
        WHERE ra.receipt_slip_number = r.receipt_slip_number AND ra.line_number = r.line_number
    );
END
GO

ALTER TABLE dbo.receipt ALTER COLUMN amount decimal(15, 2) NOT NULL;
GO

-- -----------------------------------------------------------------------------
-- 3. receipt から充当用の列・制約・索引を削除する
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.FK_receipt_billing', N'F') IS NOT NULL
BEGIN
    ALTER TABLE dbo.receipt DROP CONSTRAINT FK_receipt_billing;
END
GO

IF EXISTS (SELECT 1 FROM sys.indexes
           WHERE name = N'IX_receipt_billing_number' AND object_id = OBJECT_ID(N'dbo.receipt'))
BEGIN
    DROP INDEX IX_receipt_billing_number ON dbo.receipt;
END
GO

IF COL_LENGTH('dbo.receipt', 'billing_number') IS NOT NULL
BEGIN
    ALTER TABLE dbo.receipt DROP COLUMN billing_number;
END
GO

IF COL_LENGTH('dbo.receipt', 'allocated_amount') IS NOT NULL
BEGIN
    ALTER TABLE dbo.receipt DROP COLUMN allocated_amount;
END
GO

IF COL_LENGTH('dbo.receipt', 'fee_adjustment_amount') IS NOT NULL
BEGIN
    ALTER TABLE dbo.receipt DROP COLUMN fee_adjustment_amount;
END
GO

IF COL_LENGTH('dbo.receipt', 'receipt_amount') IS NOT NULL
BEGIN
    ALTER TABLE dbo.receipt DROP COLUMN receipt_amount;
END
GO

-- -----------------------------------------------------------------------------
-- 4. receipt_method と付随列（bank_account_code／bill_due_date）の対応を DB で強制する
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.CK_receipt_method_columns', N'C') IS NULL
BEGIN
    ALTER TABLE dbo.receipt WITH CHECK
        ADD CONSTRAINT CK_receipt_method_columns CHECK (
            (receipt_method = 2 AND bank_account_code IS NOT NULL AND bill_due_date IS NULL)
         OR (receipt_method = 3 AND bill_due_date IS NOT NULL AND bank_account_code IS NULL)
         OR (receipt_method IN (1, 4) AND bank_account_code IS NULL AND bill_due_date IS NULL)
        );
END
GO
