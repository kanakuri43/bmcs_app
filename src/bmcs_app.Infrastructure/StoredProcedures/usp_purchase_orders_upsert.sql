CREATE OR ALTER PROCEDURE dbo.usp_purchase_orders_upsert
    @purchase_order_no   NVARCHAR(20),
    @purchase_order_date DATE,
    @supplier_id         INT,
    @employee_id         INT,
    @slip_remarks        NVARCHAR(200) = NULL,
    @lines               NVARCHAR(MAX)
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
        CAST(j.line_no          AS INT)            AS line_no,
        CAST(j.product_id       AS INT)            AS product_id,
        CAST(j.product_code     AS NVARCHAR(20))   AS product_code,
        CAST(j.product_name     AS NVARCHAR(100))  AS product_name,
        CAST(j.quantity         AS DECIMAL(10,2))  AS quantity,
        CAST(j.unit_price       AS DECIMAL(10,2))  AS unit_price,
        CAST(j.cost_price       AS DECIMAL(18,2))  AS cost_price,
        CAST(j.tax_type_id      AS INT)            AS tax_type_id,
        CAST(j.tax_rate_type    AS TINYINT)        AS tax_rate_type,
        CAST(j.applied_tax_rate AS DECIMAL(6,4))   AS applied_tax_rate,
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
        cost_price       DECIMAL(18,2)   N'$.cost_price',
        tax_type_id      INT             N'$.tax_type_id',
        tax_rate_type    TINYINT         N'$.tax_rate_type',
        applied_tax_rate DECIMAL(6,4)    N'$.applied_tax_rate',
        tax_amount       DECIMAL(10,2)   N'$.tax_amount',
        slip_tax_amount  DECIMAL(10,2)   N'$.slip_tax_amount',
        line_remarks     NVARCHAR(200)   N'$.line_remarks'
    ) j;

    -- 仕入済みチェック（更新時のみ）
    IF EXISTS (SELECT 1 FROM purchase_orders WHERE purchase_order_no = @purchase_order_no AND is_deleted = 0)
       AND EXISTS (SELECT 1 FROM purchases WHERE purchase_order_no = @purchase_order_no AND is_deleted = 0)
    BEGIN
        RAISERROR(N'仕入登録済みの発注は変更できません。', 16, 1);
        RETURN;
    END

    -- 仕入先の存在・削除チェック
    DECLARE @supplier_code    NVARCHAR(20);
    DECLARE @supplier_name    NVARCHAR(100);
    DECLARE @tax_calc_unit_id INT;
    DECLARE @tax_fraction_id  INT;

    SELECT
        @supplier_code    = supplier_code,
        @supplier_name    = supplier_name,
        @tax_calc_unit_id = tax_calc_unit_id,
        @tax_fraction_id  = tax_fraction_id
    FROM suppliers
    WHERE supplier_id = @supplier_id AND is_deleted = 0;

    IF @supplier_code IS NULL
    BEGIN
        RAISERROR(N'仕入先が存在しないか削除済みです。', 16, 1);
        RETURN;
    END

    -- 担当社員の存在・削除チェック
    DECLARE @employee_code NVARCHAR(20);
    DECLARE @employee_name NVARCHAR(100);

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

    IF NOT EXISTS (SELECT 1 FROM #lines)
    BEGIN
        RAISERROR(N'明細行を1行以上入力してください。', 16, 1);
        RETURN;
    END

    IF EXISTS (
        SELECT 1 FROM #lines l
        WHERE NOT EXISTS (SELECT 1 FROM products p WHERE p.product_id = l.product_id AND p.is_deleted = 0)
    )
    BEGIN
        RAISERROR(N'存在しないか削除済みの商品が含まれています。', 16, 1);
        RETURN;
    END

    -- 論理削除 → 再INSERT
    BEGIN TRANSACTION;

    UPDATE purchase_orders SET is_deleted = 1 WHERE purchase_order_no = @purchase_order_no AND is_deleted = 0;

    INSERT INTO purchase_orders (
        purchase_order_no, purchase_order_date,
        supplier_id, supplier_code, supplier_name,
        employee_id, employee_code, employee_name,
        line_no,
        product_id, product_code, product_name,
        quantity, unit_price, cost_price,
        tax_type_id, tax_rate_type, tax_calc_unit_id, tax_fraction_id,
        applied_tax_rate, tax_amount, slip_tax_amount,
        slip_remarks, line_remarks
    )
    SELECT
        @purchase_order_no, @purchase_order_date,
        @supplier_id, @supplier_code, @supplier_name,
        @employee_id, @employee_code, @employee_name,
        l.line_no,
        l.product_id, l.product_code, l.product_name,
        l.quantity, l.unit_price, ISNULL(l.cost_price, 0),
        l.tax_type_id, l.tax_rate_type, @tax_calc_unit_id, @tax_fraction_id,
        l.applied_tax_rate, l.tax_amount, l.slip_tax_amount,
        @slip_remarks, l.line_remarks
    FROM #lines l;

    COMMIT TRANSACTION;
    DROP TABLE #lines;
END;