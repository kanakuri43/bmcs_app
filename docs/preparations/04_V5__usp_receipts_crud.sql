-- =============================================================================
-- 入金 CRUD ストアドプロシージャ（JSON版）
-- SQL Server 2022
--
-- @lines JSON形式（配列）
-- [
--   {
--     "line_no"          : 1,
--     "payment_method_id": 2,
--     "amount"           : 80000.00,
--     "line_remarks"     : null
--   },
--   ...
-- ]
-- =============================================================================

-- =============================================================================
-- 登録 usp_receipts_insert
-- =============================================================================
CREATE OR ALTER PROCEDURE dbo.usp_receipts_insert
    @receipt_no   NVARCHAR(20),
    @receipt_date DATE,
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
        CAST(j.payment_method_id AS INT)            AS payment_method_id,
        CAST(j.amount            AS DECIMAL(12,2))  AS amount,
        CAST(j.line_remarks      AS NVARCHAR(200))  AS line_remarks
    INTO #lines
    FROM OPENJSON(@lines) WITH (
        line_no           INT             '$.line_no',
        payment_method_id INT             '$.payment_method_id',
        amount            DECIMAL(12,2)   '$.amount',
        line_remarks      NVARCHAR(200)   '$.line_remarks'
    ) j;

    -- --------------------------------------------------
    -- バリデーション
    -- --------------------------------------------------

    -- 伝票番号の重複チェック
    IF EXISTS (
        SELECT 1 FROM receipts
        WHERE receipt_no = @receipt_no AND is_deleted = 0
    )
    BEGIN
        RAISERROR(N'伝票番号 %s は既に存在します。', 16, 1, @receipt_no);
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
        RAISERROR(N'入金日 %s は請求集計済みの期間内のため登録できません。', 16, 1, @receipt_date_str);
        RETURN;
    END

    -- 明細行の存在チェック
    IF NOT EXISTS (SELECT 1 FROM #lines)
    BEGIN
        RAISERROR(N'明細行が1件もありません。', 16, 1);
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

    -- 金額の0チェック
    IF EXISTS (
        SELECT 1 FROM #lines WHERE amount = 0
    )
    BEGIN
        RAISERROR(N'金額に0は設定できません。', 16, 1);
        RETURN;
    END

    -- --------------------------------------------------
    -- INSERT
    -- --------------------------------------------------
    BEGIN TRANSACTION;

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

-- =============================================================================
-- 更新 usp_receipts_update
-- 伝票単位で全行を論理削除後、新しい行をINSERTする
-- =============================================================================
CREATE OR ALTER PROCEDURE dbo.usp_receipts_update
    @receipt_no   NVARCHAR(20),
    @receipt_date DATE,
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
        CAST(j.payment_method_id AS INT)            AS payment_method_id,
        CAST(j.amount            AS DECIMAL(12,2))  AS amount,
        CAST(j.line_remarks      AS NVARCHAR(200))  AS line_remarks
    INTO #lines
    FROM OPENJSON(@lines) WITH (
        line_no           INT             '$.line_no',
        payment_method_id INT             '$.payment_method_id',
        amount            DECIMAL(12,2)   '$.amount',
        line_remarks      NVARCHAR(200)   '$.line_remarks'
    ) j;

    -- --------------------------------------------------
    -- バリデーション
    -- --------------------------------------------------

    -- 伝票の存在チェック
    IF NOT EXISTS (
        SELECT 1 FROM receipts
        WHERE receipt_no = @receipt_no AND is_deleted = 0
    )
    BEGIN
        RAISERROR(N'伝票番号 %s が存在しません。', 16, 1, @receipt_no);
        RETURN;
    END

    -- 請求集計済みチェック（既存行）
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
        RAISERROR(N'入金日 %s は請求集計済みの期間内のため登録できません。', 16, 1, @receipt_date_str);
        RETURN;
    END

    -- 明細行の存在チェック
    IF NOT EXISTS (SELECT 1 FROM #lines)
    BEGIN
        RAISERROR(N'明細行が1件もありません。', 16, 1);
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

    -- 金額の0チェック
    IF EXISTS (
        SELECT 1 FROM #lines WHERE amount = 0
    )
    BEGIN
        RAISERROR(N'金額に0は設定できません。', 16, 1);
        RETURN;
    END

    -- --------------------------------------------------
    -- 差し替え（論理削除 → INSERT）
    -- --------------------------------------------------
    BEGIN TRANSACTION;

    UPDATE receipts
    SET is_deleted = 1
    WHERE receipt_no = @receipt_no AND is_deleted = 0;

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

-- =============================================================================
-- 削除 usp_receipts_delete
-- 伝票単位で全行を論理削除
-- =============================================================================
CREATE OR ALTER PROCEDURE dbo.usp_receipts_delete
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

-- =============================================================================
-- 照会 usp_receipts_select
-- 伝票番号 または 条件（得意先・期間）で検索
-- すべてNULLの場合は全件取得
-- =============================================================================
CREATE OR ALTER PROCEDURE dbo.usp_receipts_select
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
