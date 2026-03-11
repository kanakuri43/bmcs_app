-- =============================================================================
-- 売上 CRUD ストアドプロシージャ（JSON版）
-- SQL Server 2022
--
-- @lines JSON形式（配列）
-- [
--   {
--     "line_no"         : 1,
--     "product_id"      : 1,
--     "product_code"    : "P001",
--     "product_name"    : "コーヒー豆 1kg",
--     "quantity"        : 10.00,
--     "unit_price"      : 2000.00,
--     "tax_type_id"     : 1,
--     "tax_category_id" : 2,
--     "tax_amount"      : null,
--     "slip_tax_amount" : 5500.00,
--     "line_remarks"    : null
--   },
--   ...
-- ]
-- =============================================================================

-- =============================================================================
-- 登録 usp_sales_insert
-- =============================================================================
CREATE OR ALTER PROCEDURE dbo.usp_sales_insert
    @sale_no      NVARCHAR(20),
    @sale_date    DATE,
    @customer_id  INT,
    @order_id     INT           = NULL,
    @order_no     NVARCHAR(20)  = NULL,
    @employee_id  INT,
    @slip_remarks NVARCHAR(200) = NULL,
    @lines        NVARCHAR(MAX)             -- JSON配列
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- --------------------------------------------------
    -- JSONパース
    -- --------------------------------------------------
    IF ISJSON(@lines) = 0
    BEGIN
        RAISERROR(N'@lines が有効なJSON形式ではありません。', 16, 1);
        RETURN;
    END

    -- 一時テーブルに展開
    SELECT
        CAST(j.line_no           AS INT)            AS line_no,
        CAST(j.product_id        AS INT)            AS product_id,
        CAST(j.product_code      AS NVARCHAR(20))   AS product_code,
        CAST(j.product_name      AS NVARCHAR(100))  AS product_name,
        CAST(j.quantity          AS DECIMAL(10,2))  AS quantity,
        CAST(j.unit_price        AS DECIMAL(10,2))  AS unit_price,
        CAST(j.tax_type_id       AS INT)            AS tax_type_id,
        CAST(j.tax_category_id   AS INT)            AS tax_category_id,
        CAST(j.tax_amount        AS DECIMAL(10,2))  AS tax_amount,
        CAST(j.slip_tax_amount   AS DECIMAL(10,2))  AS slip_tax_amount,
        CAST(j.line_remarks      AS NVARCHAR(200))  AS line_remarks
    INTO #lines
    FROM OPENJSON(@lines) WITH (
        line_no           INT             '$.line_no',
        product_id        INT             '$.product_id',
        product_code      NVARCHAR(20)    '$.product_code',
        product_name      NVARCHAR(100)   '$.product_name',
        quantity          DECIMAL(10,2)   '$.quantity',
        unit_price        DECIMAL(10,2)   '$.unit_price',
        tax_type_id       INT             '$.tax_type_id',
        tax_category_id   INT             '$.tax_category_id',
        tax_amount        DECIMAL(10,2)   '$.tax_amount',
        slip_tax_amount   DECIMAL(10,2)   '$.slip_tax_amount',
        line_remarks      NVARCHAR(200)   '$.line_remarks'
    ) j;

    -- --------------------------------------------------
    -- バリデーション
    -- --------------------------------------------------

    -- 伝票番号の重複チェック
    IF EXISTS (
        SELECT 1 FROM sales
        WHERE sale_no = @sale_no AND is_deleted = 0
    )
    BEGIN
        RAISERROR(N'伝票番号 %s は既に存在します。', 16, 1, @sale_no);
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
        RAISERROR(N'売上日 %s は請求集計済みの期間内のため登録できません。', 16, 1, @sale_date_str);
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
        RAISERROR(N'明細行が1件もありません。', 16, 1);
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

    -- --------------------------------------------------
    -- INSERT
    -- --------------------------------------------------
    BEGIN TRANSACTION;

    INSERT INTO sales (
        sale_no, sale_date,
        customer_id, customer_code, customer_name,
        order_id, order_no,
        employee_id, employee_code, employee_name,
        line_no,
        product_id, product_code, product_name,
        quantity, unit_price,
        tax_type_id, tax_category_id, tax_calc_unit_id, tax_fraction_id,
        tax_amount, slip_tax_amount, invoiced_date,
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
        l.tax_type_id, l.tax_category_id, @tax_calc_unit_id, @tax_fraction_id,
        l.tax_amount, l.slip_tax_amount, NULL,
        @slip_remarks, l.line_remarks
    FROM #lines l;

    COMMIT TRANSACTION;

    DROP TABLE #lines;
END;
GO

-- =============================================================================
-- 更新 usp_sales_update
-- 伝票単位で全行を論理削除後、新しい行をINSERTする
-- =============================================================================
CREATE OR ALTER PROCEDURE dbo.usp_sales_update
    @sale_no      NVARCHAR(20),
    @sale_date    DATE,
    @customer_id  INT,
    @order_id     INT           = NULL,
    @order_no     NVARCHAR(20)  = NULL,
    @employee_id  INT,
    @slip_remarks NVARCHAR(200) = NULL,
    @lines        NVARCHAR(MAX)             -- JSON配列
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- --------------------------------------------------
    -- JSONパース
    -- --------------------------------------------------
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
        CAST(j.tax_category_id   AS INT)            AS tax_category_id,
        CAST(j.tax_amount        AS DECIMAL(10,2))  AS tax_amount,
        CAST(j.slip_tax_amount   AS DECIMAL(10,2))  AS slip_tax_amount,
        CAST(j.line_remarks      AS NVARCHAR(200))  AS line_remarks
    INTO #lines
    FROM OPENJSON(@lines) WITH (
        line_no           INT             '$.line_no',
        product_id        INT             '$.product_id',
        product_code      NVARCHAR(20)    '$.product_code',
        product_name      NVARCHAR(100)   '$.product_name',
        quantity          DECIMAL(10,2)   '$.quantity',
        unit_price        DECIMAL(10,2)   '$.unit_price',
        tax_type_id       INT             '$.tax_type_id',
        tax_category_id   INT             '$.tax_category_id',
        tax_amount        DECIMAL(10,2)   '$.tax_amount',
        slip_tax_amount   DECIMAL(10,2)   '$.slip_tax_amount',
        line_remarks      NVARCHAR(200)   '$.line_remarks'
    ) j;

    -- --------------------------------------------------
    -- バリデーション
    -- --------------------------------------------------

    -- 伝票の存在チェック
    IF NOT EXISTS (
        SELECT 1 FROM sales
        WHERE sale_no = @sale_no AND is_deleted = 0
    )
    BEGIN
        RAISERROR(N'伝票番号 %s が存在しません。', 16, 1, @sale_no);
        RETURN;
    END

    -- 請求集計済みチェック
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
        RAISERROR(N'売上日 %s は請求集計済みの期間内のため登録できません。', 16, 1, @sale_date_str);
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
        RAISERROR(N'明細行が1件もありません。', 16, 1);
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

    -- --------------------------------------------------
    -- 差し替え（論理削除 → INSERT）
    -- --------------------------------------------------
    BEGIN TRANSACTION;

    UPDATE sales
    SET is_deleted = 1
    WHERE sale_no = @sale_no AND is_deleted = 0;

    INSERT INTO sales (
        sale_no, sale_date,
        customer_id, customer_code, customer_name,
        order_id, order_no,
        employee_id, employee_code, employee_name,
        line_no,
        product_id, product_code, product_name,
        quantity, unit_price,
        tax_type_id, tax_category_id, tax_calc_unit_id, tax_fraction_id,
        tax_amount, slip_tax_amount, invoiced_date,
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
        l.tax_type_id, l.tax_category_id, @tax_calc_unit_id, @tax_fraction_id,
        l.tax_amount, l.slip_tax_amount, NULL,
        @slip_remarks, l.line_remarks
    FROM #lines l;

    COMMIT TRANSACTION;

    DROP TABLE #lines;
END;
GO

-- =============================================================================
-- 削除 usp_sales_delete
-- 伝票単位で全行を論理削除
-- =============================================================================
CREATE OR ALTER PROCEDURE dbo.usp_sales_delete
    @sale_no NVARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- 伝票の存在チェック
    IF NOT EXISTS (
        SELECT 1 FROM sales
        WHERE sale_no = @sale_no AND is_deleted = 0
    )
    BEGIN
        RAISERROR(N'伝票番号 %s が存在しません。', 16, 1, @sale_no);
        RETURN;
    END

    -- 請求集計済みチェック
    IF EXISTS (
        SELECT 1 FROM sales
        WHERE sale_no = @sale_no
          AND is_deleted = 0
          AND invoiced_date IS NOT NULL
    )
    BEGIN
        RAISERROR(N'請求集計済みの伝票は削除できません。', 16, 1);
        RETURN;
    END

    BEGIN TRANSACTION;

    UPDATE sales
    SET is_deleted = 1
    WHERE sale_no = @sale_no AND is_deleted = 0;

    COMMIT TRANSACTION;
END;
GO

-- =============================================================================
-- 照会 usp_sales_select
-- 伝票番号 または 条件（得意先・期間）で検索
-- すべてNULLの場合は全件取得
-- =============================================================================
CREATE OR ALTER PROCEDURE dbo.usp_sales_select
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
        s.tax_category_id,
        tc.tax_category_name,
        s.tax_calc_unit_id,
        tu.tax_calc_unit_name,
        s.tax_fraction_id,
        tf.tax_fraction_name,
        s.tax_amount,
        s.slip_tax_amount,
        s.invoiced_date,
        s.slip_remarks,
        s.line_remarks,
        s.row_version
    FROM sales s
    INNER JOIN tax_type_classifications      tt ON tt.tax_type_id      = s.tax_type_id
    INNER JOIN tax_category_classifications  tc ON tc.tax_category_id  = s.tax_category_id
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
