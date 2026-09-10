-- =============================================================================
-- 012_redesign_monthly_closing.sql
-- monthly_closing を「全社単位1レコード/月」から「得意先×月末日で1レコード」の
-- billing 類似レイアウトへ再設計する。
--
-- 背景（2026-09-09決定）: 締め得意先への請求（billing）は得意先ごとの締め日
-- （closing_day）期間で集計するが、会計上の月次売掛金は全得意先を暦月（月初〜月末）で
-- 集計する必要があり、両者の集計期間が一致しない。そのため「請求締め」と「月次締め」を
-- 別々の締め処理として併存させ、monthly_closing は得意先ごとの暦月末残高を保持する。
-- 編集ロックは、customer_code+伝票日付の年月に一致する monthly_closing の確定、
-- または sales.billing_number が確定済み billing を指すか、のいずれかで判定する。
--
-- 対象: bmcs_db（172.16.3.171）
-- 参照: docs/database-schema.md 1章・2.16節
--
-- 注意: 旧 monthly_closing は開発用の暫定データのみを保持しており（seed_dev_data.sql）、
-- 本番データは存在しないため DROP TABLE で作り直す。
-- このファイルは適用後に改変しない。スキーマ変更は新しい連番ファイルで行う。
-- =============================================================================

USE bmcs_db;
GO

IF OBJECT_ID(N'dbo.monthly_closing', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.monthly_closing;
END
GO

CREATE TABLE dbo.monthly_closing
(
    closing_date                    date            NOT NULL,
    customer_code                   varchar(10)     NOT NULL,
    tax_unit                        tinyint         NOT NULL,
    customer_name                   nvarchar(60)    NOT NULL,

    previous_balance                decimal(15,2)   NOT NULL,
    sales_amount                    decimal(15,2)   NOT NULL,
    receipt_amount                  decimal(15,2)   NOT NULL,
    tax_amount                      decimal(15,2)   NOT NULL,
    closing_balance                 decimal(15,2)   NOT NULL,

    standard_rate_taxable_amount    decimal(15,2)   NOT NULL,
    standard_rate_tax_amount        decimal(15,2)   NOT NULL,
    reduced_rate_taxable_amount     decimal(15,2)   NOT NULL,
    reduced_rate_tax_amount         decimal(15,2)   NOT NULL,
    tax_exempt_amount               decimal(15,2)   NOT NULL,

    closing_status                  tinyint         NOT NULL,
    confirmed_at                    datetime2(3)    NOT NULL,
    confirmed_by                    varchar(10)     NOT NULL,
    released_at                     datetime2(3)    NULL,
    released_by                     varchar(10)     NULL,

    is_deleted                      bit             NOT NULL CONSTRAINT DF_monthly_closing_is_deleted DEFAULT (0),
    created_by                      varchar(10)     NOT NULL,
    created_at                      datetime2(3)    NOT NULL,
    updated_by                      varchar(10)     NOT NULL,
    updated_at                      datetime2(3)    NOT NULL,
    row_version                     rowversion      NOT NULL,

    CONSTRAINT PK_monthly_closing PRIMARY KEY (closing_date, customer_code),

    CONSTRAINT CK_monthly_closing_tax_unit CHECK (tax_unit IN (1, 2, 3)),
    CONSTRAINT CK_monthly_closing_status CHECK (closing_status IN (1, 2)),

    -- customer の UQ_customer_code_tax_unit を参照する複合FK。billing/sales/receipt と
    -- 同じパターンで、得意先マスタの税区分との整合をDBで強制する（010_unify_tax_unit_tables.sql）。
    CONSTRAINT FK_monthly_closing_customer FOREIGN KEY (customer_code, tax_unit)
        REFERENCES dbo.customer (customer_code, tax_unit)
);
GO
