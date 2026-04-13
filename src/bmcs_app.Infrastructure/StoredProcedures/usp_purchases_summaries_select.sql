CREATE OR ALTER PROCEDURE dbo.usp_purchases_summaries_select
AS
BEGIN
    SET NOCOUNT ON;
    SELECT purchase_no,
           MIN(purchase_date)  AS purchase_date,
           MAX(supplier_name)  AS supplier_name
    FROM   purchases
    WHERE  is_deleted = 0
    GROUP  BY purchase_no
    ORDER  BY purchase_no;
END;