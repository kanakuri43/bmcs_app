-- =============================================================
-- Database : bmcs_db
-- Server   : 172.16.6.11
-- Extracted: 2026-03-24 09:37:23
-- =============================================================

-- ===== TABLES =====================================================

-- ----- Table: accounts_receivable_histories -----
CREATE TABLE [dbo].[accounts_receivable_histories] (
    [ar_history_id] int IDENTITY(1,1) NOT NULL,
    [customer_id] int NOT NULL,
    [customer_code] nvarchar(20) NOT NULL,
    [customer_name] nvarchar(100) NOT NULL,
    [closing_date] date NOT NULL,
    [carried_over_amount] decimal(12,2) NOT NULL,
    [sales_amount_standard] decimal(12,2) NOT NULL,
    [sales_amount_reduced] decimal(12,2) NOT NULL,
    [tax_amount_standard] decimal(12,2) NOT NULL,
    [tax_amount_reduced] decimal(12,2) NOT NULL,
    [receipt_amount] decimal(12,2) NOT NULL,
    [closing_amount] decimal(12,2) NOT NULL,
    [is_deleted] bit DEFAULT ((0)) NOT NULL,
    [row_version] timestamp NOT NULL,
    CONSTRAINT [PK_accounts_receivable_histories] PRIMARY KEY CLUSTERED ([ar_history_id]),
    CONSTRAINT [FK_ar_histories_customers] FOREIGN KEY ([customer_id]) REFERENCES [dbo].[customers] ([customer_id])
);
GO

CREATE UNIQUE INDEX [UQ_ar_histories] ON [dbo].[accounts_receivable_histories] ([customer_id] ASC, [closing_date] ASC);
GO

-- ----- Table: company_info -----
CREATE TABLE [dbo].[company_info] (
    [company_info_id] int IDENTITY(1,1) NOT NULL,
    [company_name] nvarchar(100) NOT NULL,
    [address] nvarchar(200) NULL,
    [tel] nvarchar(20) NULL,
    [fax] nvarchar(20) NULL,
    [invoice_no] nvarchar(20) NULL,
    CONSTRAINT [PK_company_info] PRIMARY KEY CLUSTERED ([company_info_id])
);
GO

-- ----- Table: customers -----
CREATE TABLE [dbo].[customers] (
    [customer_id] int IDENTITY(1,1) NOT NULL,
    [customer_code] nvarchar(20) NOT NULL,
    [customer_name] nvarchar(100) NOT NULL,
    [closing_day] tinyint NOT NULL,
    [tax_fraction_id] int NOT NULL,
    [tax_calc_unit_id] int NOT NULL,
    [employee_id] int NULL,
    [is_deleted] bit DEFAULT ((0)) NOT NULL,
    [row_version] timestamp NOT NULL,
    CONSTRAINT [PK_customers] PRIMARY KEY CLUSTERED ([customer_id]),
    CONSTRAINT [FK_customers_employees] FOREIGN KEY ([employee_id]) REFERENCES [dbo].[employees] ([employee_id]),
    CONSTRAINT [FK_customers_tax_calc_unit] FOREIGN KEY ([tax_calc_unit_id]) REFERENCES [dbo].[tax_calc_unit_classifications] ([tax_calc_unit_id]),
    CONSTRAINT [FK_customers_tax_fraction] FOREIGN KEY ([tax_fraction_id]) REFERENCES [dbo].[tax_fraction_classifications] ([tax_fraction_id])
);
GO

CREATE UNIQUE INDEX [UQ_customers_code] ON [dbo].[customers] ([customer_code] ASC);
GO

-- ----- Table: employees -----
CREATE TABLE [dbo].[employees] (
    [employee_id] int IDENTITY(1,1) NOT NULL,
    [employee_code] nvarchar(20) NOT NULL,
    [employee_name] nvarchar(50) NOT NULL,
    [is_deleted] bit DEFAULT ((0)) NOT NULL,
    [row_version] timestamp NOT NULL,
    CONSTRAINT [PK_employees] PRIMARY KEY CLUSTERED ([employee_id])
);
GO

CREATE UNIQUE INDEX [UQ_employees_code] ON [dbo].[employees] ([employee_code] ASC);
GO

-- ----- Table: invoice_headers -----
CREATE TABLE [dbo].[invoice_headers] (
    [invoice_header_id] int IDENTITY(1,1) NOT NULL,
    [customer_id] int NOT NULL,
    [customer_code] nvarchar(20) NOT NULL,
    [customer_name] nvarchar(100) NOT NULL,
    [invoice_date] date NOT NULL,
    [previous_invoice_amount] decimal(12,2) NOT NULL,
    [receipt_amount] decimal(12,2) NOT NULL,
    [sales_amount_standard] decimal(12,2) NOT NULL,
    [sales_amount_reduced] decimal(12,2) NOT NULL,
    [tax_amount_standard] decimal(12,2) NOT NULL,
    [tax_amount_reduced] decimal(12,2) NOT NULL,
    [current_invoice_amount] decimal(12,2) NOT NULL,
    [is_deleted] bit DEFAULT ((0)) NOT NULL,
    [row_version] timestamp NOT NULL,
    CONSTRAINT [PK_invoice_headers] PRIMARY KEY CLUSTERED ([invoice_header_id]),
    CONSTRAINT [FK_invoice_headers_customers] FOREIGN KEY ([customer_id]) REFERENCES [dbo].[customers] ([customer_id])
);
GO

CREATE UNIQUE INDEX [UQ_invoice_headers] ON [dbo].[invoice_headers] ([customer_id] ASC, [invoice_date] ASC);
GO

-- ----- Table: orders -----
CREATE TABLE [dbo].[orders] (
    [order_id] int IDENTITY(1,1) NOT NULL,
    [order_no] nvarchar(20) NOT NULL,
    [order_date] date NOT NULL,
    [customer_id] int NOT NULL,
    [customer_code] nvarchar(20) NOT NULL,
    [customer_name] nvarchar(100) NOT NULL,
    [line_no] int NOT NULL,
    [product_id] int NOT NULL,
    [product_code] nvarchar(20) NOT NULL,
    [product_name] nvarchar(100) NOT NULL,
    [quantity] decimal(10,2) NOT NULL,
    [unit_price] decimal(10,2) NOT NULL,
    [tax_type_id] int NOT NULL,
    [tax_calc_unit_id] int NOT NULL,
    [tax_fraction_id] int NOT NULL,
    [tax_amount] decimal(10,2) NULL,
    [slip_tax_amount] decimal(10,2) NULL,
    [slip_remarks] nvarchar(200) NULL,
    [line_remarks] nvarchar(200) NULL,
    [is_deleted] bit DEFAULT ((0)) NOT NULL,
    [row_version] timestamp NOT NULL,
    [applied_tax_rate] decimal(5,4) NULL,
    [tax_rate_type] tinyint NOT NULL,
    CONSTRAINT [PK_orders] PRIMARY KEY CLUSTERED ([order_id]),
    CONSTRAINT [FK_orders_customers] FOREIGN KEY ([customer_id]) REFERENCES [dbo].[customers] ([customer_id]),
    CONSTRAINT [FK_orders_products] FOREIGN KEY ([product_id]) REFERENCES [dbo].[products] ([product_id]),
    CONSTRAINT [FK_orders_tax_calc_unit] FOREIGN KEY ([tax_calc_unit_id]) REFERENCES [dbo].[tax_calc_unit_classifications] ([tax_calc_unit_id]),
    CONSTRAINT [FK_orders_tax_fraction] FOREIGN KEY ([tax_fraction_id]) REFERENCES [dbo].[tax_fraction_classifications] ([tax_fraction_id]),
    CONSTRAINT [FK_orders_tax_type] FOREIGN KEY ([tax_type_id]) REFERENCES [dbo].[tax_type_classifications] ([tax_type_id])
);
GO

CREATE UNIQUE INDEX [UQ_orders_line] ON [dbo].[orders] ([order_no] ASC, [line_no] ASC);
GO

-- ----- Table: payment_method_classifications -----
CREATE TABLE [dbo].[payment_method_classifications] (
    [payment_method_id] int IDENTITY(1,1) NOT NULL,
    [payment_method_code] nvarchar(20) NOT NULL,
    [payment_method_name] nvarchar(50) NOT NULL,
    [is_deleted] bit DEFAULT ((0)) NOT NULL,
    [row_version] timestamp NOT NULL,
    CONSTRAINT [PK_payment_method_classifications] PRIMARY KEY CLUSTERED ([payment_method_id])
);
GO

CREATE UNIQUE INDEX [UQ_payment_method_classifications_code] ON [dbo].[payment_method_classifications] ([payment_method_code] ASC);
GO

-- ----- Table: products -----
CREATE TABLE [dbo].[products] (
    [product_id] int IDENTITY(1,1) NOT NULL,
    [product_code] nvarchar(20) NOT NULL,
    [product_name] nvarchar(100) NOT NULL,
    [tax_type_id] int NOT NULL,
    [is_deleted] bit DEFAULT ((0)) NOT NULL,
    [row_version] timestamp NOT NULL,
    [tax_rate_type] tinyint NOT NULL,
    CONSTRAINT [PK_products] PRIMARY KEY CLUSTERED ([product_id]),
    CONSTRAINT [FK_products_tax_type] FOREIGN KEY ([tax_type_id]) REFERENCES [dbo].[tax_type_classifications] ([tax_type_id])
);
GO

CREATE UNIQUE INDEX [UQ_products_code] ON [dbo].[products] ([product_code] ASC);
GO

-- ----- Table: receipts -----
CREATE TABLE [dbo].[receipts] (
    [receipt_id] int IDENTITY(1,1) NOT NULL,
    [receipt_no] nvarchar(20) NOT NULL,
    [receipt_date] date NOT NULL,
    [customer_id] int NOT NULL,
    [customer_code] nvarchar(20) NOT NULL,
    [customer_name] nvarchar(100) NOT NULL,
    [line_no] int NOT NULL,
    [payment_method_id] int NOT NULL,
    [amount] decimal(12,2) NOT NULL,
    [invoiced_date] date NULL,
    [slip_remarks] nvarchar(200) NULL,
    [line_remarks] nvarchar(200) NULL,
    [is_deleted] bit DEFAULT ((0)) NOT NULL,
    [row_version] timestamp NOT NULL,
    CONSTRAINT [PK_receipts] PRIMARY KEY CLUSTERED ([receipt_id]),
    CONSTRAINT [FK_receipts_customers] FOREIGN KEY ([customer_id]) REFERENCES [dbo].[customers] ([customer_id]),
    CONSTRAINT [FK_receipts_payment_method] FOREIGN KEY ([payment_method_id]) REFERENCES [dbo].[payment_method_classifications] ([payment_method_id])
);
GO

CREATE UNIQUE INDEX [UQ_receipts_line] ON [dbo].[receipts] ([receipt_no] ASC, [line_no] ASC);
GO

-- ----- Table: sales -----
CREATE TABLE [dbo].[sales] (
    [sale_id] int IDENTITY(1,1) NOT NULL,
    [sale_no] nvarchar(20) NOT NULL,
    [sale_date] date NOT NULL,
    [customer_id] int NOT NULL,
    [customer_code] nvarchar(20) NOT NULL,
    [customer_name] nvarchar(100) NOT NULL,
    [order_id] int NULL,
    [order_no] nvarchar(20) NULL,
    [employee_id] int NOT NULL,
    [employee_code] nvarchar(20) NOT NULL,
    [employee_name] nvarchar(50) NOT NULL,
    [line_no] int NOT NULL,
    [product_id] int NOT NULL,
    [product_code] nvarchar(20) NOT NULL,
    [product_name] nvarchar(100) NOT NULL,
    [quantity] decimal(10,2) NOT NULL,
    [unit_price] decimal(10,2) NOT NULL,
    [tax_type_id] int NOT NULL,
    [tax_calc_unit_id] int NOT NULL,
    [tax_fraction_id] int NOT NULL,
    [line_tax_amount] decimal(10,2) NULL,
    [slip_tax_amount] decimal(10,2) NULL,
    [invoiced_date] date NULL,
    [slip_remarks] nvarchar(200) NULL,
    [line_remarks] nvarchar(200) NULL,
    [is_deleted] bit DEFAULT ((0)) NOT NULL,
    [row_version] timestamp NOT NULL,
    [applied_tax_rate] decimal(5,4) NULL,
    [tax_rate_type] tinyint NOT NULL,
    CONSTRAINT [PK_sales] PRIMARY KEY CLUSTERED ([sale_id]),
    CONSTRAINT [FK_sales_customers] FOREIGN KEY ([customer_id]) REFERENCES [dbo].[customers] ([customer_id]),
    CONSTRAINT [FK_sales_employees] FOREIGN KEY ([employee_id]) REFERENCES [dbo].[employees] ([employee_id]),
    CONSTRAINT [FK_sales_orders] FOREIGN KEY ([order_id]) REFERENCES [dbo].[orders] ([order_id]),
    CONSTRAINT [FK_sales_products] FOREIGN KEY ([product_id]) REFERENCES [dbo].[products] ([product_id]),
    CONSTRAINT [FK_sales_tax_calc_unit] FOREIGN KEY ([tax_calc_unit_id]) REFERENCES [dbo].[tax_calc_unit_classifications] ([tax_calc_unit_id]),
    CONSTRAINT [FK_sales_tax_fraction] FOREIGN KEY ([tax_fraction_id]) REFERENCES [dbo].[tax_fraction_classifications] ([tax_fraction_id]),
    CONSTRAINT [FK_sales_tax_type] FOREIGN KEY ([tax_type_id]) REFERENCES [dbo].[tax_type_classifications] ([tax_type_id])
);
GO

CREATE UNIQUE INDEX [UQ_sales_line] ON [dbo].[sales] ([sale_no] ASC, [line_no] ASC);
GO

-- ----- Table: tax_calc_unit_classifications -----
CREATE TABLE [dbo].[tax_calc_unit_classifications] (
    [tax_calc_unit_id] int IDENTITY(1,1) NOT NULL,
    [tax_calc_unit_code] nvarchar(20) NOT NULL,
    [tax_calc_unit_name] nvarchar(50) NOT NULL,
    [is_deleted] bit DEFAULT ((0)) NOT NULL,
    [row_version] timestamp NOT NULL,
    CONSTRAINT [PK_tax_calc_unit_classifications] PRIMARY KEY CLUSTERED ([tax_calc_unit_id])
);
GO

CREATE UNIQUE INDEX [UQ_tax_calc_unit_classifications_code] ON [dbo].[tax_calc_unit_classifications] ([tax_calc_unit_code] ASC);
GO

-- ----- Table: tax_fraction_classifications -----
CREATE TABLE [dbo].[tax_fraction_classifications] (
    [tax_fraction_id] int IDENTITY(1,1) NOT NULL,
    [tax_fraction_code] nvarchar(20) NOT NULL,
    [tax_fraction_name] nvarchar(50) NOT NULL,
    [is_deleted] bit DEFAULT ((0)) NOT NULL,
    [row_version] timestamp NOT NULL,
    CONSTRAINT [PK_tax_fraction_classifications] PRIMARY KEY CLUSTERED ([tax_fraction_id])
);
GO

CREATE UNIQUE INDEX [UQ_tax_fraction_classifications_code] ON [dbo].[tax_fraction_classifications] ([tax_fraction_code] ASC);
GO

-- ----- Table: tax_rate_periods -----
CREATE TABLE [dbo].[tax_rate_periods] (
    [tax_rate_period_id] int IDENTITY(1,1) NOT NULL,
    [start_date] date NOT NULL,
    [end_date] date NULL,
    [primary_tax_rate] decimal(5,4) NOT NULL,
    [secondary_tax_rate] decimal(5,4) NOT NULL,
    [tertiary_tax_rate] decimal(5,4) NULL,
    [is_deleted] bit DEFAULT ((0)) NOT NULL,
    [row_version] timestamp NOT NULL,
    CONSTRAINT [PK_tax_rate_periods] PRIMARY KEY CLUSTERED ([tax_rate_period_id])
);
GO

-- ----- Table: tax_type_classifications -----
CREATE TABLE [dbo].[tax_type_classifications] (
    [tax_type_id] int IDENTITY(1,1) NOT NULL,
    [tax_type_code] nvarchar(20) NOT NULL,
    [tax_type_name] nvarchar(50) NOT NULL,
    [is_deleted] bit DEFAULT ((0)) NOT NULL,
    [row_version] timestamp NOT NULL,
    CONSTRAINT [PK_tax_type_classifications] PRIMARY KEY CLUSTERED ([tax_type_id])
);
GO

CREATE UNIQUE INDEX [UQ_tax_type_classifications_code] ON [dbo].[tax_type_classifications] ([tax_type_code] ASC);
GO


-- ===== STORED PROCEDURES & FUNCTIONS ====================================

-- ===== STORED PROCEDURES =====

-- ----- usp_company_info_upsert -----
-- ============================================================
-- usp_company_info_upsert（自社情報は単一行のためupsertのみ）
-- ============================================================
CREATE   PROCEDURE dbo.usp_company_info_upsert
    @company_name NVARCHAR(100),
    @address      NVARCHAR(200) = NULL,
    @tel          NVARCHAR(20)  = NULL,
    @fax          NVARCHAR(20)  = NULL,
    @invoice_no   NVARCHAR(20)  = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF EXISTS (SELECT 1 FROM company_info)
    BEGIN
        UPDATE company_info
        SET company_name = @company_name,
            address      = @address,
            tel          = @tel,
            fax          = @fax,
            invoice_no   = @invoice_no;
    END
    ELSE
    BEGIN
        INSERT INTO company_info (company_name, address, tel, fax, invoice_no)
        VALUES (@company_name, @address, @tel, @fax, @invoice_no);
    END
END
GO

-- ----- usp_customers_delete -----
-- ============================================================
-- usp_customers_delete
-- ============================================================
CREATE   PROCEDURE dbo.usp_customers_delete
    @customer_id INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM customers WHERE customer_id = @customer_id AND is_deleted = 0)
    BEGIN
        RAISERROR(N'得意先ID %d が存在しないか削除済みです。', 16, 1, @customer_id);
        RETURN;
    END
    IF EXISTS (SELECT 1 FROM orders WHERE customer_id = @customer_id AND is_deleted = 0)
    BEGIN
        RAISERROR(N'受注データが存在するため削除できません。', 16, 1);
        RETURN;
    END
    IF EXISTS (SELECT 1 FROM sales WHERE customer_id = @customer_id AND is_deleted = 0)
    BEGIN
        RAISERROR(N'売上データが存在するため削除できません。', 16, 1);
        RETURN;
    END
    IF EXISTS (SELECT 1 FROM receipts WHERE customer_id = @customer_id AND is_deleted = 0)
    BEGIN
        RAISERROR(N'入金データが存在するため削除できません。', 16, 1);
        RETURN;
    END
    UPDATE customers SET is_deleted = 1 WHERE customer_id = @customer_id AND is_deleted = 0;
END
GO

-- ----- usp_customers_upsert -----
-- ============================================================
-- usp_customers_upsert
-- ============================================================
CREATE   PROCEDURE dbo.usp_customers_upsert
    @customer_id     INT           = NULL,
    @customer_code   NVARCHAR(20),
    @customer_name   NVARCHAR(100),
    @closing_day     TINYINT,
    @tax_fraction_id INT,
    @tax_calc_unit_id INT,
    @employee_id     INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- 締日チェック（1〜27 または 99=月末）
    IF @closing_day NOT BETWEEN 1 AND 27 AND @closing_day <> 99
    BEGIN
        RAISERROR(N'締日は1〜27または99（月末）を指定してください。', 16, 1);
        RETURN;
    END
    -- 端数処理区分チェック
    IF NOT EXISTS (SELECT 1 FROM tax_fraction_classifications WHERE tax_fraction_id = @tax_fraction_id AND is_deleted = 0)
    BEGIN
        RAISERROR(N'消費税端数処理区分が存在しないか削除済みです。', 16, 1);
        RETURN;
    END
    -- 計算単位区分チェック
    IF NOT EXISTS (SELECT 1 FROM tax_calc_unit_classifications WHERE tax_calc_unit_id = @tax_calc_unit_id AND is_deleted = 0)
    BEGIN
        RAISERROR(N'消費税計算単位区分が存在しないか削除済みです。', 16, 1);
        RETURN;
    END
    -- 担当社員チェック（指定時のみ）
    IF @employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees WHERE employee_id = @employee_id AND is_deleted = 0)
    BEGIN
        RAISERROR(N'担当社員が存在しないか削除済みです。', 16, 1);
        RETURN;
    END
    -- コード重複チェック
    IF EXISTS (
        SELECT 1 FROM customers
        WHERE customer_code = @customer_code
          AND is_deleted = 0
          AND (@customer_id IS NULL OR customer_id <> @customer_id)
    )
    BEGIN
        RAISERROR(N'得意先コード %s は既に使用されています。', 16, 1, @customer_code);
        RETURN;
    END

    IF @customer_id IS NULL
    BEGIN
        INSERT INTO customers (customer_code, customer_name, closing_day, tax_fraction_id, tax_calc_unit_id, employee_id, is_deleted)
        VALUES (@customer_code, @customer_name, @closing_day, @tax_fraction_id, @tax_calc_unit_id, @employee_id, 0);
    END
    ELSE
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM customers WHERE customer_id = @customer_id AND is_deleted = 0)
        BEGIN
            RAISERROR(N'得意先ID %d が存在しないか削除済みです。', 16, 1, @customer_id);
            RETURN;
        END
        UPDATE customers
        SET customer_code    = @customer_code,
            customer_name    = @customer_name,
            closing_day      = @closing_day,
            tax_fraction_id  = @tax_fraction_id,
            tax_calc_unit_id = @tax_calc_unit_id,
            employee_id      = @employee_id
        WHERE customer_id = @customer_id AND is_deleted = 0;
    END
END
GO

-- ----- usp_employees_delete -----
-- ============================================================
-- usp_employees_delete
-- ============================================================
CREATE   PROCEDURE dbo.usp_employees_delete
    @employee_id INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM employees WHERE employee_id = @employee_id AND is_deleted = 0)
    BEGIN
        RAISERROR(N'社員ID %d が存在しないか削除済みです。', 16, 1, @employee_id);
        RETURN;
    END
    IF EXISTS (SELECT 1 FROM sales WHERE employee_id = @employee_id AND is_deleted = 0)
    BEGIN
        RAISERROR(N'売上データが存在するため削除できません。', 16, 1);
        RETURN;
    END
    IF EXISTS (SELECT 1 FROM customers WHERE employee_id = @employee_id AND is_deleted = 0)
    BEGIN
        RAISERROR(N'得意先の担当者に設定されているため削除できません。', 16, 1);
        RETURN;
    END
    UPDATE employees SET is_deleted = 1 WHERE employee_id = @employee_id AND is_deleted = 0;
END
GO

-- ----- usp_employees_upsert -----
-- ============================================================
-- usp_employees_upsert
-- ============================================================
CREATE   PROCEDURE dbo.usp_employees_upsert
    @employee_id   INT           = NULL,
    @employee_code NVARCHAR(20),
    @employee_name NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- コード重複チェック（他レコード）
    IF EXISTS (
        SELECT 1 FROM employees
        WHERE employee_code = @employee_code
          AND is_deleted = 0
          AND (@employee_id IS NULL OR employee_id <> @employee_id)
    )
    BEGIN
        RAISERROR(N'社員コード %s は既に使用されています。', 16, 1, @employee_code);
        RETURN;
    END

    IF @employee_id IS NULL
    BEGIN
        INSERT INTO employees (employee_code, employee_name, is_deleted)
        VALUES (@employee_code, @employee_name, 0);
    END
    ELSE
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM employees WHERE employee_id = @employee_id AND is_deleted = 0)
        BEGIN
            RAISERROR(N'社員ID %d が存在しないか削除済みです。', 16, 1, @employee_id);
            RETURN;
        END
        UPDATE employees
        SET employee_code = @employee_code,
            employee_name = @employee_name
        WHERE employee_id = @employee_id AND is_deleted = 0;
    END
END
GO

-- ----- usp_orders_delete -----
-- =============================================================================
-- usp_orders_delete（変更なし）
-- =============================================================================
CREATE   PROCEDURE dbo.usp_orders_delete
    @order_no NVARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM orders WHERE order_no = @order_no AND is_deleted = 0)
    BEGIN
        RAISERROR(N'伝票番号 %s が存在しません。', 16, 1, @order_no);
        RETURN;
    END

    IF EXISTS (
        SELECT 1 FROM sales s
        INNER JOIN orders o ON o.order_id = s.order_id
        WHERE o.order_no = @order_no AND o.is_deleted = 0 AND s.is_deleted = 0
    )
    BEGIN
        RAISERROR(N'売上登録済みの受注は削除できません。', 16, 1);
        RETURN;
    END

    BEGIN TRANSACTION;
    UPDATE orders SET is_deleted = 1 WHERE order_no = @order_no AND is_deleted = 0;
    COMMIT TRANSACTION;
END;
GO

-- ----- usp_orders_select -----
-- =============================================================================
-- usp_orders_select
-- =============================================================================
CREATE   PROCEDURE dbo.usp_orders_select
    @order_no    NVARCHAR(20) = NULL,
    @customer_id INT          = NULL,
    @date_from   DATE         = NULL,
    @date_to     DATE         = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        o.order_id,
        o.order_no,
        o.order_date,
        o.customer_id,
        o.customer_code,
        o.customer_name,
        o.line_no,
        o.product_id,
        o.product_code,
        o.product_name,
        o.quantity,
        o.unit_price,
        o.quantity * o.unit_price AS line_amount,
        o.tax_type_id,
        tt.tax_type_name,
        o.tax_rate_type,
        o.tax_calc_unit_id,
        tu.tax_calc_unit_name,
        o.tax_fraction_id,
        tf.tax_fraction_name,
        o.applied_tax_rate,
        o.tax_amount,
        o.slip_tax_amount,
        o.slip_remarks,
        o.line_remarks,
        CASE WHEN EXISTS (
            SELECT 1 FROM sales s WHERE s.order_id = o.order_id AND s.is_deleted = 0
        ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS has_sales,
        o.row_version
    FROM orders o
    INNER JOIN tax_type_classifications      tt ON tt.tax_type_id      = o.tax_type_id
    INNER JOIN tax_calc_unit_classifications tu ON tu.tax_calc_unit_id = o.tax_calc_unit_id
    INNER JOIN tax_fraction_classifications  tf ON tf.tax_fraction_id  = o.tax_fraction_id
    WHERE o.is_deleted = 0
      AND (@order_no    IS NULL OR o.order_no    = @order_no)
      AND (@customer_id IS NULL OR o.customer_id = @customer_id)
      AND (@date_from   IS NULL OR o.order_date >= @date_from)
      AND (@date_to     IS NULL OR o.order_date <= @date_to)
    ORDER BY o.order_no, o.line_no;
END;
GO

-- ----- usp_orders_upsert -----
-- =============================================================================
-- usp_orders_upsert
-- =============================================================================
CREATE   PROCEDURE dbo.usp_orders_upsert
    @order_no     NVARCHAR(20),
    @order_date   DATE,
    @customer_id  INT,
    @slip_remarks NVARCHAR(200) = NULL,
    @lines        NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- JSON パース
    IF ISJSON(@lines) = 0
    BEGIN
        RAISERROR(N'@lines が有効なJSON形式ではありません。', 16, 1);
        RETURN;
    END

    SELECT
        CAST(j.line_no          AS INT)            AS line_no,
        CAST(j.product_id       AS INT)            AS product_id,
        CAST(j.product_code     AS NVARCHAR(20))   AS product_code,
        CAST(j.product_name     AS NVARCHAR(100))  AS product_name,
        CAST(j.quantity         AS DECIMAL(10,2))  AS quantity,
        CAST(j.unit_price       AS DECIMAL(10,2))  AS unit_price,
        CAST(j.tax_type_id      AS INT)            AS tax_type_id,
        CAST(j.tax_rate_type    AS TINYINT)        AS tax_rate_type,
        CAST(j.applied_tax_rate AS DECIMAL(5,4))   AS applied_tax_rate,
        CAST(j.tax_amount       AS DECIMAL(10,2))  AS tax_amount,
        CAST(j.slip_tax_amount  AS DECIMAL(10,2))  AS slip_tax_amount,
        CAST(j.line_remarks     AS NVARCHAR(200))  AS line_remarks
    INTO #lines
    FROM OPENJSON(@lines) WITH (
        line_no          INT             N'$.line_no',
        product_id       INT             N'$.product_id',
        product_code     NVARCHAR(20)    N'$.product_code',
        product_name     NVARCHAR(100)   N'$.product_name',
        quantity         DECIMAL(10,2)   N'$.quantity',
        unit_price       DECIMAL(10,2)   N'$.unit_price',
        tax_type_id      INT             N'$.tax_type_id',
        tax_rate_type    TINYINT         N'$.tax_rate_type',
        applied_tax_rate DECIMAL(5,4)    N'$.applied_tax_rate',
        tax_amount       DECIMAL(10,2)   N'$.tax_amount',
        slip_tax_amount  DECIMAL(10,2)   N'$.slip_tax_amount',
        line_remarks     NVARCHAR(200)   N'$.line_remarks'
    ) j;

    -- 売上登録済みチェック（更新時のみ）
    IF EXISTS (SELECT 1 FROM orders WHERE order_no = @order_no AND is_deleted = 0)
       AND EXISTS (
        SELECT 1 FROM sales s
        INNER JOIN orders o ON o.order_id = s.order_id
        WHERE o.order_no = @order_no AND o.is_deleted = 0 AND s.is_deleted = 0
    )
    BEGIN
        RAISERROR(N'売上登録済みの受注は変更できません。', 16, 1);
        RETURN;
    END

    -- 得意先の存在・削除チェック
    DECLARE @customer_code    NVARCHAR(20);
    DECLARE @customer_name    NVARCHAR(100);
    DECLARE @tax_calc_unit_id INT;
    DECLARE @tax_fraction_id  INT;

    SELECT
        @customer_code    = customer_code,
        @customer_name    = customer_name,
        @tax_calc_unit_id = tax_calc_unit_id,
        @tax_fraction_id  = tax_fraction_id
    FROM customers
    WHERE customer_id = @customer_id AND is_deleted = 0;

    IF @customer_code IS NULL
    BEGIN
        RAISERROR(N'得意先が存在しないか削除済みです。', 16, 1);
        RETURN;
    END

    -- 明細行の存在チェック
    IF NOT EXISTS (SELECT 1 FROM #lines)
    BEGIN
        RAISERROR(N'明細行を1件以上入力してください。', 16, 1);
        RETURN;
    END

    -- 商品の存在・削除チェック
    IF EXISTS (
        SELECT 1 FROM #lines l
        WHERE NOT EXISTS (SELECT 1 FROM products p WHERE p.product_id = l.product_id AND p.is_deleted = 0)
    )
    BEGIN
        RAISERROR(N'存在しないか削除済みの商品が含まれています。', 16, 1);
        RETURN;
    END

    -- 差し替え（論理削除 → INSERT）
    BEGIN TRANSACTION;

    UPDATE orders SET is_deleted = 1 WHERE order_no = @order_no AND is_deleted = 0;

    INSERT INTO orders (
        order_no, order_date,
        customer_id, customer_code, customer_name,
        line_no,
        product_id, product_code, product_name,
        quantity, unit_price,
        tax_type_id, tax_rate_type, tax_calc_unit_id, tax_fraction_id,
        applied_tax_rate, tax_amount, slip_tax_amount,
        slip_remarks, line_remarks
    )
    SELECT
        @order_no, @order_date,
        @customer_id, @customer_code, @customer_name,
        l.line_no,
        l.product_id, l.product_code, l.product_name,
        l.quantity, l.unit_price,
        l.tax_type_id, l.tax_rate_type, @tax_calc_unit_id, @tax_fraction_id,
        l.applied_tax_rate, l.tax_amount, l.slip_tax_amount,
        @slip_remarks, l.line_remarks
    FROM #lines l;

    COMMIT TRANSACTION;
    DROP TABLE #lines;
END;
GO

-- ----- usp_payment_method_classifications_delete -----
-- ============================================================
-- usp_payment_method_classifications_delete
-- ============================================================
CREATE   PROCEDURE dbo.usp_payment_method_classifications_delete
    @payment_method_id INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM payment_method_classifications WHERE payment_method_id = @payment_method_id AND is_deleted = 0)
    BEGIN
        RAISERROR(N'入金方法ID %d が存在しないか削除済みです。', 16, 1, @payment_method_id);
        RETURN;
    END
    IF EXISTS (SELECT 1 FROM receipts WHERE payment_method_id = @payment_method_id AND is_deleted = 0)
    BEGIN
        RAISERROR(N'入金データが存在するため削除できません。', 16, 1);
        RETURN;
    END
    UPDATE payment_method_classifications SET is_deleted = 1 WHERE payment_method_id = @payment_method_id AND is_deleted = 0;
END
GO

-- ----- usp_payment_method_classifications_upsert -----
-- ============================================================
-- usp_payment_method_classifications_upsert
-- ============================================================
CREATE   PROCEDURE dbo.usp_payment_method_classifications_upsert
    @payment_method_id   INT           = NULL,
    @payment_method_code NVARCHAR(20),
    @payment_method_name NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF EXISTS (
        SELECT 1 FROM payment_method_classifications
        WHERE payment_method_code = @payment_method_code
          AND is_deleted = 0
          AND (@payment_method_id IS NULL OR payment_method_id <> @payment_method_id)
    )
    BEGIN
        RAISERROR(N'入金方法コード %s は既に使用されています。', 16, 1, @payment_method_code);
        RETURN;
    END

    IF @payment_method_id IS NULL
    BEGIN
        INSERT INTO payment_method_classifications (payment_method_code, payment_method_name, is_deleted)
        VALUES (@payment_method_code, @payment_method_name, 0);
    END
    ELSE
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM payment_method_classifications WHERE payment_method_id = @payment_method_id AND is_deleted = 0)
        BEGIN
            RAISERROR(N'入金方法ID %d が存在しないか削除済みです。', 16, 1, @payment_method_id);
            RETURN;
        END
        UPDATE payment_method_classifications
        SET payment_method_code = @payment_method_code,
            payment_method_name = @payment_method_name
        WHERE payment_method_id = @payment_method_id AND is_deleted = 0;
    END
END
GO

-- ----- usp_products_delete -----
-- ============================================================
-- usp_products_delete
-- ============================================================
CREATE   PROCEDURE dbo.usp_products_delete
    @product_id INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM products WHERE product_id = @product_id AND is_deleted = 0)
    BEGIN
        RAISERROR(N'商品ID %d が存在しないか削除済みです。', 16, 1, @product_id);
        RETURN;
    END
    IF EXISTS (SELECT 1 FROM orders WHERE product_id = @product_id AND is_deleted = 0)
    BEGIN
        RAISERROR(N'受注データが存在するため削除できません。', 16, 1);
        RETURN;
    END
    IF EXISTS (SELECT 1 FROM sales WHERE product_id = @product_id AND is_deleted = 0)
    BEGIN
        RAISERROR(N'売上データが存在するため削除できません。', 16, 1);
        RETURN;
    END
    UPDATE products SET is_deleted = 1 WHERE product_id = @product_id AND is_deleted = 0;
END
GO

-- ----- usp_products_upsert -----
-- ============================================================
-- usp_products_upsert
-- ============================================================
CREATE   PROCEDURE dbo.usp_products_upsert
    @product_id    INT           = NULL,
    @product_code  NVARCHAR(20),
    @product_name  NVARCHAR(100),
    @tax_type_id   INT,
    @tax_rate_type TINYINT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM tax_type_classifications WHERE tax_type_id = @tax_type_id AND is_deleted = 0)
    BEGIN
        RAISERROR(N'課税種別が存在しないか削除済みです。', 16, 1);
        RETURN;
    END
    IF EXISTS (
        SELECT 1 FROM products
        WHERE product_code = @product_code
          AND is_deleted = 0
          AND (@product_id IS NULL OR product_id <> @product_id)
    )
    BEGIN
        RAISERROR(N'商品コード %s は既に使用されています。', 16, 1, @product_code);
        RETURN;
    END
    IF @product_id IS NULL
    BEGIN
        INSERT INTO products (product_code, product_name, tax_type_id, tax_rate_type, is_deleted)
        VALUES (@product_code, @product_name, @tax_type_id, @tax_rate_type, 0);
    END
    ELSE
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM products WHERE product_id = @product_id AND is_deleted = 0)
        BEGIN
            RAISERROR(N'商品ID %d が存在しないか削除済みです。', 16, 1, @product_id);
            RETURN;
        END
        UPDATE products
        SET product_code  = @product_code,
            product_name  = @product_name,
            tax_type_id   = @tax_type_id,
            tax_rate_type = @tax_rate_type
        WHERE product_id = @product_id AND is_deleted = 0;
    END
END
GO

-- ----- usp_receipts_delete -----

-- =============================================================================
-- 削除 usp_receipts_delete
-- 伝票単位で全行を論理削除
-- =============================================================================
CREATE   PROCEDURE dbo.usp_receipts_delete
    @receipt_no NVARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- 伝票の存在チェック
    IF NOT EXISTS (
        SELECT 1 FROM receipts
        WHERE receipt_no = @receipt_no AND is_deleted = 0
    )
    BEGIN
        RAISERROR(N'伝票番号 %s が存在しません。', 16, 1, @receipt_no);
        RETURN;
    END

    -- 請求集計済みチェック
    IF EXISTS (
        SELECT 1 FROM receipts
        WHERE receipt_no = @receipt_no
          AND is_deleted = 0
          AND invoiced_date IS NOT NULL
    )
    BEGIN
        RAISERROR(N'請求集計済みの伝票は削除できません。', 16, 1);
        RETURN;
    END

    BEGIN TRANSACTION;

    UPDATE receipts
    SET is_deleted = 1
    WHERE receipt_no = @receipt_no AND is_deleted = 0;

    COMMIT TRANSACTION;
END;
GO

-- ----- usp_receipts_select -----

-- =============================================================================
-- 照会 usp_receipts_select
-- 伝票番号 または 条件（得意先・期間）で検索
-- すべてNULLの場合は全件取得
-- =============================================================================
CREATE   PROCEDURE dbo.usp_receipts_select
    @receipt_no  NVARCHAR(20) = NULL,
    @customer_id INT          = NULL,
    @date_from   DATE         = NULL,
    @date_to     DATE         = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        r.receipt_id,
        r.receipt_no,
        r.receipt_date,
        r.customer_id,
        r.customer_code,
        r.customer_name,
        r.line_no,
        r.payment_method_id,
        pm.payment_method_name,
        r.amount,
        r.invoiced_date,
        r.slip_remarks,
        r.line_remarks,
        r.row_version
    FROM receipts r
    INNER JOIN payment_method_classifications pm ON pm.payment_method_id = r.payment_method_id
    WHERE r.is_deleted = 0
      AND (@receipt_no  IS NULL OR r.receipt_no  = @receipt_no)
      AND (@customer_id IS NULL OR r.customer_id = @customer_id)
      AND (@date_from   IS NULL OR r.receipt_date >= @date_from)
      AND (@date_to     IS NULL OR r.receipt_date <= @date_to)
    ORDER BY r.receipt_no, r.line_no;
END;
GO

-- ----- usp_receipts_upsert -----
-- =============================================================================
-- usp_receipts_upsert
-- =============================================================================
CREATE   PROCEDURE dbo.usp_receipts_upsert
    @receipt_no   NVARCHAR(20),
    @receipt_date DATE,
    @customer_id  INT,
    @slip_remarks NVARCHAR(200) = NULL,
    @lines        NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- JSON パース
    IF ISJSON(@lines) = 0
    BEGIN
        RAISERROR(N'@lines が有効なJSON形式ではありません。', 16, 1);
        RETURN;
    END

    SELECT
        CAST(j.line_no           AS INT)            AS line_no,
        CAST(j.payment_method_id AS INT)            AS payment_method_id,
        CAST(j.amount            AS DECIMAL(12,2))  AS amount,
        CAST(j.line_remarks      AS NVARCHAR(200))  AS line_remarks
    INTO #lines
    FROM OPENJSON(@lines) WITH (
        line_no           INT             N'$.line_no',
        payment_method_id INT             N'$.payment_method_id',
        amount            DECIMAL(12,2)   N'$.amount',
        line_remarks      NVARCHAR(200)   N'$.line_remarks'
    ) j;

    -- 請求集計済みチェック（更新時のみ）
    IF EXISTS (
        SELECT 1 FROM receipts
        WHERE receipt_no = @receipt_no
          AND is_deleted = 0
          AND invoiced_date IS NOT NULL
    )
    BEGIN
        RAISERROR(N'請求集計済みの伝票は変更できません。', 16, 1);
        RETURN;
    END

    -- 得意先の存在・削除チェック
    DECLARE @customer_code NVARCHAR(20);
    DECLARE @customer_name NVARCHAR(100);

    SELECT
        @customer_code = customer_code,
        @customer_name = customer_name
    FROM customers
    WHERE customer_id = @customer_id AND is_deleted = 0;

    IF @customer_code IS NULL
    BEGIN
        RAISERROR(N'得意先が存在しないか削除済みです。', 16, 1);
        RETURN;
    END

    -- 請求集計済み期間チェック
    IF EXISTS (
        SELECT 1 FROM invoice_headers
        WHERE customer_id = @customer_id
          AND invoice_date >= @receipt_date
          AND is_deleted = 0
    )
    BEGIN
        DECLARE @receipt_date_str NVARCHAR(10) = CONVERT(NVARCHAR(10), @receipt_date, 23);
        RAISERROR(N'日付 %s は請求集計済みの期間内のため登録できません。', 16, 1, @receipt_date_str);
        RETURN;
    END

    -- 明細行の存在チェック
    IF NOT EXISTS (SELECT 1 FROM #lines)
    BEGIN
        RAISERROR(N'明細行を1件以上入力してください。', 16, 1);
        RETURN;
    END

    -- 入金方法の存在・削除チェック
    IF EXISTS (
        SELECT 1 FROM #lines l
        WHERE NOT EXISTS (
            SELECT 1 FROM payment_method_classifications p
            WHERE p.payment_method_id = l.payment_method_id AND p.is_deleted = 0
        )
    )
    BEGIN
        RAISERROR(N'存在しないか削除済みの入金方法が含まれています。', 16, 1);
        RETURN;
    END

    -- 入金額0チェック
    IF EXISTS (SELECT 1 FROM #lines WHERE amount = 0)
    BEGIN
        RAISERROR(N'入金額0は設定できません。', 16, 1);
        RETURN;
    END

    -- 差し替え（論理削除 → INSERT）
    BEGIN TRANSACTION;

    UPDATE receipts SET is_deleted = 1 WHERE receipt_no = @receipt_no AND is_deleted = 0;

    INSERT INTO receipts (
        receipt_no, receipt_date,
        customer_id, customer_code, customer_name,
        line_no,
        payment_method_id, amount,
        invoiced_date,
        slip_remarks, line_remarks
    )
    SELECT
        @receipt_no, @receipt_date,
        @customer_id, @customer_code, @customer_name,
        l.line_no,
        l.payment_method_id, l.amount,
        NULL,
        @slip_remarks, l.line_remarks
    FROM #lines l;

    COMMIT TRANSACTION;
    DROP TABLE #lines;
END;
GO

-- ----- usp_sales_delete -----
-- =============================================================================
-- usp_sales_delete（変更なし）
-- =============================================================================
CREATE   PROCEDURE dbo.usp_sales_delete
    @sale_no NVARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM sales WHERE sale_no = @sale_no AND is_deleted = 0)
    BEGIN
        RAISERROR(N'伝票番号 %s が存在しません。', 16, 1, @sale_no);
        RETURN;
    END

    IF EXISTS (
        SELECT 1 FROM sales
        WHERE sale_no = @sale_no AND is_deleted = 0 AND invoiced_date IS NOT NULL
    )
    BEGIN
        RAISERROR(N'請求集計済みの伝票は削除できません。', 16, 1);
        RETURN;
    END

    BEGIN TRANSACTION;
    UPDATE sales SET is_deleted = 1 WHERE sale_no = @sale_no AND is_deleted = 0;
    COMMIT TRANSACTION;
END;
GO

-- ----- usp_sales_select -----
-- =============================================================================
-- 照会 usp_sales_select
-- 伝票番号 または 条件（得意先・期間）で検索
-- すべてNULLの場合は全件取得
-- =============================================================================
CREATE   PROCEDURE dbo.usp_sales_select
    @sale_no     NVARCHAR(20) = NULL,
    @customer_id INT          = NULL,
    @date_from   DATE         = NULL,
    @date_to     DATE         = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        s.sale_id,
        s.sale_no,
        s.sale_date,
        s.customer_id,
        s.customer_code,
        s.customer_name,
        s.order_id,
        s.order_no,
        s.employee_id,
        s.employee_code,
        s.employee_name,
        s.line_no,
        s.product_id,
        s.product_code,
        s.product_name,
        s.quantity,
        s.unit_price,
        s.quantity * s.unit_price AS line_amount,
        s.tax_type_id,
        tt.tax_type_name,
        s.tax_rate_type,
        s.applied_tax_rate,
        s.tax_calc_unit_id,
        tu.tax_calc_unit_name,
        s.tax_fraction_id,
        tf.tax_fraction_name,
        s.line_tax_amount,
        s.slip_tax_amount,
        s.invoiced_date,
        s.slip_remarks,
        s.line_remarks,
        s.row_version
    FROM sales s
    INNER JOIN tax_type_classifications      tt ON tt.tax_type_id      = s.tax_type_id
    INNER JOIN tax_calc_unit_classifications tu ON tu.tax_calc_unit_id = s.tax_calc_unit_id
    INNER JOIN tax_fraction_classifications  tf ON tf.tax_fraction_id  = s.tax_fraction_id
    WHERE s.is_deleted = 0
      AND (@sale_no     IS NULL OR s.sale_no     = @sale_no)
      AND (@customer_id IS NULL OR s.customer_id = @customer_id)
      AND (@date_from   IS NULL OR s.sale_date  >= @date_from)
      AND (@date_to     IS NULL OR s.sale_date  <= @date_to)
    ORDER BY s.sale_no, s.line_no;
END;
GO

-- ----- usp_sales_upsert -----
-- =============================================================================
-- usp_sales_upsert
-- =============================================================================
CREATE   PROCEDURE dbo.usp_sales_upsert
    @sale_no      NVARCHAR(20),
    @sale_date    DATE,
    @customer_id  INT,
    @order_id     INT           = NULL,
    @order_no     NVARCHAR(20)  = NULL,
    @employee_id  INT,
    @slip_remarks NVARCHAR(200) = NULL,
    @lines        NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- JSON パース
    IF ISJSON(@lines) = 0
    BEGIN
        RAISERROR(N'@lines が有効なJSON形式ではありません。', 16, 1);
        RETURN;
    END

    SELECT
        CAST(j.line_no           AS INT)            AS line_no,
        CAST(j.product_id        AS INT)            AS product_id,
        CAST(j.product_code      AS NVARCHAR(20))   AS product_code,
        CAST(j.product_name      AS NVARCHAR(100))  AS product_name,
        CAST(j.quantity          AS DECIMAL(10,2))  AS quantity,
        CAST(j.unit_price        AS DECIMAL(10,2))  AS unit_price,
        CAST(j.tax_type_id       AS INT)            AS tax_type_id,
        CAST(j.tax_rate_type     AS TINYINT)        AS tax_rate_type,
        CAST(j.applied_tax_rate  AS DECIMAL(6,4))   AS applied_tax_rate,
        CAST(j.line_tax_amount   AS DECIMAL(10,2))  AS line_tax_amount,
        CAST(j.slip_tax_amount   AS DECIMAL(10,2))  AS slip_tax_amount,
        CAST(j.line_remarks      AS NVARCHAR(200))  AS line_remarks
    INTO #lines
    FROM OPENJSON(@lines) WITH (
        line_no           INT             N'$.line_no',
        product_id        INT             N'$.product_id',
        product_code      NVARCHAR(20)    N'$.product_code',
        product_name      NVARCHAR(100)   N'$.product_name',
        quantity          DECIMAL(10,2)   N'$.quantity',
        unit_price        DECIMAL(10,2)   N'$.unit_price',
        tax_type_id       INT             N'$.tax_type_id',
        tax_rate_type     TINYINT         N'$.tax_rate_type',
        applied_tax_rate  DECIMAL(6,4)    N'$.applied_tax_rate',
        line_tax_amount   DECIMAL(10,2)   N'$.line_tax_amount',
        slip_tax_amount   DECIMAL(10,2)   N'$.slip_tax_amount',
        line_remarks      NVARCHAR(200)   N'$.line_remarks'
    ) j;

    -- 請求集計済みチェック（更新時のみ）
    IF EXISTS (
        SELECT 1 FROM sales
        WHERE sale_no = @sale_no
          AND is_deleted = 0
          AND invoiced_date IS NOT NULL
    )
    BEGIN
        RAISERROR(N'請求集計済みの伝票は変更できません。', 16, 1);
        RETURN;
    END

    -- 得意先の存在・削除チェック
    DECLARE @customer_code    NVARCHAR(20);
    DECLARE @customer_name    NVARCHAR(100);
    DECLARE @tax_calc_unit_id INT;
    DECLARE @tax_fraction_id  INT;

    SELECT
        @customer_code    = customer_code,
        @customer_name    = customer_name,
        @tax_calc_unit_id = tax_calc_unit_id,
        @tax_fraction_id  = tax_fraction_id
    FROM customers
    WHERE customer_id = @customer_id AND is_deleted = 0;

    IF @customer_code IS NULL
    BEGIN
        RAISERROR(N'得意先が存在しないか削除済みです。', 16, 1);
        RETURN;
    END

    -- 請求集計済み期間チェック
    IF EXISTS (
        SELECT 1 FROM invoice_headers
        WHERE customer_id = @customer_id
          AND invoice_date >= @sale_date
          AND is_deleted = 0
    )
    BEGIN
        DECLARE @sale_date_str NVARCHAR(10) = CONVERT(NVARCHAR(10), @sale_date, 23);
        RAISERROR(N'日付 %s は請求集計済みの期間内のため登録できません。', 16, 1, @sale_date_str);
        RETURN;
    END

    -- 担当社員の存在・削除チェック
    DECLARE @employee_code NVARCHAR(20);
    DECLARE @employee_name NVARCHAR(50);

    SELECT
        @employee_code = employee_code,
        @employee_name = employee_name
    FROM employees
    WHERE employee_id = @employee_id AND is_deleted = 0;

    IF @employee_code IS NULL
    BEGIN
        RAISERROR(N'担当社員が存在しないか削除済みです。', 16, 1);
        RETURN;
    END

    -- 参照受注の存在チェック（指定時のみ）
    IF @order_id IS NOT NULL AND NOT EXISTS (
        SELECT 1 FROM orders
        WHERE order_id = @order_id AND is_deleted = 0
    )
    BEGIN
        RAISERROR(N'参照受注が存在しないか削除済みです。', 16, 1);
        RETURN;
    END

    -- 明細行の存在チェック
    IF NOT EXISTS (SELECT 1 FROM #lines)
    BEGIN
        RAISERROR(N'明細行を1件以上入力してください。', 16, 1);
        RETURN;
    END

    -- 商品の存在・削除チェック
    IF EXISTS (
        SELECT 1 FROM #lines l
        WHERE NOT EXISTS (
            SELECT 1 FROM products p
            WHERE p.product_id = l.product_id AND p.is_deleted = 0
        )
    )
    BEGIN
        RAISERROR(N'存在しないか削除済みの商品が含まれています。', 16, 1);
        RETURN;
    END

    -- 差し替え（論理削除 → INSERT）
    BEGIN TRANSACTION;

    UPDATE sales SET is_deleted = 1 WHERE sale_no = @sale_no AND is_deleted = 0;

    INSERT INTO sales (
        sale_no, sale_date,
        customer_id, customer_code, customer_name,
        order_id, order_no,
        employee_id, employee_code, employee_name,
        line_no,
        product_id, product_code, product_name,
        quantity, unit_price,
        tax_type_id, tax_rate_type, applied_tax_rate,
        tax_calc_unit_id, tax_fraction_id,
        line_tax_amount, slip_tax_amount, invoiced_date,
        slip_remarks, line_remarks
    )
    SELECT
        @sale_no, @sale_date,
        @customer_id, @customer_code, @customer_name,
        @order_id, @order_no,
        @employee_id, @employee_code, @employee_name,
        l.line_no,
        l.product_id, l.product_code, l.product_name,
        l.quantity, l.unit_price,
        l.tax_type_id, l.tax_rate_type, l.applied_tax_rate,
        @tax_calc_unit_id, @tax_fraction_id,
        l.line_tax_amount, l.slip_tax_amount, NULL,
        @slip_remarks, l.line_remarks
    FROM #lines l;

    COMMIT TRANSACTION;
    DROP TABLE #lines;
END;
GO

-- ----- usp_tax_calc_unit_classifications_delete -----
-- ============================================================
-- usp_tax_calc_unit_classifications_delete
-- ============================================================
CREATE   PROCEDURE dbo.usp_tax_calc_unit_classifications_delete
    @tax_calc_unit_id INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM tax_calc_unit_classifications WHERE tax_calc_unit_id = @tax_calc_unit_id AND is_deleted = 0)
    BEGIN
        RAISERROR(N'計算単位ID %d が存在しないか削除済みです。', 16, 1, @tax_calc_unit_id);
        RETURN;
    END
    IF EXISTS (SELECT 1 FROM customers WHERE tax_calc_unit_id = @tax_calc_unit_id AND is_deleted = 0)
    BEGIN
        RAISERROR(N'得意先マスタで使用されているため削除できません。', 16, 1);
        RETURN;
    END
    UPDATE tax_calc_unit_classifications SET is_deleted = 1 WHERE tax_calc_unit_id = @tax_calc_unit_id AND is_deleted = 0;
END
GO

-- ----- usp_tax_calc_unit_classifications_upsert -----
-- ============================================================
-- usp_tax_calc_unit_classifications_upsert
-- ============================================================
CREATE   PROCEDURE dbo.usp_tax_calc_unit_classifications_upsert
    @tax_calc_unit_id   INT           = NULL,
    @tax_calc_unit_code NVARCHAR(20),
    @tax_calc_unit_name NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF EXISTS (
        SELECT 1 FROM tax_calc_unit_classifications
        WHERE tax_calc_unit_code = @tax_calc_unit_code
          AND is_deleted = 0
          AND (@tax_calc_unit_id IS NULL OR tax_calc_unit_id <> @tax_calc_unit_id)
    )
    BEGIN
        RAISERROR(N'計算単位コード %s は既に使用されています。', 16, 1, @tax_calc_unit_code);
        RETURN;
    END

    IF @tax_calc_unit_id IS NULL
    BEGIN
        INSERT INTO tax_calc_unit_classifications (tax_calc_unit_code, tax_calc_unit_name, is_deleted)
        VALUES (@tax_calc_unit_code, @tax_calc_unit_name, 0);
    END
    ELSE
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM tax_calc_unit_classifications WHERE tax_calc_unit_id = @tax_calc_unit_id AND is_deleted = 0)
        BEGIN
            RAISERROR(N'計算単位ID %d が存在しないか削除済みです。', 16, 1, @tax_calc_unit_id);
            RETURN;
        END
        UPDATE tax_calc_unit_classifications
        SET tax_calc_unit_code = @tax_calc_unit_code,
            tax_calc_unit_name = @tax_calc_unit_name
        WHERE tax_calc_unit_id = @tax_calc_unit_id AND is_deleted = 0;
    END
END
GO

-- ----- usp_tax_fraction_classifications_delete -----
-- ============================================================
-- usp_tax_fraction_classifications_delete
-- ============================================================
CREATE   PROCEDURE dbo.usp_tax_fraction_classifications_delete
    @tax_fraction_id INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM tax_fraction_classifications WHERE tax_fraction_id = @tax_fraction_id AND is_deleted = 0)
    BEGIN
        RAISERROR(N'端数処理ID %d が存在しないか削除済みです。', 16, 1, @tax_fraction_id);
        RETURN;
    END
    IF EXISTS (SELECT 1 FROM customers WHERE tax_fraction_id = @tax_fraction_id AND is_deleted = 0)
    BEGIN
        RAISERROR(N'得意先マスタで使用されているため削除できません。', 16, 1);
        RETURN;
    END
    UPDATE tax_fraction_classifications SET is_deleted = 1 WHERE tax_fraction_id = @tax_fraction_id AND is_deleted = 0;
END
GO

-- ----- usp_tax_fraction_classifications_upsert -----
-- ============================================================
-- usp_tax_fraction_classifications_upsert
-- ============================================================
CREATE   PROCEDURE dbo.usp_tax_fraction_classifications_upsert
    @tax_fraction_id   INT           = NULL,
    @tax_fraction_code NVARCHAR(20),
    @tax_fraction_name NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF EXISTS (
        SELECT 1 FROM tax_fraction_classifications
        WHERE tax_fraction_code = @tax_fraction_code
          AND is_deleted = 0
          AND (@tax_fraction_id IS NULL OR tax_fraction_id <> @tax_fraction_id)
    )
    BEGIN
        RAISERROR(N'端数処理コード %s は既に使用されています。', 16, 1, @tax_fraction_code);
        RETURN;
    END

    IF @tax_fraction_id IS NULL
    BEGIN
        INSERT INTO tax_fraction_classifications (tax_fraction_code, tax_fraction_name, is_deleted)
        VALUES (@tax_fraction_code, @tax_fraction_name, 0);
    END
    ELSE
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM tax_fraction_classifications WHERE tax_fraction_id = @tax_fraction_id AND is_deleted = 0)
        BEGIN
            RAISERROR(N'端数処理ID %d が存在しないか削除済みです。', 16, 1, @tax_fraction_id);
            RETURN;
        END
        UPDATE tax_fraction_classifications
        SET tax_fraction_code = @tax_fraction_code,
            tax_fraction_name = @tax_fraction_name
        WHERE tax_fraction_id = @tax_fraction_id AND is_deleted = 0;
    END
END
GO

-- ----- usp_tax_rate_periods_delete -----
-- ============================================================
-- usp_tax_rate_periods_delete
-- ============================================================
CREATE   PROCEDURE dbo.usp_tax_rate_periods_delete
    @tax_rate_period_id INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM tax_rate_periods WHERE tax_rate_period_id = @tax_rate_period_id AND is_deleted = 0)
    BEGIN
        RAISERROR(N'税率期間ID %d が存在しないか削除済みです。', 16, 1, @tax_rate_period_id);
        RETURN;
    END
    UPDATE tax_rate_periods SET is_deleted = 1 WHERE tax_rate_period_id = @tax_rate_period_id AND is_deleted = 0;
END
GO

-- ----- usp_tax_rate_periods_upsert -----
-- ============================================================
-- usp_tax_rate_periods_upsert
-- ============================================================
CREATE   PROCEDURE dbo.usp_tax_rate_periods_upsert
    @tax_rate_period_id  INT            = NULL,
    @start_date          DATE,
    @end_date            DATE           = NULL,
    @primary_tax_rate    DECIMAL(5,4),
    @secondary_tax_rate  DECIMAL(5,4),
    @tertiary_tax_rate   DECIMAL(5,4)   = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- 開始日重複チェック（他レコード）
    IF EXISTS (
        SELECT 1 FROM tax_rate_periods
        WHERE start_date = @start_date
          AND is_deleted = 0
          AND (@tax_rate_period_id IS NULL OR tax_rate_period_id <> @tax_rate_period_id)
    )
    BEGIN
        DECLARE @start_date_str NVARCHAR(10) = CONVERT(NVARCHAR(10), @start_date, 23);
        RAISERROR(N'開始日 %s の税率期間は既に存在します。', 16, 1, @start_date_str);
        RETURN;
    END

    IF @tax_rate_period_id IS NULL
    BEGIN
        INSERT INTO tax_rate_periods (start_date, end_date, primary_tax_rate, secondary_tax_rate, tertiary_tax_rate, is_deleted)
        VALUES (@start_date, @end_date, @primary_tax_rate, @secondary_tax_rate, @tertiary_tax_rate, 0);
    END
    ELSE
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM tax_rate_periods WHERE tax_rate_period_id = @tax_rate_period_id AND is_deleted = 0)
        BEGIN
            RAISERROR(N'税率期間ID %d が存在しないか削除済みです。', 16, 1, @tax_rate_period_id);
            RETURN;
        END
        UPDATE tax_rate_periods
        SET start_date         = @start_date,
            end_date           = @end_date,
            primary_tax_rate   = @primary_tax_rate,
            secondary_tax_rate = @secondary_tax_rate,
            tertiary_tax_rate  = @tertiary_tax_rate
        WHERE tax_rate_period_id = @tax_rate_period_id AND is_deleted = 0;
    END
END
GO

-- ----- usp_tax_type_classifications_delete -----
-- ============================================================
-- usp_tax_type_classifications_delete
-- ============================================================
CREATE   PROCEDURE dbo.usp_tax_type_classifications_delete
    @tax_type_id INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM tax_type_classifications WHERE tax_type_id = @tax_type_id AND is_deleted = 0)
    BEGIN
        RAISERROR(N'課税種別ID %d が存在しないか削除済みです。', 16, 1, @tax_type_id);
        RETURN;
    END
    IF EXISTS (SELECT 1 FROM products WHERE tax_type_id = @tax_type_id AND is_deleted = 0)
    BEGIN
        RAISERROR(N'商品マスタで使用されているため削除できません。', 16, 1);
        RETURN;
    END
    UPDATE tax_type_classifications SET is_deleted = 1 WHERE tax_type_id = @tax_type_id AND is_deleted = 0;
END
GO

-- ----- usp_tax_type_classifications_upsert -----
-- ============================================================
-- usp_tax_type_classifications_upsert
-- ============================================================
CREATE   PROCEDURE dbo.usp_tax_type_classifications_upsert
    @tax_type_id   INT           = NULL,
    @tax_type_code NVARCHAR(20),
    @tax_type_name NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF EXISTS (
        SELECT 1 FROM tax_type_classifications
        WHERE tax_type_code = @tax_type_code
          AND is_deleted = 0
          AND (@tax_type_id IS NULL OR tax_type_id <> @tax_type_id)
    )
    BEGIN
        RAISERROR(N'課税種別コード %s は既に使用されています。', 16, 1, @tax_type_code);
        RETURN;
    END

    IF @tax_type_id IS NULL
    BEGIN
        INSERT INTO tax_type_classifications (tax_type_code, tax_type_name, is_deleted)
        VALUES (@tax_type_code, @tax_type_name, 0);
    END
    ELSE
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM tax_type_classifications WHERE tax_type_id = @tax_type_id AND is_deleted = 0)
        BEGIN
            RAISERROR(N'課税種別ID %d が存在しないか削除済みです。', 16, 1, @tax_type_id);
            RETURN;
        END
        UPDATE tax_type_classifications
        SET tax_type_code = @tax_type_code,
            tax_type_name = @tax_type_name
        WHERE tax_type_id = @tax_type_id AND is_deleted = 0;
    END
END
GO

