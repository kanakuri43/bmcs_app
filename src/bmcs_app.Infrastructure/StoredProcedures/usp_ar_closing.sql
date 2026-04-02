CREATE OR ALTER PROCEDURE dbo.usp_ar_closing
    @process_date date,
    @customer_id  int = NULL
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRANSACTION;
    BEGIN TRY

        -- Step 1: Set ar_aggregated_at on sales
        UPDATE sales
        SET    ar_aggregated_at = @process_date
        WHERE  is_deleted        = 0
          AND  ar_aggregated_at IS NULL
          AND  sale_date        <= @process_date
          AND  (@customer_id IS NULL OR customer_id = @customer_id);

        -- Step 2: Set ar_aggregated_at on receipts
        UPDATE receipts
        SET    ar_aggregated_at = @process_date
        WHERE  is_deleted        = 0
          AND  ar_aggregated_at IS NULL
          AND  receipt_date     <= @process_date
          AND  (@customer_id IS NULL OR customer_id = @customer_id);

        -- Step 3: Delete existing accounts_receivable_histories for re-run idempotency
        DELETE FROM accounts_receivable_histories
        WHERE  closing_date = @process_date
          AND  is_deleted   = 0
          AND  customer_id IN (
                   SELECT DISTINCT customer_id FROM sales
                   WHERE  is_deleted        = 0
                     AND  ar_aggregated_at  = @process_date
                     AND  (@customer_id IS NULL OR customer_id = @customer_id)
                   UNION
                   SELECT DISTINCT customer_id FROM receipts
                   WHERE  is_deleted        = 0
                     AND  ar_aggregated_at  = @process_date
                     AND  (@customer_id IS NULL OR customer_id = @customer_id)
               );

        ;WITH
        -- Customers affected by this closing (sales or receipts)
        affected AS (
            SELECT DISTINCT customer_id, customer_code, customer_name
            FROM   sales
            WHERE  is_deleted        = 0
              AND  ar_aggregated_at  = @process_date
              AND  (@customer_id IS NULL OR customer_id = @customer_id)
            UNION
            SELECT DISTINCT customer_id, customer_code, customer_name
            FROM   receipts
            WHERE  is_deleted        = 0
              AND  ar_aggregated_at  = @process_date
              AND  (@customer_id IS NULL OR customer_id = @customer_id)
        ),
        -- Latest previous accounts_receivable_histories per customer
        prev_ar AS (
            SELECT customer_id, closing_amount, closing_date
            FROM (
                SELECT customer_id, closing_amount, closing_date,
                       ROW_NUMBER() OVER (PARTITION BY customer_id ORDER BY closing_date DESC) AS rn
                FROM   accounts_receivable_histories
                WHERE  is_deleted = 0
            ) t
            WHERE rn = 1
        ),
        -- Per-slip tax grouping (handles both line-level and slip-level tax calc)
        slip_groups AS (
            SELECT
                s.customer_id,
                s.sale_no,
                s.tax_rate_type,
                s.tax_type_id,
                s.applied_tax_rate,
                SUM(s.quantity * s.unit_price) AS group_base
            FROM   sales s
            WHERE  s.is_deleted        = 0
              AND  s.ar_aggregated_at  = @process_date
              AND  (@customer_id IS NULL OR s.customer_id = @customer_id)
            GROUP BY s.customer_id, s.sale_no, s.tax_rate_type, s.tax_type_id, s.applied_tax_rate
        ),
        -- Aggregate by customer and tax rate type
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
        -- Receipt aggregate for this closing
        receipt_agg AS (
            SELECT customer_id, SUM(amount) AS receipt_total
            FROM   receipts
            WHERE  is_deleted        = 0
              AND  ar_aggregated_at  = @process_date
              AND  (@customer_id IS NULL OR customer_id = @customer_id)
            GROUP BY customer_id
        )
        INSERT INTO accounts_receivable_histories (
            customer_id, customer_code, customer_name,
            closing_date,
            carried_over_amount,
            sales_amount_standard, sales_amount_reduced,
            tax_amount_standard,   tax_amount_reduced,
            receipt_amount,
            closing_amount,
            is_deleted
        )
        SELECT
            ac.customer_id,
            ac.customer_code,
            ac.customer_name,
            @process_date,
            ISNULL(pa.closing_amount, 0),
            ISNULL(sa.sales_standard, 0),
            ISNULL(sa.sales_reduced,  0),
            ISNULL(sa.tax_standard,   0),
            ISNULL(sa.tax_reduced,    0),
            ISNULL(ra.receipt_total,  0),
            -- closing_amount = carried_over + sales + tax - receipt
            ISNULL(pa.closing_amount, 0)
            + ISNULL(sa.sales_standard, 0) + ISNULL(sa.tax_standard, 0)
            + ISNULL(sa.sales_reduced,  0) + ISNULL(sa.tax_reduced,  0)
            - ISNULL(ra.receipt_total,  0),
            0
        FROM   affected  ac
        LEFT JOIN prev_ar     pa ON ac.customer_id = pa.customer_id
        LEFT JOIN sales_agg   sa ON ac.customer_id = sa.customer_id
        LEFT JOIN receipt_agg ra ON ac.customer_id = ra.customer_id;

        COMMIT TRANSACTION;

    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
