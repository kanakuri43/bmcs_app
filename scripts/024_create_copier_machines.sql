-- =============================================================================
-- 024_create_copier_machines.sql
-- コピー機売上CSV取込用のテーブルを作成する。
--   copier_machines          : 機番から得意先を特定する変換マスタ
--   copier_import_histories  : 二重取込防止の履歴（機番＋締日で一意）
--
-- 対象: bmcs_db（172.16.3.171）
-- 参照: docs/database-schema.md 2.6-2節、docs/design_document.md 30章
-- 適用: sqlcmd -S <サーバ> -U <ユーザー> -d bmcs_db -C -I -i scripts\024_create_copier_machines.sql
-- =============================================================================

USE bmcs_db;
GO

SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.copier_machines', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.copier_machines
    (
        machine_no      varchar(20)     NOT NULL,
        customer_code   varchar(10)     NOT NULL,
        machine_model   nvarchar(60)    NULL,
        remarks         nvarchar(100)   NULL,

        is_deleted      bit             NOT NULL CONSTRAINT DF_copier_machines_is_deleted DEFAULT (0),
        created_by      varchar(10)     NOT NULL,
        created_at      datetime2(3)    NOT NULL,
        updated_by      varchar(10)     NOT NULL,
        updated_at      datetime2(3)    NOT NULL,
        row_version     rowversion      NOT NULL,

        CONSTRAINT PK_copier_machines PRIMARY KEY (machine_no),
        CONSTRAINT FK_copier_machines_customers
            FOREIGN KEY (customer_code) REFERENCES dbo.customers (customer_code)
    );
END
GO

IF OBJECT_ID(N'dbo.copier_import_histories', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.copier_import_histories
    (
        machine_no          varchar(20)     NOT NULL,
        closing_date        date            NOT NULL,
        sales_slip_number   varchar(20)     NOT NULL,

        created_by          varchar(10)     NOT NULL,
        created_at          datetime2(3)    NOT NULL,
        updated_by          varchar(10)     NOT NULL,
        updated_at          datetime2(3)    NOT NULL,

        CONSTRAINT PK_copier_import_histories PRIMARY KEY (machine_no, closing_date),
        CONSTRAINT FK_copier_import_histories_copier_machines
            FOREIGN KEY (machine_no) REFERENCES dbo.copier_machines (machine_no)
    );
END
GO
