-- =============================================================================
-- 販売管理システム マイグレーション V1
-- SQL Server 2022
-- =============================================================================

-- -----------------------------------------------------------------------------
-- 自社情報
-- -----------------------------------------------------------------------------
CREATE TABLE company_info (
    company_info_id     INT             NOT NULL IDENTITY(1,1),
    company_name        NVARCHAR(100)   NOT NULL,
    address             NVARCHAR(200)   NULL,
    tel                 NVARCHAR(20)    NULL,
    fax                 NVARCHAR(20)    NULL,
    invoice_no          NVARCHAR(20)    NULL,           -- 適格請求書発行事業者番号
    CONSTRAINT PK_company_info PRIMARY KEY (company_info_id)
);

-- -----------------------------------------------------------------------------
-- 区分マスタ群
-- -----------------------------------------------------------------------------
CREATE TABLE payment_method_classifications (
    payment_method_id   INT             NOT NULL IDENTITY(1,1),
    payment_method_code NVARCHAR(20)    NOT NULL,
    payment_method_name NVARCHAR(50)    NOT NULL,
    is_deleted          BIT             NOT NULL CONSTRAINT DF_payment_method_classifications_is_deleted DEFAULT 0,
    row_version         ROWVERSION      NOT NULL,
    CONSTRAINT PK_payment_method_classifications PRIMARY KEY (payment_method_id),
    CONSTRAINT UQ_payment_method_classifications_code UNIQUE (payment_method_code)
);

CREATE TABLE tax_type_classifications (
    tax_type_id         INT             NOT NULL IDENTITY(1,1),
    tax_type_code       NVARCHAR(20)    NOT NULL,
    tax_type_name       NVARCHAR(50)    NOT NULL,
    is_deleted          BIT             NOT NULL CONSTRAINT DF_tax_type_classifications_is_deleted DEFAULT 0,
    row_version         ROWVERSION      NOT NULL,
    CONSTRAINT PK_tax_type_classifications PRIMARY KEY (tax_type_id),
    CONSTRAINT UQ_tax_type_classifications_code UNIQUE (tax_type_code)
);

CREATE TABLE tax_category_classifications (
    tax_category_id     INT             NOT NULL IDENTITY(1,1),
    tax_category_code   NVARCHAR(20)    NOT NULL,
    tax_category_name   NVARCHAR(50)    NOT NULL,
    is_deleted          BIT             NOT NULL CONSTRAINT DF_tax_category_classifications_is_deleted DEFAULT 0,
    row_version         ROWVERSION      NOT NULL,
    CONSTRAINT PK_tax_category_classifications PRIMARY KEY (tax_category_id),
    CONSTRAINT UQ_tax_category_classifications_code UNIQUE (tax_category_code)
);

CREATE TABLE tax_fraction_classifications (
    tax_fraction_id     INT             NOT NULL IDENTITY(1,1),
    tax_fraction_code   NVARCHAR(20)    NOT NULL,
    tax_fraction_name   NVARCHAR(50)    NOT NULL,
    is_deleted          BIT             NOT NULL CONSTRAINT DF_tax_fraction_classifications_is_deleted DEFAULT 0,
    row_version         ROWVERSION      NOT NULL,
    CONSTRAINT PK_tax_fraction_classifications PRIMARY KEY (tax_fraction_id),
    CONSTRAINT UQ_tax_fraction_classifications_code UNIQUE (tax_fraction_code)
);

CREATE TABLE tax_calc_unit_classifications (
    tax_calc_unit_id    INT             NOT NULL IDENTITY(1,1),
    tax_calc_unit_code  NVARCHAR(20)    NOT NULL,
    tax_calc_unit_name  NVARCHAR(50)    NOT NULL,
    is_deleted          BIT             NOT NULL CONSTRAINT DF_tax_calc_unit_classifications_is_deleted DEFAULT 0,
    row_version         ROWVERSION      NOT NULL,
    CONSTRAINT PK_tax_calc_unit_classifications PRIMARY KEY (tax_calc_unit_id),
    CONSTRAINT UQ_tax_calc_unit_classifications_code UNIQUE (tax_calc_unit_code)
);

-- -----------------------------------------------------------------------------
-- 社員マスタ
-- -----------------------------------------------------------------------------
CREATE TABLE employees (
    employee_id         INT             NOT NULL IDENTITY(1,1),
    employee_code       NVARCHAR(20)    NOT NULL,
    employee_name       NVARCHAR(50)    NOT NULL,
    is_deleted          BIT             NOT NULL CONSTRAINT DF_employees_is_deleted DEFAULT 0,
    row_version         ROWVERSION      NOT NULL,
    CONSTRAINT PK_employees PRIMARY KEY (employee_id),
    CONSTRAINT UQ_employees_code UNIQUE (employee_code)
);

-- -----------------------------------------------------------------------------
-- 得意先マスタ
-- -----------------------------------------------------------------------------
CREATE TABLE customers (
    customer_id         INT             NOT NULL IDENTITY(1,1),
    customer_code       NVARCHAR(20)    NOT NULL,
    customer_name       NVARCHAR(100)   NOT NULL,
    closing_day         TINYINT         NOT NULL,       -- 締日（例：10, 20, 31=末日）
    tax_fraction_id     INT             NOT NULL,
    tax_calc_unit_id    INT             NOT NULL,
    employee_id         INT             NULL,			-― 得意先担当者
    is_deleted          BIT             NOT NULL CONSTRAINT DF_customers_is_deleted DEFAULT 0,
    row_version         ROWVERSION      NOT NULL,
    CONSTRAINT PK_customers PRIMARY KEY (customer_id),
    CONSTRAINT UQ_customers_code UNIQUE (customer_code),
    CONSTRAINT FK_customers_tax_fraction FOREIGN KEY (tax_fraction_id)
        REFERENCES tax_fraction_classifications (tax_fraction_id),
    CONSTRAINT FK_customers_tax_calc_unit FOREIGN KEY (tax_calc_unit_id)
        REFERENCES tax_calc_unit_classifications (tax_calc_unit_id),
    CONSTRAINT FK_customers_employees FOREIGN KEY (employee_id)
        REFERENCES employees (employee_id),
    CONSTRAINT CK_customers_closing_day
        CHECK (closing_day BETWEEN 1 AND 31)
);

-- -----------------------------------------------------------------------------
-- 商品マスタ
-- -----------------------------------------------------------------------------
CREATE TABLE products (
    product_id          INT             NOT NULL IDENTITY(1,1),
    product_code        NVARCHAR(20)    NOT NULL,
    product_name        NVARCHAR(100)   NOT NULL,
    tax_type_id         INT             NOT NULL,
    tax_category_id     INT             NOT NULL,
    is_deleted          BIT             NOT NULL CONSTRAINT DF_products_is_deleted DEFAULT 0,
    row_version         ROWVERSION      NOT NULL,
    CONSTRAINT PK_products PRIMARY KEY (product_id),
    CONSTRAINT UQ_products_code UNIQUE (product_code),
    CONSTRAINT FK_products_tax_type FOREIGN KEY (tax_type_id)
        REFERENCES tax_type_classifications (tax_type_id),
    CONSTRAINT FK_products_tax_category FOREIGN KEY (tax_category_id)
        REFERENCES tax_category_classifications (tax_category_id)
);

-- -----------------------------------------------------------------------------
-- 消費税率マスタ
-- -----------------------------------------------------------------------------
CREATE TABLE tax_rate_histories (
    tax_rate_history_id INT             NOT NULL IDENTITY(1,1),
    tax_category_id     INT             NOT NULL,
    rate                DECIMAL(5, 4)   NOT NULL,       -- 例：0.1000, 0.0800
    start_date          DATE            NOT NULL,
    end_date            DATE            NULL,           -- NULLは現在適用中
    is_deleted          BIT             NOT NULL CONSTRAINT DF_tax_rate_histories_is_deleted DEFAULT 0,
    row_version         ROWVERSION      NOT NULL,
    CONSTRAINT PK_tax_rate_histories PRIMARY KEY (tax_rate_history_id),
    CONSTRAINT FK_tax_rate_histories_tax_category FOREIGN KEY (tax_category_id)
        REFERENCES tax_category_classifications (tax_category_id),
    CONSTRAINT CK_tax_rate_histories_rate
        CHECK (rate >= 0)
);

-- -----------------------------------------------------------------------------
-- 受注
-- -----------------------------------------------------------------------------
CREATE TABLE orders (
    order_id            INT             NOT NULL IDENTITY(1,1),
    order_no            NVARCHAR(20)    NOT NULL,       -- 伝票番号
    order_date          DATE            NOT NULL,
    customer_id         INT             NOT NULL,
    customer_code       NVARCHAR(20)    NOT NULL,       -- ジャーナル保持
    customer_name       NVARCHAR(100)   NOT NULL,       -- ジャーナル保持
    line_no             INT             NOT NULL,       -- 行番号
    product_id          INT             NOT NULL,
    product_code        NVARCHAR(20)    NOT NULL,       -- ジャーナル保持
    product_name        NVARCHAR(100)   NOT NULL,       -- ジャーナル保持
    quantity            DECIMAL(10, 2)  NOT NULL,
    unit_price          DECIMAL(10, 2)  NOT NULL,
    tax_type_id         INT             NOT NULL,
    tax_category_id     INT             NOT NULL,
    tax_calc_unit_id    INT             NOT NULL,       -- 得意先から引継ぎ
    tax_fraction_id     INT             NOT NULL,       -- 得意先から引継ぎ
    tax_amount          DECIMAL(10, 2)  NULL,           -- 消費税額（明細単位時のみ使用）
    slip_tax_amount     DECIMAL(10, 2)  NULL,           -- 消費税額（伝票単位時のみ使用／伝票内全行同値）
    slip_remarks        NVARCHAR(200)   NULL,           -- 伝票備考
    line_remarks        NVARCHAR(200)   NULL,           -- 行備考
    is_deleted          BIT             NOT NULL CONSTRAINT DF_orders_is_deleted DEFAULT 0,
    row_version         ROWVERSION      NOT NULL,
    CONSTRAINT PK_orders PRIMARY KEY (order_id),
    CONSTRAINT UQ_orders_line UNIQUE (order_no, line_no),
    CONSTRAINT FK_orders_customers FOREIGN KEY (customer_id)
        REFERENCES customers (customer_id),
    CONSTRAINT FK_orders_products FOREIGN KEY (product_id)
        REFERENCES products (product_id),
    CONSTRAINT FK_orders_tax_type FOREIGN KEY (tax_type_id)
        REFERENCES tax_type_classifications (tax_type_id),
    CONSTRAINT FK_orders_tax_category FOREIGN KEY (tax_category_id)
        REFERENCES tax_category_classifications (tax_category_id),
    CONSTRAINT FK_orders_tax_calc_unit FOREIGN KEY (tax_calc_unit_id)
        REFERENCES tax_calc_unit_classifications (tax_calc_unit_id),
    CONSTRAINT FK_orders_tax_fraction FOREIGN KEY (tax_fraction_id)
        REFERENCES tax_fraction_classifications (tax_fraction_id),
    CONSTRAINT CK_orders_quantity
        CHECK (quantity <> 0)
);

-- -----------------------------------------------------------------------------
-- 売上
-- -----------------------------------------------------------------------------
CREATE TABLE sales (
    sale_id             INT             NOT NULL IDENTITY(1,1),
    sale_no             NVARCHAR(20)    NOT NULL,       -- 伝票番号
    sale_date           DATE            NOT NULL,
    customer_id         INT             NOT NULL,
    customer_code       NVARCHAR(20)    NOT NULL,       -- ジャーナル保持
    customer_name       NVARCHAR(100)   NOT NULL,       -- ジャーナル保持
    order_id            INT             NULL,           -- 参照受注ID（nullable）
    order_no            NVARCHAR(20)    NULL,           -- 参照受注伝票番号
    employee_id         INT             NOT NULL,       -- 実績担当者
    employee_code       NVARCHAR(20)    NOT NULL,       -- ジャーナル保持
    employee_name       NVARCHAR(50)    NOT NULL,       -- ジャーナル保持
    line_no             INT             NOT NULL,       -- 行番号
    product_id          INT             NOT NULL,
    product_code        NVARCHAR(20)    NOT NULL,       -- ジャーナル保持
    product_name        NVARCHAR(100)   NOT NULL,       -- ジャーナル保持
    quantity            DECIMAL(10, 2)  NOT NULL,
    unit_price          DECIMAL(10, 2)  NOT NULL,
    tax_type_id         INT             NOT NULL,
    tax_category_id     INT             NOT NULL,
    tax_calc_unit_id    INT             NOT NULL,       -- 得意先から引継ぎ
    tax_fraction_id     INT             NOT NULL,       -- 得意先から引継ぎ
    tax_amount          DECIMAL(10, 2)  NULL,           -- 消費税額（明細単位時のみ使用）
    slip_tax_amount     DECIMAL(10, 2)  NULL,           -- 消費税額（伝票単位時のみ使用／伝票内全行同値）
    invoiced_date       DATE            NULL,           -- 請求集計日（NULL=未集計）
    slip_remarks        NVARCHAR(200)   NULL,           -- 伝票備考
    line_remarks        NVARCHAR(200)   NULL,           -- 行備考
    is_deleted          BIT             NOT NULL CONSTRAINT DF_sales_is_deleted DEFAULT 0,
    row_version         ROWVERSION      NOT NULL,
    CONSTRAINT PK_sales PRIMARY KEY (sale_id),
    CONSTRAINT UQ_sales_line UNIQUE (sale_no, line_no),
    CONSTRAINT FK_sales_customers FOREIGN KEY (customer_id)
        REFERENCES customers (customer_id),
    CONSTRAINT FK_sales_orders FOREIGN KEY (order_id)
        REFERENCES orders (order_id),
    CONSTRAINT FK_sales_employees FOREIGN KEY (employee_id)
        REFERENCES employees (employee_id),
    CONSTRAINT FK_sales_products FOREIGN KEY (product_id)
        REFERENCES products (product_id),
    CONSTRAINT FK_sales_tax_type FOREIGN KEY (tax_type_id)
        REFERENCES tax_type_classifications (tax_type_id),
    CONSTRAINT FK_sales_tax_category FOREIGN KEY (tax_category_id)
        REFERENCES tax_category_classifications (tax_category_id),
    CONSTRAINT FK_sales_tax_calc_unit FOREIGN KEY (tax_calc_unit_id)
        REFERENCES tax_calc_unit_classifications (tax_calc_unit_id),
    CONSTRAINT FK_sales_tax_fraction FOREIGN KEY (tax_fraction_id)
        REFERENCES tax_fraction_classifications (tax_fraction_id),
    CONSTRAINT CK_sales_quantity
        CHECK (quantity <> 0)
);

-- -----------------------------------------------------------------------------
-- 入金
-- -----------------------------------------------------------------------------
CREATE TABLE receipts (
    receipt_id          INT             NOT NULL IDENTITY(1,1),
    receipt_no          NVARCHAR(20)    NOT NULL,       -- 伝票番号
    receipt_date        DATE            NOT NULL,
    customer_id         INT             NOT NULL,
    customer_code       NVARCHAR(20)    NOT NULL,       -- ジャーナル保持
    customer_name       NVARCHAR(100)   NOT NULL,       -- ジャーナル保持
    line_no             INT             NOT NULL,       -- 行番号
    payment_method_id   INT             NOT NULL,
    amount              DECIMAL(12, 2)  NOT NULL,
    invoiced_date       DATE            NULL,           -- 請求集計日（NULL=未集計）
    slip_remarks        NVARCHAR(200)   NULL,           -- 伝票備考
    line_remarks        NVARCHAR(200)   NULL,           -- 行備考
    is_deleted          BIT             NOT NULL CONSTRAINT DF_receipts_is_deleted DEFAULT 0,
    row_version         ROWVERSION      NOT NULL,
    CONSTRAINT PK_receipts PRIMARY KEY (receipt_id),
    CONSTRAINT UQ_receipts_line UNIQUE (receipt_no, line_no),
    CONSTRAINT FK_receipts_customers FOREIGN KEY (customer_id)
        REFERENCES customers (customer_id),
    CONSTRAINT FK_receipts_payment_method FOREIGN KEY (payment_method_id)
        REFERENCES payment_method_classifications (payment_method_id),
    CONSTRAINT CK_receipts_amount
        CHECK (amount <> 0)
);

-- -----------------------------------------------------------------------------
-- 請求ヘッダ履歴
-- -----------------------------------------------------------------------------
CREATE TABLE invoice_headers (
    invoice_header_id       INT             NOT NULL IDENTITY(1,1),
    customer_id             INT             NOT NULL,
    customer_code           NVARCHAR(20)    NOT NULL,   -- ジャーナル保持
    customer_name           NVARCHAR(100)   NOT NULL,   -- ジャーナル保持
    invoice_date            DATE            NOT NULL,   -- 請求締日
    previous_invoice_amount DECIMAL(12, 2)  NOT NULL,   -- 前回請求額
    receipt_amount          DECIMAL(12, 2)  NOT NULL,   -- 入金額
    sales_amount_standard   DECIMAL(12, 2)  NOT NULL,   -- 売上額_標準税率
    sales_amount_reduced    DECIMAL(12, 2)  NOT NULL,   -- 売上額_軽減税率
    tax_amount_standard     DECIMAL(12, 2)  NOT NULL,   -- 消費税額_標準税率
    tax_amount_reduced      DECIMAL(12, 2)  NOT NULL,   -- 消費税額_軽減税率
    current_invoice_amount  DECIMAL(12, 2)  NOT NULL,   -- 今回請求額
    is_deleted              BIT             NOT NULL CONSTRAINT DF_invoice_headers_is_deleted DEFAULT 0,
    row_version             ROWVERSION      NOT NULL,
    CONSTRAINT PK_invoice_headers PRIMARY KEY (invoice_header_id),
    CONSTRAINT UQ_invoice_headers UNIQUE (customer_id, invoice_date),
    CONSTRAINT FK_invoice_headers_customers FOREIGN KEY (customer_id)
        REFERENCES customers (customer_id)
);

-- -----------------------------------------------------------------------------
-- 売掛金集計履歴
-- -----------------------------------------------------------------------------
CREATE TABLE accounts_receivable_histories (
    ar_history_id           INT             NOT NULL IDENTITY(1,1),
    customer_id             INT             NOT NULL,
    customer_code           NVARCHAR(20)    NOT NULL,   -- ジャーナル保持
    customer_name           NVARCHAR(100)   NOT NULL,   -- ジャーナル保持
    closing_date            DATE            NOT NULL,   -- 集計締日（月末日付）
    carried_over_amount     DECIMAL(12, 2)  NOT NULL,   -- 前月繰越残高
    sales_amount_standard   DECIMAL(12, 2)  NOT NULL,   -- 当月売上額_標準税率
    sales_amount_reduced    DECIMAL(12, 2)  NOT NULL,   -- 当月売上額_軽減税率
    tax_amount_standard     DECIMAL(12, 2)  NOT NULL,   -- 当月消費税額_標準税率
    tax_amount_reduced      DECIMAL(12, 2)  NOT NULL,   -- 当月消費税額_軽減税率
    receipt_amount          DECIMAL(12, 2)  NOT NULL,   -- 当月入金額
    closing_amount          DECIMAL(12, 2)  NOT NULL,   -- 当月末残高
    is_deleted              BIT             NOT NULL CONSTRAINT DF_ar_histories_is_deleted DEFAULT 0,
    row_version             ROWVERSION      NOT NULL,
    CONSTRAINT PK_accounts_receivable_histories PRIMARY KEY (ar_history_id),
    CONSTRAINT UQ_ar_histories UNIQUE (customer_id, closing_date),
    CONSTRAINT FK_ar_histories_customers FOREIGN KEY (customer_id)
        REFERENCES customers (customer_id),
    CONSTRAINT CK_ar_histories_closing_date
        CHECK (closing_date = EOMONTH(closing_date))
);

-- =============================================================================
-- 初期データ
-- =============================================================================

INSERT INTO payment_method_classifications (payment_method_code, payment_method_name) VALUES
    ('01', '現金'),
    ('02', '振込'),
    ('03', '手形'),
    ('04', '小切手'),
    ('05', 'その他');

INSERT INTO tax_type_classifications (tax_type_code, tax_type_name) VALUES
    ('01', '外税'),
    ('02', '内税'),
    ('03', '非課税');

INSERT INTO tax_category_classifications (tax_category_code, tax_category_name) VALUES
    ('01', '通常'),
    ('02', '軽減税率'),
    ('03', '予備');

INSERT INTO tax_fraction_classifications (tax_fraction_code, tax_fraction_name) VALUES
    ('01', '切捨'),
    ('02', '切上'),
    ('03', '四捨五入');

INSERT INTO tax_calc_unit_classifications (tax_calc_unit_code, tax_calc_unit_name) VALUES
    ('01', '明細'),
    ('02', '伝票');

INSERT INTO tax_rate_histories (tax_category_id, rate, start_date, end_date) VALUES
    (1, 0.1000, '2019-10-01', NULL),   -- 通常税率 10%
    (2, 0.0800, '2019-10-01', NULL);   -- 軽減税率 8%
