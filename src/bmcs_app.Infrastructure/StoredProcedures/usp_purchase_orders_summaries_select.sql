CREATE OR ALTER PROCEDURE dbo.usp_purchase_orders_summaries_select
AS
BEGIN
    SET NOCOUNT ON;
    SELECT purchase_order_no,
           MIN(purchase_order_date) AS purchase_order_date,
           MAX(supplier_name)       AS supplier_name
    FROM   purchase_orders
    WHERE  is_deleted = 0
    GROUP  BY purchase_order_no
    ORDER  BY purchase_order_no;
END;