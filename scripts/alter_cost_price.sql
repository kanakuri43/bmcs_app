-- ============================================================
-- 原価フィールド追加スクリプト
-- ============================================================

-- 1. products テーブルに cost_price 追加（冪等）
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'products' AND COLUMN_NAME = 'cost_price')
    ALTER TABLE products ADD cost_price DECIMAL(18,2) NOT NULL DEFAULT 0;
GO

-- 2. sales テーブルに cost_price 追加（冪等）
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'sales' AND COLUMN_NAME = 'cost_price')
    ALTER TABLE sales ADD cost_price DECIMAL(18,2) NOT NULL DEFAULT 0;
GO

-- 3. usp_products_upsert 更新
ALTER PROCEDURE usp_products_upsert
    @product_id    int = NULL,
    @product_code  nvarchar(20),
    @product_name  nvarchar(100),
    @tax_type_id   int,
    @tax_rate_type tinyint,
    @cost_price    decimal(18,2) = 0
AS
BEGIN
    SET NOCOUNT ON;
    IF @product_id IS NULL
    BEGIN
        INSERT INTO products (product_code, product_name, tax_type_id, tax_rate_type, cost_price, is_deleted)
        VALUES (@product_code, @product_name, @tax_type_id, @tax_rate_type, @cost_price, 0);
    END
    ELSE
    BEGIN
        UPDATE products
        SET    product_code  = @product_code,
               product_name  = @product_name,
               tax_type_id   = @tax_type_id,
               tax_rate_type = @tax_rate_type,
               cost_price    = @cost_price
        WHERE  product_id = @product_id AND is_deleted = 0;
    END
END
GO

-- 4. usp_sales_upsert 更新（cost_price を JSON から受け取り INSERT）
ALTER PROCEDURE dbo.usp_sales_upsert
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

    IF ISJSON(@lines) = 0
    BEGIN
        RAISERROR(N'@lines は有効なJSON形式ではありません。', 16, 1);
        RETURN;
    END

    SELECT
        CAST(j.line_no           AS INT)            AS line_no,
        CAST(j.product_id        AS INT)            AS product_id,
        CAST(j.product_code      AS NVARCHAR(20))   AS product_code,
        CAST(j.product_name      AS NVARCHAR(100))  AS product_name,
        CAST(j.quantity          AS DECIMAL(10,2))  AS quantity,
        CAST(j.unit_price        AS DECIMAL(10,2))  AS unit_price,
        CAST(j.cost_price        AS DECIMAL(18,2))  AS cost_price,
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
        cost_price        DECIMAL(18,2)   N'$.cost_price',
        tax_type_id       INT             N'$.tax_type_id',
        tax_rate_type     TINYINT         N'$.tax_rate_type',
        applied_tax_rate  DECIMAL(6,4)    N'$.applied_tax_rate',
        line_tax_amount   DECIMAL(10,2)   N'$.line_tax_amount',
        slip_tax_amount   DECIMAL(10,2)   N'$.slip_tax_amount',
        line_remarks      NVARCHAR(200)   N'$.line_remarks'
    ) j;

    -- 集計済みチェック（更新時のみ）
    IF EXISTS (
        SELECT 1 FROM sales
        WHERE sale_no = @sale_no AND is_deleted = 0
          AND (invoiced_at IS NOT NULL OR ar_aggregated_at IS NOT NULL)
    )
    BEGIN
        RAISERROR(N'集計済みの伝票は変更できません。', 16, 1);
        RETURN;
    END

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

    DECLARE @employee_code NVARCHAR(20);
    DECLARE @employee_name NVARCHAR(50);

    SELECT
        @employee_code = employee_code,
        @employee_name = employee_name
    FROM employees
    WHERE employee_id = @employee_id AND is_deleted = 0;

    IF @employee_code IS NULL
    BEGIN
        RAISERROR(N'担当者が存在しないか削除済みです。', 16, 1);
        RETURN;
    END

    IF @order_id IS NOT NULL AND NOT EXISTS (
        SELECT 1 FROM orders WHERE order_id = @order_id AND is_deleted = 0
    )
    BEGIN
        RAISERROR(N'参照受注が存在しないか削除済みです。', 16, 1);
        RETURN;
    END

    IF NOT EXISTS (SELECT 1 FROM #lines)
    BEGIN
        RAISERROR(N'明細行を1件以上入力してください。', 16, 1);
        RETURN;
    END

    IF EXISTS (
        SELECT 1 FROM #lines l
        WHERE NOT EXISTS (
            SELECT 1 FROM products p WHERE p.product_id = l.product_id AND p.is_deleted = 0
        )
    )
    BEGIN
        RAISERROR(N'存在しないか削除済みの商品が含まれています。', 16, 1);
        RETURN;
    END

    BEGIN TRANSACTION;

    UPDATE sales SET is_deleted = 1 WHERE sale_no = @sale_no AND is_deleted = 0;

    INSERT INTO sales (
        sale_no, sale_date,
        customer_id, customer_code, customer_name,
        order_id, order_no,
        employee_id, employee_code, employee_name,
        line_no,
        product_id, product_code, product_name,
        quantity, unit_price, cost_price,
        tax_type_id, tax_rate_type, applied_tax_rate,
        tax_calc_unit_id, tax_fraction_id,
        line_tax_amount, slip_tax_amount,
        invoiced_at, ar_aggregated_at,
        slip_remarks, line_remarks
    )
    SELECT
        @sale_no, @sale_date,
        @customer_id, @customer_code, @customer_name,
        @order_id, @order_no,
        @employee_id, @employee_code, @employee_name,
        l.line_no,
        l.product_id, l.product_code, l.product_name,
        l.quantity, l.unit_price, ISNULL(l.cost_price, 0),
        l.tax_type_id, l.tax_rate_type, l.applied_tax_rate,
        @tax_calc_unit_id, @tax_fraction_id,
        l.line_tax_amount, l.slip_tax_amount,
        NULL, NULL,
        @slip_remarks, l.line_remarks
    FROM #lines l;

    COMMIT TRANSACTION;
    DROP TABLE #lines;
END;
GO

-- 5. usp_sales_select 更新（cost_price を SELECT に追加）
ALTER PROCEDURE dbo.usp_sales_select
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
        s.cost_price,
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
        s.invoiced_at,
        s.ar_aggregated_at,
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
