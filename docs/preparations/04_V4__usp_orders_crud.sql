-- =============================================================================
-- 受注 CRUD ストアドプロシージャ（JSON版）
-- SQL Server 2022
--
-- @lines JSON形式（配列）
-- [
--   {
--     "line_no"          : 1,
--     "product_id"       : 1,
--     "product_code"     : "P001",
--     "product_name"     : "コーヒー豆 1kg",
--     "quantity"         : 10.00,
--     "unit_price"       : 2000.00,
--     "tax_type_id"      : 1,
--     "tax_category_id"  : 2,
--     "tax_amount"       : null,
--     "slip_tax_amount"  : 5500.00,
--     "line_remarks"     : null
--   },
--   ...
-- ]
-- =============================================================================

-- =============================================================================
-- 登録 usp_orders_insert
-- =============================================================================
CREATE OR ALTER PROCEDURE dbo.usp_orders_insert
    @order_no     NVARCHAR(20),
    @order_date   DATE,
    @customer_id  INT,
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

    -- 伝票番号の重複チェック
    IF EXISTS (
        SELECT 1 FROM orders
        WHERE order_no = @order_no AND is_deleted = 0
    )
    BEGIN
        RAISERROR(N'伝票番号 %s は既に存在します。', 16, 1, @order_no);
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

    INSERT INTO orders (
        order_no, order_date,
        customer_id, customer_code, customer_name,
        line_no,
        product_id, product_code, product_name,
        quantity, unit_price,
        tax_type_id, tax_category_id, tax_calc_unit_id, tax_fraction_id,
        tax_amount, slip_tax_amount,
        slip_remarks, line_remarks
    )
    SELECT
        @order_no, @order_date,
        @customer_id, @customer_code, @customer_name,
        l.line_no,
        l.product_id, l.product_code, l.product_name,
        l.quantity, l.unit_price,
        l.tax_type_id, l.tax_category_id, @tax_calc_unit_id, @tax_fraction_id,
        l.tax_amount, l.slip_tax_amount,
        @slip_remarks, l.line_remarks
    FROM #lines l;

    COMMIT TRANSACTION;

    DROP TABLE #lines;
END;
GO

-- =============================================================================
-- 更新 usp_orders_update
-- 伝票単位で全行を論理削除後、新しい行をINSERTする
-- 売上登録済みの受注は変更不可
-- =============================================================================
CREATE OR ALTER PROCEDURE dbo.usp_orders_update
    @order_no     NVARCHAR(20),
    @order_date   DATE,
    @customer_id  INT,
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
        SELECT 1 FROM orders
        WHERE order_no = @order_no AND is_deleted = 0
    )
    BEGIN
        RAISERROR(N'伝票番号 %s が存在しません。', 16, 1, @order_no);
        RETURN;
    END

    -- 売上登録済みチェック（この受注を参照した売上が存在する場合は変更不可）
    IF EXISTS (
        SELECT 1 FROM sales s
        INNER JOIN orders o ON o.order_id = s.order_id
        WHERE o.order_no = @order_no
          AND o.is_deleted = 0
          AND s.is_deleted = 0
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

    UPDATE orders
    SET is_deleted = 1
    WHERE order_no = @order_no AND is_deleted = 0;

    INSERT INTO orders (
        order_no, order_date,
        customer_id, customer_code, customer_name,
        line_no,
        product_id, product_code, product_name,
        quantity, unit_price,
        tax_type_id, tax_category_id, tax_calc_unit_id, tax_fraction_id,
        tax_amount, slip_tax_amount,
        slip_remarks, line_remarks
    )
    SELECT
        @order_no, @order_date,
        @customer_id, @customer_code, @customer_name,
        l.line_no,
        l.product_id, l.product_code, l.product_name,
        l.quantity, l.unit_price,
        l.tax_type_id, l.tax_category_id, @tax_calc_unit_id, @tax_fraction_id,
        l.tax_amount, l.slip_tax_amount,
        @slip_remarks, l.line_remarks
    FROM #lines l;

    COMMIT TRANSACTION;

    DROP TABLE #lines;
END;
GO

-- =============================================================================
-- 削除 usp_orders_delete
-- 伝票単位で全行を論理削除
-- 売上登録済みの受注は削除不可
-- =============================================================================
CREATE OR ALTER PROCEDURE dbo.usp_orders_delete
    @order_no NVARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- 伝票の存在チェック
    IF NOT EXISTS (
        SELECT 1 FROM orders
        WHERE order_no = @order_no AND is_deleted = 0
    )
    BEGIN
        RAISERROR(N'伝票番号 %s が存在しません。', 16, 1, @order_no);
        RETURN;
    END

    -- 売上登録済みチェック
    IF EXISTS (
        SELECT 1 FROM sales s
        INNER JOIN orders o ON o.order_id = s.order_id
        WHERE o.order_no = @order_no
          AND o.is_deleted = 0
          AND s.is_deleted = 0
    )
    BEGIN
        RAISERROR(N'売上登録済みの受注は削除できません。', 16, 1);
        RETURN;
    END

    BEGIN TRANSACTION;

    UPDATE orders
    SET is_deleted = 1
    WHERE order_no = @order_no AND is_deleted = 0;

    COMMIT TRANSACTION;
END;
GO

-- =============================================================================
-- 照会 usp_orders_select
-- 伝票番号 または 条件（得意先・期間）で検索
-- すべてNULLの場合は全件取得
-- =============================================================================
CREATE OR ALTER PROCEDURE dbo.usp_orders_select
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
        o.tax_category_id,
        o.tax_calc_unit_id,
        tu.tax_calc_unit_name,
        o.tax_fraction_id,
        tf.tax_fraction_name,
        o.tax_amount,
        o.slip_tax_amount,
        o.slip_remarks,
        o.line_remarks,
        CASE WHEN EXISTS (
            SELECT 1 FROM sales s
            WHERE s.order_id = o.order_id AND s.is_deleted = 0
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
