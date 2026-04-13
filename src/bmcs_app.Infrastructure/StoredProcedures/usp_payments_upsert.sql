CREATE OR ALTER PROCEDURE dbo.usp_payments_upsert
    @payment_no   NVARCHAR(20),
    @payment_date DATE,
    @supplier_id  INT,
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
        CAST(j.line_no           AS INT)           AS line_no,
        CAST(j.payment_method_id AS INT)           AS payment_method_id,
        CAST(j.amount            AS DECIMAL(12,2)) AS amount,
        CAST(j.line_remarks      AS NVARCHAR(200)) AS line_remarks,
        CAST(j.bill_due_date     AS DATE)          AS bill_due_date
    INTO #lines
    FROM OPENJSON(@lines) WITH (
        line_no           INT             N'$.line_no',
        payment_method_id INT             N'$.payment_method_id',
        amount            DECIMAL(12,2)   N'$.amount',
        line_remarks      NVARCHAR(200)   N'$.line_remarks',
        bill_due_date     DATE            N'$.bill_due_date'
    ) j;

    -- 集計済みチェック（更新時のみ）
    IF EXISTS (
        SELECT 1 FROM payments
        WHERE payment_no = @payment_no AND is_deleted = 0
          AND ap_closing_at IS NOT NULL
    )
    BEGIN
        RAISERROR(N'集計済みの伝票は変更できません。', 16, 1);
        RETURN;
    END

    -- 仕入先の存在・削除チェック
    DECLARE @supplier_code        NVARCHAR(20);
    DECLARE @supplier_name        NVARCHAR(100);
    DECLARE @supplier_postal_code NVARCHAR(8);
    DECLARE @supplier_address1    NVARCHAR(100);
    DECLARE @supplier_address2    NVARCHAR(100);

    SELECT
        @supplier_code        = supplier_code,
        @supplier_name        = supplier_name,
        @supplier_postal_code = postal_code,
        @supplier_address1    = address1,
        @supplier_address2    = address2
    FROM suppliers
    WHERE supplier_id = @supplier_id AND is_deleted = 0;

    IF @supplier_code IS NULL
    BEGIN
        RAISERROR(N'仕入先が存在しないか削除済みです。', 16, 1);
        RETURN;
    END

    IF NOT EXISTS (SELECT 1 FROM #lines)
    BEGIN
        RAISERROR(N'明細行を1行以上入力してください。', 16, 1);
        RETURN;
    END

    IF EXISTS (
        SELECT 1 FROM #lines l
        WHERE NOT EXISTS (
            SELECT 1 FROM payment_method_classifications pm
            WHERE pm.payment_method_id = l.payment_method_id AND pm.is_deleted = 0
        )
    )
    BEGIN
        RAISERROR(N'存在しないか削除済みの支払区分が含まれています。', 16, 1);
        RETURN;
    END

    IF EXISTS (SELECT 1 FROM #lines WHERE amount = 0)
    BEGIN
        RAISERROR(N'金額に0は設定できません。', 16, 1);
        RETURN;
    END

    BEGIN TRANSACTION;

    UPDATE payments SET is_deleted = 1 WHERE payment_no = @payment_no AND is_deleted = 0;

    INSERT INTO payments (
        payment_no, payment_date,
        supplier_id, supplier_code, supplier_name,
        supplier_postal_code, supplier_address1, supplier_address2,
        line_no,
        payment_method_id, amount, bill_due_date,
        ap_closing_at,
        slip_remarks, line_remarks
    )
    SELECT
        @payment_no, @payment_date,
        @supplier_id, @supplier_code, @supplier_name,
        @supplier_postal_code, @supplier_address1, @supplier_address2,
        l.line_no,
        l.payment_method_id, l.amount, l.bill_due_date,
        NULL,
        @slip_remarks, l.line_remarks
    FROM #lines l;

    COMMIT TRANSACTION;
    DROP TABLE #lines;
END;