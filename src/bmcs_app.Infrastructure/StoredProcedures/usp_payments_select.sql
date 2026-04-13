CREATE OR ALTER PROCEDURE dbo.usp_payments_select
    @payment_no  NVARCHAR(20) = NULL,
    @supplier_id INT          = NULL,
    @date_from   DATE         = NULL,
    @date_to     DATE         = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        p.payment_id,
        p.payment_no,
        p.payment_date,
        p.supplier_id,
        p.supplier_code,
        p.supplier_name,
        p.supplier_postal_code,
        p.supplier_address1,
        p.supplier_address2,
        p.line_no,
        p.payment_method_id,
        pm.payment_method_name,
        p.amount,
        p.bill_due_date,
        p.ap_closing_at,
        p.slip_remarks,
        p.line_remarks,
        p.row_version
    FROM payments p
    INNER JOIN payment_method_classifications pm ON pm.payment_method_id = p.payment_method_id
    WHERE p.is_deleted = 0
      AND (@payment_no  IS NULL OR p.payment_no  = @payment_no)
      AND (@supplier_id IS NULL OR p.supplier_id = @supplier_id)
      AND (@date_from   IS NULL OR p.payment_date >= @date_from)
      AND (@date_to     IS NULL OR p.payment_date <= @date_to)
    ORDER BY p.payment_no, p.line_no;
END;