-- ============================================================
-- invoice_headers に得意先住所を追加（請求書再発行時の住所固定）
-- 集計実行時点の住所を記録し、マスタ変更後の再発行でも不変にする
-- ============================================================

-- 1. invoice_headers テーブルに住所列追加（冪等）
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'invoice_headers' AND COLUMN_NAME = 'customer_postal_code')
    ALTER TABLE dbo.invoice_headers ADD customer_postal_code NVARCHAR(8)   NULL;
GO
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'invoice_headers' AND COLUMN_NAME = 'customer_address1')
    ALTER TABLE dbo.invoice_headers ADD customer_address1    NVARCHAR(100) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'invoice_headers' AND COLUMN_NAME = 'customer_address2')
    ALTER TABLE dbo.invoice_headers ADD customer_address2    NVARCHAR(100) NULL;
GO

-- 2. usp_invoice_closing 更新（affected CTE で住所を取得し INSERT に含める）
ALTER PROCEDURE dbo.usp_invoice_closing
    @closing_day  tinyint,
    @process_date date,
    @customer_id  int = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @max_day      int  = DAY(EOMONTH(@process_date));
    DECLARE @closing_date date = CASE
        WHEN @closing_day IN (0, 99)
            THEN EOMONTH(@process_date)
        ELSE DATEFROMPARTS(
            YEAR(@process_date),
            MONTH(@process_date),
            CASE WHEN @closing_day > @max_day THEN @max_day ELSE @closing_day END
        )
    END;

    BEGIN TRANSACTION;
    BEGIN TRY

        -- Step 1: Set invoiced_at on sales
        UPDATE sales
        SET    invoiced_at = @process_date
        WHERE  is_deleted   = 0
          AND  invoiced_at IS NULL
          AND  sale_date   <= @closing_date
          AND  (@customer_id IS NULL OR customer_id = @customer_id)
          AND  customer_id IN (
                   SELECT customer_id FROM customers
                   WHERE  closing_day = @closing_day AND is_deleted = 0
               );

        -- Step 2: Remove existing invoice_headers for re-run
        DELETE FROM invoice_headers
        WHERE  invoice_date = @process_date
          AND  is_deleted   = 0
          AND  customer_id IN (
                   SELECT DISTINCT customer_id FROM sales
                   WHERE  is_deleted  = 0
                     AND  invoiced_at = @process_date
                     AND  (@customer_id IS NULL OR customer_id = @customer_id)
               );

        -- Step 3: Set invoiced_at on receipts
        UPDATE r
        SET    r.invoiced_at = @process_date
        FROM   receipts r
        WHERE  r.is_deleted    = 0
          AND  r.invoiced_at  IS NULL
          AND  r.receipt_date <= @closing_date
          AND  r.receipt_date  > ISNULL((
                   SELECT TOP 1 h.invoice_date
                   FROM   invoice_headers h
                   WHERE  h.customer_id = r.customer_id AND h.is_deleted = 0
                   ORDER BY h.invoice_date DESC
               ), '19000101')
          AND  r.customer_id IN (
                   SELECT DISTINCT customer_id FROM sales
                   WHERE  is_deleted   = 0
                     AND  invoiced_at  = @process_date
                     AND  (@customer_id IS NULL OR customer_id = @customer_id)
               )
          AND  (@customer_id IS NULL OR r.customer_id = @customer_id);

        -- Step 4: Insert invoice_headers via CTE chain
        ;WITH
        -- Customers included in this closing run (join customers for current code/name/address)
        affected AS (
            SELECT DISTINCT
                s.customer_id,
                c.customer_code,
                c.customer_name,
                c.postal_code  AS customer_postal_code,
                c.address1     AS customer_address1,
                c.address2     AS customer_address2
            FROM   sales s
            JOIN   customers c ON s.customer_id = c.customer_id AND c.is_deleted = 0
            WHERE  s.is_deleted  = 0
              AND  s.invoiced_at = @process_date
              AND  (@customer_id IS NULL OR s.customer_id = @customer_id)
        ),
        -- Most recent previous invoice_headers record per customer
        prev_inv AS (
            SELECT customer_id, current_invoice_amount, invoice_date
            FROM (
                SELECT customer_id, current_invoice_amount, invoice_date,
                       ROW_NUMBER() OVER (PARTITION BY customer_id ORDER BY invoice_date DESC) AS rn
                FROM   invoice_headers
                WHERE  is_deleted = 0
            ) t
            WHERE rn = 1
        ),
        -- Per-slip tax group aggregation
        slip_groups AS (
            SELECT
                s.customer_id,
                s.sale_no,
                s.tax_rate_type,
                s.tax_type_id,
                s.applied_tax_rate,
                SUM(s.quantity * s.unit_price) AS group_base
            FROM   sales s
            WHERE  s.is_deleted  = 0
              AND  s.invoiced_at = @process_date
              AND  (@customer_id IS NULL OR s.customer_id = @customer_id)
            GROUP BY s.customer_id, s.sale_no, s.tax_rate_type, s.tax_type_id, s.applied_tax_rate
        ),
        -- Per-customer sales and tax totals by rate type
        sales_agg AS (
            SELECT
                customer_id,
                SUM(CASE WHEN tax_rate_type = 1 THEN group_base ELSE 0 END) AS sales_standard,
                SUM(CASE WHEN tax_rate_type = 2 THEN group_base ELSE 0 END) AS sales_reduced,
                SUM(CASE WHEN tax_rate_type = 1 THEN
                    CASE WHEN tax_type_id = 2
                        THEN FLOOR(group_base * applied_tax_rate / (1 + applied_tax_rate))
                        ELSE FLOOR(group_base * applied_tax_rate)
                    END ELSE 0 END) AS tax_standard,
                SUM(CASE WHEN tax_rate_type = 2 THEN
                    CASE WHEN tax_type_id = 2
                        THEN FLOOR(group_base * applied_tax_rate / (1 + applied_tax_rate))
                        ELSE FLOOR(group_base * applied_tax_rate)
                    END ELSE 0 END) AS tax_reduced
            FROM   slip_groups
            GROUP BY customer_id
        ),
        -- Receipts in this closing period (invoiced_at = @process_date)
        receipt_agg AS (
            SELECT customer_id, SUM(amount) AS receipt_total
            FROM   receipts
            WHERE  is_deleted    = 0
              AND  CAST(invoiced_at AS date) = @process_date
              AND  (@customer_id IS NULL OR customer_id = @customer_id)
            GROUP BY customer_id
        )
        INSERT INTO invoice_headers (
            customer_id, customer_code, customer_name,
            customer_postal_code, customer_address1, customer_address2,
            invoice_date,
            previous_invoice_amount,
            receipt_amount,
            sales_amount_standard, sales_amount_reduced,
            tax_amount_standard,   tax_amount_reduced,
            current_invoice_amount,
            is_deleted
        )
        SELECT
            ac.customer_id,
            ac.customer_code,
            ac.customer_name,
            ac.customer_postal_code,
            ac.customer_address1,
            ac.customer_address2,
            @process_date,
            ISNULL(pi.current_invoice_amount, 0),
            ISNULL(ra.receipt_total, 0),
            ISNULL(sa.sales_standard, 0),
            ISNULL(sa.sales_reduced,  0),
            ISNULL(sa.tax_standard,   0),
            ISNULL(sa.tax_reduced,    0),
            ISNULL(pi.current_invoice_amount, 0)
            - ISNULL(ra.receipt_total,        0)
            + ISNULL(sa.sales_standard,       0) + ISNULL(sa.tax_standard, 0)
            + ISNULL(sa.sales_reduced,        0) + ISNULL(sa.tax_reduced,  0),
            0
        FROM   affected  ac
        LEFT JOIN prev_inv  pi ON ac.customer_id = pi.customer_id
        LEFT JOIN sales_agg sa ON ac.customer_id = sa.customer_id
        LEFT JOIN receipt_agg ra ON ac.customer_id = ra.customer_id;

        COMMIT TRANSACTION;

    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

-- 3. usp_invoice_headers_select 更新（住所列を SELECT に追加）
ALTER PROCEDURE dbo.usp_invoice_headers_select
    @invoice_date date,
    @closing_day  tinyint,
    @customer_id  int = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        ih.customer_id,
        ih.customer_name,
        ih.invoice_date,
        ih.previous_invoice_amount,
        ih.receipt_amount,
        ih.sales_amount_standard,
        ih.sales_amount_reduced,
        ih.tax_amount_standard,
        ih.tax_amount_reduced,
        ih.current_invoice_amount,
        ih.customer_postal_code,
        ih.customer_address1,
        ih.customer_address2
    FROM   invoice_headers ih
    JOIN   customers c ON ih.customer_id = c.customer_id AND c.is_deleted = 0
    WHERE  ih.invoice_date = @invoice_date
      AND  ih.is_deleted   = 0
      AND  c.closing_day   = @closing_day
      AND  (@customer_id IS NULL OR ih.customer_id = @customer_id)
    ORDER BY ih.customer_code;
END
GO
