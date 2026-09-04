-- =============================================================================
-- 001_create_master_tables.sql
-- マスタ系テーブルの作成: employee, customer, product, company_info,
-- bank_account, menu
--
-- 対象: bmcs_db（172.16.3.171）
-- 参照: docs/database-schema.md 2.1〜2.6
--
-- 注意: このファイルは適用後に改変しない。スキーマ変更は新しい連番ファイルで行う。
-- 同一サーバ上に他システムのDBが同居しているため、USE で対象DBを固定し、
-- 誤って再実行してもエラーにならないよう IF NOT EXISTS で保護する。
-- =============================================================================

USE bmcs_db;
GO

-- -----------------------------------------------------------------------------
-- employee（社員マスタ）
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.employee', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.employee
    (
        employee_code       varchar(10)     NOT NULL,
        employee_name       nvarchar(40)    NOT NULL,
        employee_name_kana  nvarchar(40)    NULL,
        permission_level    tinyint         NOT NULL,

        is_deleted          bit             NOT NULL CONSTRAINT DF_employee_is_deleted DEFAULT (0),
        created_by          varchar(10)     NOT NULL,
        created_at          datetime2(3)    NOT NULL,
        updated_by          varchar(10)     NOT NULL,
        updated_at          datetime2(3)    NOT NULL,
        row_version         rowversion      NOT NULL,

        CONSTRAINT PK_employee PRIMARY KEY (employee_code)
    );
END
GO

-- -----------------------------------------------------------------------------
-- customer（得意先マスタ）
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.customer', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.customer
    (
        customer_code               varchar(10)     NOT NULL,
        customer_name                nvarchar(60)    NOT NULL,
        customer_name_kana           nvarchar(60)    NULL,
        postal_code                  varchar(8)      NULL,
        address1                     nvarchar(100)   NULL,
        address2                     nvarchar(100)   NULL,
        phone_number                 varchar(20)     NULL,
        fax_number                   varchar(20)     NULL,
        contact_person_name          nvarchar(40)    NULL,
        sales_employee_code          varchar(10)     NULL,
        closing_day                  tinyint         NOT NULL,
        tax_unit                     tinyint         NOT NULL,
        rounding_type                tinyint         NOT NULL,
        print_representative_flag    bit             NOT NULL,

        is_deleted                   bit             NOT NULL CONSTRAINT DF_customer_is_deleted DEFAULT (0),
        created_by                   varchar(10)     NOT NULL,
        created_at                   datetime2(3)    NOT NULL,
        updated_by                   varchar(10)     NOT NULL,
        updated_at                   datetime2(3)    NOT NULL,
        row_version                  rowversion      NOT NULL,

        CONSTRAINT PK_customer PRIMARY KEY (customer_code),

        CONSTRAINT FK_customer_employee
            FOREIGN KEY (sales_employee_code) REFERENCES dbo.employee (employee_code),

        -- C-1: 税区分=内税明細単位 ⇔ closing_day=0 の相互制約。
        -- 破綻する組み合わせ（締め得意先×明細単位／都度得意先×請求単位）を DB 側で拒否する。
        -- closing_day は 1〜31 が実日付、99 が「末日締め」の専用値（実日付31と区別する）。
        CONSTRAINT CK_customer_tax_unit_closing_day
            CHECK ((tax_unit = 3 AND closing_day = 0)
                OR (tax_unit IN (1, 2) AND (closing_day BETWEEN 1 AND 31 OR closing_day = 99))),

        CONSTRAINT CK_customer_tax_unit CHECK (tax_unit IN (1, 2, 3)),
        CONSTRAINT CK_customer_rounding_type CHECK (rounding_type IN (1, 2, 3)),
        CONSTRAINT CK_customer_closing_day CHECK (closing_day BETWEEN 0 AND 31 OR closing_day = 99)
    );
END
GO

-- -----------------------------------------------------------------------------
-- product（商品マスタ）
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.product', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.product
    (
        product_code          varchar(20)     NOT NULL,
        product_name           nvarchar(60)    NOT NULL,
        product_name_kana      nvarchar(60)    NULL,
        specification          nvarchar(60)    NULL,
        unit_name               nvarchar(10)    NULL,
        standard_unit_price    decimal(15, 4)  NOT NULL,
        standard_cost_price    decimal(15, 4)  NOT NULL,
        tax_category            tinyint         NOT NULL,

        is_deleted             bit             NOT NULL CONSTRAINT DF_product_is_deleted DEFAULT (0),
        created_by              varchar(10)     NOT NULL,
        created_at               datetime2(3)    NOT NULL,
        updated_by               varchar(10)     NOT NULL,
        updated_at                datetime2(3)    NOT NULL,
        row_version                rowversion      NOT NULL,

        CONSTRAINT PK_product PRIMARY KEY (product_code),

        CONSTRAINT CK_product_tax_category CHECK (tax_category IN (1, 2, 3, 4))
    );
END
GO

-- -----------------------------------------------------------------------------
-- company_info（自社情報マスタ）
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.company_info', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.company_info
    (
        company_info_id                tinyint         NOT NULL,
        company_name                    nvarchar(60)    NOT NULL,
        invoice_registration_number     varchar(14)     NOT NULL,
        postal_code                     varchar(8)      NULL,
        address1                        nvarchar(100)   NULL,
        address2                        nvarchar(100)   NULL,
        phone_number                    varchar(20)     NULL,
        fax_number                      varchar(20)     NULL,
        representative_name             nvarchar(40)    NULL,

        is_deleted                      bit             NOT NULL CONSTRAINT DF_company_info_is_deleted DEFAULT (0),
        created_by                      varchar(10)     NOT NULL,
        created_at                       datetime2(3)    NOT NULL,
        updated_by                       varchar(10)     NOT NULL,
        updated_at                        datetime2(3)    NOT NULL,
        row_version                        rowversion      NOT NULL,

        CONSTRAINT PK_company_info PRIMARY KEY (company_info_id),

        -- 自社情報マスタは1レコード運用。複数行の登録を防ぐ。
        CONSTRAINT CK_company_info_single_row CHECK (company_info_id = 1)
    );
END
GO

-- -----------------------------------------------------------------------------
-- bank_account（銀行口座マスタ）
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.bank_account', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.bank_account
    (
        bank_account_code       varchar(10)     NOT NULL,
        bank_name                 nvarchar(40)    NOT NULL,
        branch_name                nvarchar(40)    NOT NULL,
        account_type                tinyint         NOT NULL,
        account_number               varchar(10)     NOT NULL,
        account_holder_name          nvarchar(60)    NOT NULL,
        is_print_on_invoice           bit             NOT NULL,
        display_order                  smallint        NOT NULL,

        is_deleted                     bit             NOT NULL CONSTRAINT DF_bank_account_is_deleted DEFAULT (0),
        created_by                     varchar(10)     NOT NULL,
        created_at                      datetime2(3)    NOT NULL,
        updated_by                      varchar(10)     NOT NULL,
        updated_at                       datetime2(3)    NOT NULL,
        row_version                       rowversion      NOT NULL,

        CONSTRAINT PK_bank_account PRIMARY KEY (bank_account_code),

        CONSTRAINT CK_bank_account_type CHECK (account_type IN (1, 2))
    );
END
GO

-- -----------------------------------------------------------------------------
-- menu（メニュー構成マスタ）
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.menu', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.menu
    (
        menu_code                    varchar(20)     NOT NULL,
        parent_menu_code               varchar(20)     NULL,
        menu_name                       nvarchar(40)    NOT NULL,
        display_order                    smallint        NOT NULL,
        required_permission_level         tinyint         NULL,
        screen_key                         varchar(40)     NULL,

        is_deleted                         bit             NOT NULL CONSTRAINT DF_menu_is_deleted DEFAULT (0),
        created_by                         varchar(10)     NOT NULL,
        created_at                          datetime2(3)    NOT NULL,
        updated_by                          varchar(10)     NOT NULL,
        updated_at                           datetime2(3)    NOT NULL,
        row_version                           rowversion      NOT NULL,

        CONSTRAINT PK_menu PRIMARY KEY (menu_code),

        CONSTRAINT FK_menu_parent_menu
            FOREIGN KEY (parent_menu_code) REFERENCES dbo.menu (menu_code),

        -- 権限は子（末端の機能メニュー）にのみ持たせ、親（分類の見出し）には持たせない。
        CONSTRAINT CK_menu_leaf
            CHECK ((screen_key IS NULL AND required_permission_level IS NULL)
                OR (screen_key IS NOT NULL AND required_permission_level IS NOT NULL))
    );
END
GO
