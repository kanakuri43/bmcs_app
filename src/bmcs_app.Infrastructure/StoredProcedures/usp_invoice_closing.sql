CREATE OR ALTER PROCEDURE dbo.usp_invoice_closing
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

        -- Step 1: invoiced_at をセット
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

        -- Step 2: invoice_headers を作成
        --   再実行時は同一 customer_id + invoice_date の既存レコードを先に削除
        DELETE FROM invoice_headers
        WHERE  invoice_date = @process_date
          AND  is_deleted   = 0
          AND  customer_id IN (
                   SELECT DISTINCT customer_id FROM sales
                   WHERE  is_deleted  = 0
                     AND  invoiced_at = @process_date
                     AND  (@customer_id IS NULL OR customer_id = @customer_id)
               );

        ;WITH
        -- 今回 invoiced_at がセットされた得意先
        affected AS (
            SELECT DISTINCT customer_id, customer_code, customer_name
            FROM   sales
            WHERE  is_deleted  = 0
              AND  invoiced_at = @process_date
              AND  (@customer_id IS NULL OR customer_id = @customer_id)
        ),
        -- 直近の invoice_headers レコード（前残・前回請求日を取得）
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
        -- 伝票×税率グループ単位で集計（伝票単位・明細単位どちらも同じ計算になる）
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
        -- 得意先×税率種別で集計
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
        -- 入金集計: 前回請求日より後〜締め日までの入金（初回は全入金を対象）
        receipt_agg AS (
            SELECT r.customer_id, SUM(r.amount) AS receipt_total
            FROM   receipts r
            LEFT JOIN prev_inv pi ON r.customer_id = pi.customer_id
            WHERE  r.is_deleted    = 0
              AND  r.receipt_date  > ISNULL(pi.invoice_date, '19000101')
              AND  r.receipt_date <= @closing_date
              AND  (@customer_id IS NULL OR r.customer_id = @customer_id)
            GROUP BY r.customer_id
        )
        INSERT INTO invoice_headers (
            customer_id, customer_code, customer_name,
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
            @process_date,
            ISNULL(pi.current_invoice_amount, 0),
            ISNULL(ra.receipt_total, 0),
            ISNULL(sa.sales_standard, 0),
            ISNULL(sa.sales_reduced,  0),
            ISNULL(sa.tax_standard,   0),
            ISNULL(sa.tax_reduced,    0),
            -- 今回請求額 = 前残 - 入金 + 税抜売上 + 消費税
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
