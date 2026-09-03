-- =============================================================================
-- 003_create_closing_and_sequence_tables.sql
-- 月次締め・採番テーブルの作成: monthly_closing, slip_number_sequence
--
-- 対象: bmcs_db（172.16.3.171）
-- 参照: docs/database-schema.md 2.15〜2.16
--
-- 注意: このファイルは適用後に改変しない。スキーマ変更は新しい連番ファイルで行う。
-- =============================================================================

USE bmcs_db;
GO

-- -----------------------------------------------------------------------------
-- monthly_closing（月次締め）
-- 全社単位で月次に1レコード。「未締め」はレコード不在で表す。
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.monthly_closing', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.monthly_closing
    (
        closing_year_month      char(6)         NOT NULL,
        closing_status             tinyint         NOT NULL,
        confirmed_at                  datetime2(3)    NOT NULL,
        confirmed_by                     varchar(10)     NOT NULL,
        released_at                         datetime2(3)    NULL,
        released_by                            varchar(10)     NULL,

        created_by                             varchar(10)     NOT NULL,
        created_at                             datetime2(3)    NOT NULL,
        updated_by                             varchar(10)     NOT NULL,
        updated_at                             datetime2(3)    NOT NULL,
        row_version                            rowversion      NOT NULL,

        CONSTRAINT PK_monthly_closing PRIMARY KEY (closing_year_month),

        CONSTRAINT CK_monthly_closing_status CHECK (closing_status IN (1, 2))
    );
END
GO

-- -----------------------------------------------------------------------------
-- slip_number_sequence（採番）
-- 伝票種別ごとに1行を永続保持する。row_version・is_deleted は持たない
-- （採番はUPDATEの行ロックで直列化するため楽観的排他と相性が悪い。
--  1行を永続保持するため論理削除の概念がない）。
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.slip_number_sequence', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.slip_number_sequence
    (
        sequence_key        varchar(30)     NOT NULL,
        current_value          bigint          NOT NULL,

        created_by              varchar(10)     NOT NULL,
        created_at              datetime2(3)    NOT NULL,
        updated_by              varchar(10)     NOT NULL,
        updated_at              datetime2(3)    NOT NULL,

        CONSTRAINT PK_slip_number_sequence PRIMARY KEY (sequence_key)
    );

    -- 伝票種別ごとの初期行を投入する（採番は UPDATE 前提のため、行自体は事前に必要）。
    INSERT INTO dbo.slip_number_sequence
        (sequence_key, current_value, created_by, created_at, updated_by, updated_at)
    VALUES
        (N'order_slip',      0, N'SYSTEM', SYSDATETIME(), N'SYSTEM', SYSDATETIME()),
        (N'sales_slip',      0, N'SYSTEM', SYSDATETIME(), N'SYSTEM', SYSDATETIME()),
        (N'payment_slip',    0, N'SYSTEM', SYSDATETIME(), N'SYSTEM', SYSDATETIME()),
        (N'detail_payment',  0, N'SYSTEM', SYSDATETIME(), N'SYSTEM', SYSDATETIME()),
        (N'billing',         0, N'SYSTEM', SYSDATETIME(), N'SYSTEM', SYSDATETIME()),
        (N'detail_invoice',  0, N'SYSTEM', SYSDATETIME(), N'SYSTEM', SYSDATETIME());
END
GO
