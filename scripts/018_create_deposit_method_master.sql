-- =============================================================================
-- 018_create_deposit_method_master.sql
-- 入金方法（現金・振込・手形・相殺）をハードコードのenum（receipt_method、tinyint）から
-- マスタ（deposit_method）駆動に置き換える。
--
-- 対象: bmcs_db（172.16.3.171）
-- 参照: docs/database-schema.md 2.10・2.10-1・2.11節、docs/design_document.md D-n（本ファイル適用時に追記）
--
-- 背景: 利用者が入金方法を自由に追加・改称できるようにするため、enum+CHECK制約による実装を
-- マスタテーブルへ置き換える（2026-09-18決定）。
--
-- 「振込なら口座必須」「手形なら期日必須」という旧CK_receipt_method_columns/CK_detail_receipt_method
-- の対応関係は、他テーブル（deposit_method）を参照する必要がありDBのCHECK制約では表現できないため、
-- アプリ層（ReceiptEntryService/DetailReceiptEntryServiceの検証メソッド）で担保する。
-- =============================================================================

USE bmcs_db;
GO

-- -----------------------------------------------------------------------------
-- 1. deposit_method（入金方法マスタ）の新設
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.deposit_method', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.deposit_method
    (
        deposit_method_code     varchar(10)     NOT NULL,
        deposit_method_name       nvarchar(20)    NOT NULL,
        requires_bank_account       bit             NOT NULL,
        requires_bill_due_date        bit             NOT NULL,
        display_order                     smallint        NOT NULL,

        is_deleted                        bit             NOT NULL CONSTRAINT DF_deposit_method_is_deleted DEFAULT (0),
        created_by                        varchar(10)     NOT NULL,
        created_at                        datetime2(3)    NOT NULL,
        updated_by                        varchar(10)     NOT NULL,
        updated_at                        datetime2(3)    NOT NULL,
        row_version                       rowversion      NOT NULL,

        CONSTRAINT PK_deposit_method PRIMARY KEY (deposit_method_code),

        -- 振込先口座と手形期日は同時に必須にはならない（旧enum値2/3が排他だったのと同じ制約）。
        CONSTRAINT CK_deposit_method_requires CHECK (NOT (requires_bank_account = 1 AND requires_bill_due_date = 1))
    );
END
GO

-- 旧 ReceiptMethod enum（1=現金／2=振込／3=手形／4=相殺）と1:1対応する初期データ。
-- 既存の receipt.receipt_method / detail_receipt.receipt_method のバックフィルに使う
-- （007_add_tax_rate_master_and_rename_tax_columns.sql の税率初期データと同じ方針）。
IF NOT EXISTS (SELECT 1 FROM dbo.deposit_method WHERE deposit_method_code = N'CASH')
BEGIN
    INSERT INTO dbo.deposit_method
        (deposit_method_code, deposit_method_name, requires_bank_account, requires_bill_due_date,
         display_order, created_by, created_at, updated_by, updated_at)
    VALUES
        (N'CASH', N'現金', 0, 0, 1, N'SYSTEM', SYSDATETIME(), N'SYSTEM', SYSDATETIME());
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.deposit_method WHERE deposit_method_code = N'TRANSFER')
BEGIN
    INSERT INTO dbo.deposit_method
        (deposit_method_code, deposit_method_name, requires_bank_account, requires_bill_due_date,
         display_order, created_by, created_at, updated_by, updated_at)
    VALUES
        (N'TRANSFER', N'振込', 1, 0, 2, N'SYSTEM', SYSDATETIME(), N'SYSTEM', SYSDATETIME());
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.deposit_method WHERE deposit_method_code = N'NOTE')
BEGIN
    INSERT INTO dbo.deposit_method
        (deposit_method_code, deposit_method_name, requires_bank_account, requires_bill_due_date,
         display_order, created_by, created_at, updated_by, updated_at)
    VALUES
        (N'NOTE', N'手形', 0, 1, 3, N'SYSTEM', SYSDATETIME(), N'SYSTEM', SYSDATETIME());
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.deposit_method WHERE deposit_method_code = N'OFFSET')
BEGIN
    INSERT INTO dbo.deposit_method
        (deposit_method_code, deposit_method_name, requires_bank_account, requires_bill_due_date,
         display_order, created_by, created_at, updated_by, updated_at)
    VALUES
        (N'OFFSET', N'相殺', 0, 0, 4, N'SYSTEM', SYSDATETIME(), N'SYSTEM', SYSDATETIME());
END
GO

-- -----------------------------------------------------------------------------
-- 2. receipt: receipt_method（tinyint）→ deposit_method_code（varchar(10)、FK）
-- -----------------------------------------------------------------------------
IF COL_LENGTH('dbo.receipt', 'deposit_method_code') IS NULL
BEGIN
    ALTER TABLE dbo.receipt ADD deposit_method_code varchar(10) NULL;
END
GO

-- receipt_method 列が既に削除済みの場合、静的にCASE式へ書くとコンパイル時に列名解決エラーになるため
-- （IF/WHEREによる実行時ガードは効かない）、動的SQLで存在確認と同時に組み立てる。
IF COL_LENGTH('dbo.receipt', 'receipt_method') IS NOT NULL
BEGIN
    EXEC(N'
        UPDATE dbo.receipt
        SET deposit_method_code = CASE receipt_method
            WHEN 1 THEN N''CASH''
            WHEN 2 THEN N''TRANSFER''
            WHEN 3 THEN N''NOTE''
            WHEN 4 THEN N''OFFSET''
        END
        WHERE deposit_method_code IS NULL');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.receipt WHERE deposit_method_code IS NULL)
BEGIN
    ALTER TABLE dbo.receipt ALTER COLUMN deposit_method_code varchar(10) NOT NULL;
END
GO

IF OBJECT_ID(N'dbo.CK_receipt_method_columns', N'C') IS NOT NULL
BEGIN
    ALTER TABLE dbo.receipt DROP CONSTRAINT CK_receipt_method_columns;
END
GO

-- 単純な値域チェック（010_unify_tax_unit_tables.sql:336）。CK_receipt_method_columns とは別物。
IF OBJECT_ID(N'dbo.CK_receipt_method', N'C') IS NOT NULL
BEGIN
    ALTER TABLE dbo.receipt DROP CONSTRAINT CK_receipt_method;
END
GO

IF COL_LENGTH('dbo.receipt', 'receipt_method') IS NOT NULL
BEGIN
    ALTER TABLE dbo.receipt DROP COLUMN receipt_method;
END
GO

IF OBJECT_ID(N'dbo.FK_receipt_deposit_method', N'F') IS NULL
BEGIN
    ALTER TABLE dbo.receipt WITH CHECK
        ADD CONSTRAINT FK_receipt_deposit_method FOREIGN KEY (deposit_method_code)
            REFERENCES dbo.deposit_method (deposit_method_code);
END
GO

-- 口座と手形期日の排他関係（旧CK_receipt_method_columnsの一部）はテーブル単独で表現できるため維持する。
IF OBJECT_ID(N'dbo.CK_receipt_bank_account_bill_due_date_exclusive', N'C') IS NULL
BEGIN
    ALTER TABLE dbo.receipt WITH CHECK
        ADD CONSTRAINT CK_receipt_bank_account_bill_due_date_exclusive
            CHECK (bank_account_code IS NULL OR bill_due_date IS NULL);
END
GO

-- -----------------------------------------------------------------------------
-- 3. detail_receipt: receipt_method（tinyint）→ deposit_method_code（varchar(10)、FK）
-- -----------------------------------------------------------------------------
IF COL_LENGTH('dbo.detail_receipt', 'deposit_method_code') IS NULL
BEGIN
    ALTER TABLE dbo.detail_receipt ADD deposit_method_code varchar(10) NULL;
END
GO

IF COL_LENGTH('dbo.detail_receipt', 'receipt_method') IS NOT NULL
BEGIN
    EXEC(N'
        UPDATE dbo.detail_receipt
        SET deposit_method_code = CASE receipt_method
            WHEN 1 THEN N''CASH''
            WHEN 2 THEN N''TRANSFER''
            WHEN 3 THEN N''NOTE''
            WHEN 4 THEN N''OFFSET''
        END
        WHERE deposit_method_code IS NULL');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.detail_receipt WHERE deposit_method_code IS NULL)
BEGIN
    ALTER TABLE dbo.detail_receipt ALTER COLUMN deposit_method_code varchar(10) NOT NULL;
END
GO

IF OBJECT_ID(N'dbo.CK_detail_receipt_method', N'C') IS NOT NULL
BEGIN
    ALTER TABLE dbo.detail_receipt DROP CONSTRAINT CK_detail_receipt_method;
END
GO

IF COL_LENGTH('dbo.detail_receipt', 'receipt_method') IS NOT NULL
BEGIN
    ALTER TABLE dbo.detail_receipt DROP COLUMN receipt_method;
END
GO

IF OBJECT_ID(N'dbo.FK_detail_receipt_deposit_method', N'F') IS NULL
BEGIN
    ALTER TABLE dbo.detail_receipt WITH CHECK
        ADD CONSTRAINT FK_detail_receipt_deposit_method FOREIGN KEY (deposit_method_code)
            REFERENCES dbo.deposit_method (deposit_method_code);
END
GO
