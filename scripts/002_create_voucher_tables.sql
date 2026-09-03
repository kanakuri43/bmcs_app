-- =============================================================================
-- 002_create_voucher_tables.sql
-- 伝票系テーブルの作成: order_slip, billing_tax_unit_invoice,
-- billing_tax_unit_slip, sales_tax_unit_invoice, sales_tax_unit_slip,
-- sales_tax_unit_line, payment_tax_unit_invoice, payment_tax_unit_slip,
-- detail_invoice, detail_payment, detail_invoice_sales_line
--
-- 対象: bmcs_db（172.16.3.171）
-- 参照: docs/database-schema.md 2.7〜2.13, 2.14
--
-- テーブルの並び順は FK 依存関係を満たす順（billing は sales を参照しないため
-- sales より先に作成できる）。
-- 注意: このファイルは適用後に改変しない。スキーマ変更は新しい連番ファイルで行う。
-- =============================================================================

USE bmcs_db;
GO

-- -----------------------------------------------------------------------------
-- order_slip（受注）
-- customer, product に依存。sales_tax_unit_* から参照されるため先に作成する。
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.order_slip', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.order_slip
    (
        order_slip_number          varchar(20)     NOT NULL,
        line_number                 smallint        NOT NULL,
        order_date                   date            NOT NULL,
        customer_code                 varchar(10)     NOT NULL,
        customer_name                  nvarchar(60)    NOT NULL,
        sub_customer_id                 varchar(20)     NULL,
        product_code                     varchar(20)     NOT NULL,
        product_name                      nvarchar(60)    NOT NULL,
        specification                     nvarchar(60)    NULL,
        unit_name                          nvarchar(10)    NULL,
        order_quantity                      decimal(13, 3)  NOT NULL,
        unit_price                           decimal(15, 4)  NOT NULL,
        amount                                 decimal(15, 2)  NOT NULL,
        cost_price                             decimal(15, 4)  NOT NULL,
        tax_category                            tinyint         NOT NULL,
        tax_rate                                 decimal(5, 2)   NOT NULL,
        allocated_quantity                        decimal(13, 3)  NOT NULL,
        order_status                               tinyint         NOT NULL,
        sales_confirmed_quantity                    decimal(13, 3)  NOT NULL,

        is_deleted                                  bit             NOT NULL CONSTRAINT DF_order_slip_is_deleted DEFAULT (0),
        created_by                                  varchar(10)     NOT NULL,
        created_at                                  datetime2(3)    NOT NULL,
        updated_by                                  varchar(10)     NOT NULL,
        updated_at                                  datetime2(3)    NOT NULL,
        row_version                                 rowversion      NOT NULL,

        CONSTRAINT PK_order_slip PRIMARY KEY (order_slip_number, line_number),

        CONSTRAINT FK_order_slip_customer
            FOREIGN KEY (customer_code) REFERENCES dbo.customer (customer_code),
        CONSTRAINT FK_order_slip_product
            FOREIGN KEY (product_code) REFERENCES dbo.product (product_code),

        CONSTRAINT CK_order_slip_tax_category CHECK (tax_category IN (1, 2, 3, 4)),
        CONSTRAINT CK_order_slip_order_status CHECK (order_status IN (1, 2, 3, 4))
    );
END
GO

-- -----------------------------------------------------------------------------
-- billing_tax_unit_invoice / billing_tax_unit_slip（請求データ）
-- 構造が完全に共通。sales/payment より先に作る（sales/payment から参照される）。
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.billing_tax_unit_invoice', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.billing_tax_unit_invoice
    (
        billing_number             varchar(20)     NOT NULL,
        customer_code                varchar(10)     NOT NULL,
        customer_name                  nvarchar(60)    NOT NULL,
        billing_date                     date            NOT NULL,
        closing_year_month                 char(6)         NOT NULL,
        previous_balance                     decimal(15, 2)  NOT NULL,
        payment_amount                         decimal(15, 2)  NOT NULL,
        sales_amount                             decimal(15, 2)  NOT NULL,
        tax_amount                                 decimal(15, 2)  NOT NULL,
        current_billing_amount                       decimal(15, 2)  NOT NULL,
        taxable_10_amount                              decimal(15, 2)  NOT NULL,
        tax_10_amount                                    decimal(15, 2)  NOT NULL,
        reduced_8_amount                                   decimal(15, 2)  NOT NULL,
        tax_8_amount                                         decimal(15, 2)  NOT NULL,
        tax_exempt_amount                                      decimal(15, 2)  NOT NULL,
        non_taxable_amount                                       decimal(15, 2)  NOT NULL,
        billing_status                                             tinyint         NOT NULL,
        confirmed_at                                                 datetime2(3)    NOT NULL,
        confirmed_by                                                   varchar(10)     NOT NULL,
        released_at                                                      datetime2(3)    NULL,
        released_by                                                        varchar(10)     NULL,

        created_by                                                         varchar(10)     NOT NULL,
        created_at                                                          datetime2(3)    NOT NULL,
        updated_by                                                          varchar(10)     NOT NULL,
        updated_at                                                          datetime2(3)    NOT NULL,
        row_version                                                        rowversion      NOT NULL,

        CONSTRAINT PK_billing_tax_unit_invoice PRIMARY KEY (billing_number),

        CONSTRAINT FK_billing_tax_unit_invoice_customer
            FOREIGN KEY (customer_code) REFERENCES dbo.customer (customer_code),

        CONSTRAINT CK_billing_tax_unit_invoice_status CHECK (billing_status IN (1, 2))
    );
END
GO

IF OBJECT_ID(N'dbo.billing_tax_unit_slip', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.billing_tax_unit_slip
    (
        billing_number              varchar(20)     NOT NULL,
        customer_code                 varchar(10)     NOT NULL,
        customer_name                   nvarchar(60)    NOT NULL,
        billing_date                      date            NOT NULL,
        closing_year_month                  char(6)         NOT NULL,
        previous_balance                      decimal(15, 2)  NOT NULL,
        payment_amount                          decimal(15, 2)  NOT NULL,
        sales_amount                              decimal(15, 2)  NOT NULL,
        tax_amount                                  decimal(15, 2)  NOT NULL,
        current_billing_amount                        decimal(15, 2)  NOT NULL,
        taxable_10_amount                               decimal(15, 2)  NOT NULL,
        tax_10_amount                                     decimal(15, 2)  NOT NULL,
        reduced_8_amount                                    decimal(15, 2)  NOT NULL,
        tax_8_amount                                          decimal(15, 2)  NOT NULL,
        tax_exempt_amount                                       decimal(15, 2)  NOT NULL,
        non_taxable_amount                                        decimal(15, 2)  NOT NULL,
        billing_status                                              tinyint         NOT NULL,
        confirmed_at                                                  datetime2(3)    NOT NULL,
        confirmed_by                                                    varchar(10)     NOT NULL,
        released_at                                                       datetime2(3)    NULL,
        released_by                                                         varchar(10)     NULL,

        created_by                                                          varchar(10)     NOT NULL,
        created_at                                                           datetime2(3)    NOT NULL,
        updated_by                                                           varchar(10)     NOT NULL,
        updated_at                                                           datetime2(3)    NOT NULL,
        row_version                                                         rowversion      NOT NULL,

        CONSTRAINT PK_billing_tax_unit_slip PRIMARY KEY (billing_number),

        CONSTRAINT FK_billing_tax_unit_slip_customer
            FOREIGN KEY (customer_code) REFERENCES dbo.customer (customer_code),

        CONSTRAINT CK_billing_tax_unit_slip_status CHECK (billing_status IN (1, 2))
    );
END
GO

-- -----------------------------------------------------------------------------
-- sales_tax_unit_invoice / sales_tax_unit_slip / sales_tax_unit_line（売上）
-- 3テーブルは共通構造＋テーブル固有の税額カラムを持つ。
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.sales_tax_unit_invoice', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.sales_tax_unit_invoice
    (
        sales_slip_number              varchar(20)     NOT NULL,
        line_number                      smallint        NOT NULL,
        slip_date                          date            NOT NULL,
        customer_code                        varchar(10)     NOT NULL,
        customer_name                          nvarchar(60)    NOT NULL,
        slip_type                                tinyint         NOT NULL,
        product_code                               varchar(20)     NOT NULL,
        product_name                                 nvarchar(60)    NOT NULL,
        specification                                  nvarchar(60)    NULL,
        unit_name                                        nvarchar(10)    NULL,
        quantity                                           decimal(13, 3)  NOT NULL,
        unit_price                                           decimal(15, 4)  NOT NULL,
        amount                                                 decimal(15, 2)  NOT NULL,
        cost_price                                              decimal(15, 4)  NOT NULL,
        tax_category                                              tinyint         NOT NULL,
        tax_rate                                                    decimal(5, 2)   NOT NULL,
        delivery_note_issued_at                                       datetime2(3)    NULL,
        delivery_note_issue_count                                       smallint        NOT NULL,
        billing_status                                                    tinyint         NOT NULL,
        settlement_status                                                   tinyint         NOT NULL,
        settled_amount                                                        decimal(15, 2)  NOT NULL,
        order_slip_number                                                       varchar(20)     NULL,
        order_line_number                                                         smallint        NULL,
        billing_number                                                              varchar(20)     NULL,

        is_deleted                                                                  bit             NOT NULL CONSTRAINT DF_sales_tax_unit_invoice_is_deleted DEFAULT (0),
        created_by                                                                  varchar(10)     NOT NULL,
        created_at                                                                  datetime2(3)    NOT NULL,
        updated_by                                                                  varchar(10)     NOT NULL,
        updated_at                                                                  datetime2(3)    NOT NULL,
        row_version                                                                 rowversion      NOT NULL,

        CONSTRAINT PK_sales_tax_unit_invoice PRIMARY KEY (sales_slip_number, line_number),

        CONSTRAINT FK_sales_tax_unit_invoice_customer
            FOREIGN KEY (customer_code) REFERENCES dbo.customer (customer_code),
        CONSTRAINT FK_sales_tax_unit_invoice_product
            FOREIGN KEY (product_code) REFERENCES dbo.product (product_code),
        CONSTRAINT FK_sales_tax_unit_invoice_order_slip
            FOREIGN KEY (order_slip_number, order_line_number) REFERENCES dbo.order_slip (order_slip_number, line_number),
        CONSTRAINT FK_sales_tax_unit_invoice_billing
            FOREIGN KEY (billing_number) REFERENCES dbo.billing_tax_unit_invoice (billing_number),

        CONSTRAINT CK_sales_tax_unit_invoice_slip_type CHECK (slip_type IN (1, 2, 3)),
        CONSTRAINT CK_sales_tax_unit_invoice_tax_category CHECK (tax_category IN (1, 2, 3, 4)),
        CONSTRAINT CK_sales_tax_unit_invoice_billing_status CHECK (billing_status IN (1, 2)),
        CONSTRAINT CK_sales_tax_unit_invoice_settlement_status CHECK (settlement_status IN (1, 2, 3))
    );
END
GO

IF OBJECT_ID(N'dbo.sales_tax_unit_slip', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.sales_tax_unit_slip
    (
        sales_slip_number              varchar(20)     NOT NULL,
        line_number                      smallint        NOT NULL,
        slip_date                          date            NOT NULL,
        customer_code                        varchar(10)     NOT NULL,
        customer_name                          nvarchar(60)    NOT NULL,
        slip_type                                tinyint         NOT NULL,
        product_code                               varchar(20)     NOT NULL,
        product_name                                 nvarchar(60)    NOT NULL,
        specification                                  nvarchar(60)    NULL,
        unit_name                                        nvarchar(10)    NULL,
        quantity                                           decimal(13, 3)  NOT NULL,
        unit_price                                           decimal(15, 4)  NOT NULL,
        amount                                                 decimal(15, 2)  NOT NULL,
        cost_price                                              decimal(15, 4)  NOT NULL,
        tax_category                                              tinyint         NOT NULL,
        tax_rate                                                    decimal(5, 2)   NOT NULL,
        slip_tax_amount                                               decimal(15, 2)  NOT NULL,
        delivery_note_issued_at                                         datetime2(3)    NULL,
        delivery_note_issue_count                                         smallint        NOT NULL,
        billing_status                                                      tinyint         NOT NULL,
        settlement_status                                                     tinyint         NOT NULL,
        settled_amount                                                          decimal(15, 2)  NOT NULL,
        order_slip_number                                                         varchar(20)     NULL,
        order_line_number                                                           smallint        NULL,
        billing_number                                                                varchar(20)     NULL,

        is_deleted                                                                    bit             NOT NULL CONSTRAINT DF_sales_tax_unit_slip_is_deleted DEFAULT (0),
        created_by                                                                    varchar(10)     NOT NULL,
        created_at                                                                    datetime2(3)    NOT NULL,
        updated_by                                                                    varchar(10)     NOT NULL,
        updated_at                                                                    datetime2(3)    NOT NULL,
        row_version                                                                   rowversion      NOT NULL,

        CONSTRAINT PK_sales_tax_unit_slip PRIMARY KEY (sales_slip_number, line_number),

        CONSTRAINT FK_sales_tax_unit_slip_customer
            FOREIGN KEY (customer_code) REFERENCES dbo.customer (customer_code),
        CONSTRAINT FK_sales_tax_unit_slip_product
            FOREIGN KEY (product_code) REFERENCES dbo.product (product_code),
        CONSTRAINT FK_sales_tax_unit_slip_order_slip
            FOREIGN KEY (order_slip_number, order_line_number) REFERENCES dbo.order_slip (order_slip_number, line_number),
        CONSTRAINT FK_sales_tax_unit_slip_billing
            FOREIGN KEY (billing_number) REFERENCES dbo.billing_tax_unit_slip (billing_number),

        CONSTRAINT CK_sales_tax_unit_slip_slip_type CHECK (slip_type IN (1, 2, 3)),
        CONSTRAINT CK_sales_tax_unit_slip_tax_category CHECK (tax_category IN (1, 2, 3, 4)),
        CONSTRAINT CK_sales_tax_unit_slip_billing_status CHECK (billing_status IN (1, 2)),
        CONSTRAINT CK_sales_tax_unit_slip_settlement_status CHECK (settlement_status IN (1, 2, 3))
    );
END
GO

IF OBJECT_ID(N'dbo.sales_tax_unit_line', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.sales_tax_unit_line
    (
        sales_slip_number              varchar(20)     NOT NULL,
        line_number                      smallint        NOT NULL,
        slip_date                          date            NOT NULL,
        customer_code                        varchar(10)     NOT NULL,
        customer_name                          nvarchar(60)    NOT NULL,
        slip_type                                tinyint         NOT NULL,
        product_code                               varchar(20)     NOT NULL,
        product_name                                 nvarchar(60)    NOT NULL,
        specification                                  nvarchar(60)    NULL,
        unit_name                                        nvarchar(10)    NULL,
        quantity                                           decimal(13, 3)  NOT NULL,
        unit_price                                           decimal(15, 4)  NOT NULL,
        amount                                                 decimal(15, 2)  NOT NULL,
        cost_price                                              decimal(15, 4)  NOT NULL,
        tax_category                                              tinyint         NOT NULL,
        tax_rate                                                    decimal(5, 2)   NOT NULL,
        tax_amount                                                    decimal(15, 2)  NOT NULL,
        delivery_note_issued_at                                         datetime2(3)    NULL,
        delivery_note_issue_count                                         smallint        NOT NULL,
        billing_status                                                      tinyint         NOT NULL,
        settlement_status                                                     tinyint         NOT NULL,
        settled_amount                                                          decimal(15, 2)  NOT NULL,
        order_slip_number                                                         varchar(20)     NULL,
        order_line_number                                                           smallint        NULL,

        is_deleted                                                                  bit             NOT NULL CONSTRAINT DF_sales_tax_unit_line_is_deleted DEFAULT (0),
        created_by                                                                  varchar(10)     NOT NULL,
        created_at                                                                  datetime2(3)    NOT NULL,
        updated_by                                                                  varchar(10)     NOT NULL,
        updated_at                                                                  datetime2(3)    NOT NULL,
        row_version                                                                 rowversion      NOT NULL,

        CONSTRAINT PK_sales_tax_unit_line PRIMARY KEY (sales_slip_number, line_number),

        CONSTRAINT FK_sales_tax_unit_line_customer
            FOREIGN KEY (customer_code) REFERENCES dbo.customer (customer_code),
        CONSTRAINT FK_sales_tax_unit_line_product
            FOREIGN KEY (product_code) REFERENCES dbo.product (product_code),
        CONSTRAINT FK_sales_tax_unit_line_order_slip
            FOREIGN KEY (order_slip_number, order_line_number) REFERENCES dbo.order_slip (order_slip_number, line_number),

        -- 明細請求書との紐付けは連携テーブル（detail_invoice_sales_line）で行うため
        -- billing_number は持たない。

        CONSTRAINT CK_sales_tax_unit_line_slip_type CHECK (slip_type IN (1, 2, 3)),
        CONSTRAINT CK_sales_tax_unit_line_tax_category CHECK (tax_category IN (1, 2, 3, 4)),
        CONSTRAINT CK_sales_tax_unit_line_billing_status CHECK (billing_status IN (1, 2)),
        CONSTRAINT CK_sales_tax_unit_line_settlement_status CHECK (settlement_status IN (1, 2, 3))
    );
END
GO

-- -----------------------------------------------------------------------------
-- payment_tax_unit_invoice / payment_tax_unit_slip（締め入金）
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.payment_tax_unit_invoice', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.payment_tax_unit_invoice
    (
        payment_slip_number              varchar(20)     NOT NULL,
        line_number                        smallint        NOT NULL,
        payment_date                         date            NOT NULL,
        customer_code                          varchar(10)     NOT NULL,
        customer_name                            nvarchar(60)    NOT NULL,
        payment_method                             tinyint         NOT NULL,
        bank_account_code                            varchar(10)     NULL,
        payment_amount                                 decimal(15, 2)  NOT NULL,
        billing_number                                   varchar(20)     NULL,
        allocated_amount                                   decimal(15, 2)  NOT NULL,
        fee_adjustment_amount                                decimal(15, 2)  NOT NULL,
        allocation_status                                      tinyint         NOT NULL,

        is_deleted                                             bit             NOT NULL CONSTRAINT DF_payment_tax_unit_invoice_is_deleted DEFAULT (0),
        created_by                                              varchar(10)     NOT NULL,
        created_at                                              datetime2(3)    NOT NULL,
        updated_by                                              varchar(10)     NOT NULL,
        updated_at                                              datetime2(3)    NOT NULL,
        row_version                                             rowversion      NOT NULL,

        CONSTRAINT PK_payment_tax_unit_invoice PRIMARY KEY (payment_slip_number, line_number),

        CONSTRAINT FK_payment_tax_unit_invoice_customer
            FOREIGN KEY (customer_code) REFERENCES dbo.customer (customer_code),
        CONSTRAINT FK_payment_tax_unit_invoice_bank_account
            FOREIGN KEY (bank_account_code) REFERENCES dbo.bank_account (bank_account_code),
        CONSTRAINT FK_payment_tax_unit_invoice_billing
            FOREIGN KEY (billing_number) REFERENCES dbo.billing_tax_unit_invoice (billing_number),

        CONSTRAINT CK_payment_tax_unit_invoice_method CHECK (payment_method IN (1, 2, 3, 4)),
        CONSTRAINT CK_payment_tax_unit_invoice_allocation_status CHECK (allocation_status IN (1, 2, 3))
    );
END
GO

IF OBJECT_ID(N'dbo.payment_tax_unit_slip', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.payment_tax_unit_slip
    (
        payment_slip_number              varchar(20)     NOT NULL,
        line_number                        smallint        NOT NULL,
        payment_date                         date            NOT NULL,
        customer_code                          varchar(10)     NOT NULL,
        customer_name                            nvarchar(60)    NOT NULL,
        payment_method                             tinyint         NOT NULL,
        bank_account_code                            varchar(10)     NULL,
        payment_amount                                 decimal(15, 2)  NOT NULL,
        billing_number                                   varchar(20)     NULL,
        allocated_amount                                   decimal(15, 2)  NOT NULL,
        fee_adjustment_amount                                decimal(15, 2)  NOT NULL,
        allocation_status                                      tinyint         NOT NULL,

        is_deleted                                             bit             NOT NULL CONSTRAINT DF_payment_tax_unit_slip_is_deleted DEFAULT (0),
        created_by                                              varchar(10)     NOT NULL,
        created_at                                              datetime2(3)    NOT NULL,
        updated_by                                              varchar(10)     NOT NULL,
        updated_at                                              datetime2(3)    NOT NULL,
        row_version                                             rowversion      NOT NULL,

        CONSTRAINT PK_payment_tax_unit_slip PRIMARY KEY (payment_slip_number, line_number),

        CONSTRAINT FK_payment_tax_unit_slip_customer
            FOREIGN KEY (customer_code) REFERENCES dbo.customer (customer_code),
        CONSTRAINT FK_payment_tax_unit_slip_bank_account
            FOREIGN KEY (bank_account_code) REFERENCES dbo.bank_account (bank_account_code),
        CONSTRAINT FK_payment_tax_unit_slip_billing
            FOREIGN KEY (billing_number) REFERENCES dbo.billing_tax_unit_slip (billing_number),

        CONSTRAINT CK_payment_tax_unit_slip_method CHECK (payment_method IN (1, 2, 3, 4)),
        CONSTRAINT CK_payment_tax_unit_slip_allocation_status CHECK (allocation_status IN (1, 2, 3))
    );
END
GO

-- -----------------------------------------------------------------------------
-- detail_invoice（明細請求書）
-- detail_payment / detail_invoice_sales_line から参照されるため先に作成する。
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.detail_invoice', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.detail_invoice
    (
        detail_invoice_number      varchar(20)     NOT NULL,
        customer_code                 varchar(10)     NOT NULL,
        customer_name                   nvarchar(60)    NOT NULL,
        addressee_name                    nvarchar(60)    NOT NULL,
        issue_date                          date            NOT NULL,
        sales_amount                          decimal(15, 2)  NOT NULL,
        tax_amount                              decimal(15, 2)  NOT NULL,
        total_amount                              decimal(15, 2)  NOT NULL,
        taxable_10_amount                           decimal(15, 2)  NOT NULL,
        tax_10_amount                                 decimal(15, 2)  NOT NULL,
        reduced_8_amount                                decimal(15, 2)  NOT NULL,
        tax_8_amount                                      decimal(15, 2)  NOT NULL,
        tax_exempt_amount                                   decimal(15, 2)  NOT NULL,
        non_taxable_amount                                    decimal(15, 2)  NOT NULL,
        invoice_status                                          tinyint         NOT NULL,
        issued_at                                                 datetime2(3)    NOT NULL,
        issued_by                                                   varchar(10)     NOT NULL,
        cancelled_at                                                  datetime2(3)    NULL,
        cancelled_by                                                    varchar(10)     NULL,

        created_by                                                      varchar(10)     NOT NULL,
        created_at                                                      datetime2(3)    NOT NULL,
        updated_by                                                      varchar(10)     NOT NULL,
        updated_at                                                      datetime2(3)    NOT NULL,
        row_version                                                     rowversion      NOT NULL,

        CONSTRAINT PK_detail_invoice PRIMARY KEY (detail_invoice_number),

        CONSTRAINT FK_detail_invoice_customer
            FOREIGN KEY (customer_code) REFERENCES dbo.customer (customer_code),

        CONSTRAINT CK_detail_invoice_status CHECK (invoice_status IN (1, 2))
    );
END
GO

-- -----------------------------------------------------------------------------
-- detail_payment（明細入金）
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.detail_payment', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.detail_payment
    (
        detail_payment_number              varchar(20)     NOT NULL,
        line_number                          smallint        NOT NULL,
        payment_date                           date            NOT NULL,
        customer_code                            varchar(10)     NOT NULL,
        customer_name                              nvarchar(60)    NOT NULL,
        payment_method                               tinyint         NOT NULL,
        bank_account_code                              varchar(10)     NULL,
        payment_amount                                   decimal(15, 2)  NOT NULL,
        target_type                                        tinyint         NOT NULL,
        target_sales_slip_number                             varchar(20)     NULL,
        target_sales_line_number                               smallint        NULL,
        target_detail_invoice_number                             varchar(20)     NULL,
        allocated_amount                                           decimal(15, 2)  NOT NULL,
        fee_adjustment_amount                                        decimal(15, 2)  NOT NULL,
        allocation_status                                              tinyint         NOT NULL,

        is_deleted                                                     bit             NOT NULL CONSTRAINT DF_detail_payment_is_deleted DEFAULT (0),
        created_by                                                     varchar(10)     NOT NULL,
        created_at                                                     datetime2(3)    NOT NULL,
        updated_by                                                     varchar(10)     NOT NULL,
        updated_at                                                     datetime2(3)    NOT NULL,
        row_version                                                    rowversion      NOT NULL,

        CONSTRAINT PK_detail_payment PRIMARY KEY (detail_payment_number, line_number),

        CONSTRAINT FK_detail_payment_customer
            FOREIGN KEY (customer_code) REFERENCES dbo.customer (customer_code),
        CONSTRAINT FK_detail_payment_bank_account
            FOREIGN KEY (bank_account_code) REFERENCES dbo.bank_account (bank_account_code),
        CONSTRAINT FK_detail_payment_sales_line
            FOREIGN KEY (target_sales_slip_number, target_sales_line_number)
            REFERENCES dbo.sales_tax_unit_line (sales_slip_number, line_number),
        CONSTRAINT FK_detail_payment_detail_invoice
            FOREIGN KEY (target_detail_invoice_number) REFERENCES dbo.detail_invoice (detail_invoice_number),

        CONSTRAINT CK_detail_payment_method CHECK (payment_method IN (1, 2, 3, 4)),
        CONSTRAINT CK_detail_payment_allocation_status CHECK (allocation_status IN (1, 2, 3)),
        CONSTRAINT CK_detail_payment_target_type CHECK (target_type IN (1, 2)),

        -- target_type と実際に埋まっているカラムを一致させる。
        CONSTRAINT CK_detail_payment_target
            CHECK (
                (target_type = 1
                    AND target_sales_slip_number IS NOT NULL
                    AND target_sales_line_number IS NOT NULL
                    AND target_detail_invoice_number IS NULL)
                OR
                (target_type = 2
                    AND target_detail_invoice_number IS NOT NULL
                    AND target_sales_slip_number IS NULL
                    AND target_sales_line_number IS NULL)
            )
    );
END
GO

-- -----------------------------------------------------------------------------
-- detail_invoice_sales_line（明細請求書と売上明細行の連携）
-- 二重請求防止の UNIQUE 制約を持つ。row_version は持たない
-- （行の追加・削除のみで更新がないため）。
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.detail_invoice_sales_line', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.detail_invoice_sales_line
    (
        detail_invoice_number    varchar(20)     NOT NULL,
        sales_slip_number          varchar(20)     NOT NULL,
        sales_line_number            smallint        NOT NULL,

        created_by                   varchar(10)     NOT NULL,
        created_at                   datetime2(3)    NOT NULL,
        updated_by                   varchar(10)     NOT NULL,
        updated_at                   datetime2(3)    NOT NULL,

        CONSTRAINT PK_detail_invoice_sales_line
            PRIMARY KEY (detail_invoice_number, sales_slip_number, sales_line_number),

        CONSTRAINT FK_detail_invoice_sales_line_detail_invoice
            FOREIGN KEY (detail_invoice_number) REFERENCES dbo.detail_invoice (detail_invoice_number),
        CONSTRAINT FK_detail_invoice_sales_line_sales_line
            FOREIGN KEY (sales_slip_number, sales_line_number)
            REFERENCES dbo.sales_tax_unit_line (sales_slip_number, line_number),

        -- 二重請求防止: 1つの売上明細行が紐づける明細請求書は最大1つ。
        CONSTRAINT UQ_detail_invoice_sales_line_sales_line
            UNIQUE (sales_slip_number, sales_line_number)
    );
END
GO
