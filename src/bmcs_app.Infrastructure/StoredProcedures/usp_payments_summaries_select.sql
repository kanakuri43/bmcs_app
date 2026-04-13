CREATE OR ALTER PROCEDURE dbo.usp_payments_summaries_select
AS
BEGIN
    SET NOCOUNT ON;
    SELECT payment_no,
           MIN(payment_date)  AS payment_date,
           MAX(supplier_name) AS supplier_name
    FROM   payments
    WHERE  is_deleted = 0
    GROUP  BY payment_no
    ORDER  BY payment_no;
END;