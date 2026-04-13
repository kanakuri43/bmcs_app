CREATE OR ALTER PROCEDURE dbo.usp_purchase_orders_select
    @purchase_order_no NVARCHAR(20) = NULL,
    @supplier_id       INT          = NULL,
    @date_from         DATE         = NULL,
    @date_to           DATE         = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        po.purchase_order_id,
        po.purchase_order_no,
        po.purchase_order_date,
        po.supplier_id,
        po.supplier_code,
        po.supplier_name,
        po.employee_id,
        po.employee_code,
        po.employee_name,
        po.line_no,
        po.product_id,
        po.product_code,
        po.product_name,
        po.quantity,
        po.unit_price,
        po.cost_price,
        po.quantity * po.unit_price AS line_amount,
        po.tax_type_id,
        tt.tax_type_name,
        po.tax_rate_type,
        po.tax_calc_unit_id,
        tu.tax_calc_unit_name,
        po.tax_fraction_id,
        tf.tax_fraction_name,
        po.applied_tax_rate,
        po.tax_amount,
        po.slip_tax_amount,
        po.slip_remarks,
        po.line_remarks,
        CASE WHEN EXISTS (
            SELECT 1 FROM purchases p WHERE p.purchase_order_no = po.purchase_order_no AND p.is_deleted = 0
        ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS has_purchases,
        po.row_version
    FROM purchase_orders po
    INNER JOIN tax_type_classifications      tt ON tt.tax_type_id      = po.tax_type_id
    INNER JOIN tax_calc_unit_classifications tu ON tu.tax_calc_unit_id = po.tax_calc_unit_id
    INNER JOIN tax_fraction_classifications  tf ON tf.tax_fraction_id  = po.tax_fraction_id
    WHERE po.is_deleted = 0
      AND (@purchase_order_no IS NULL OR po.purchase_order_no = @purchase_order_no)
      AND (@supplier_id       IS NULL OR po.supplier_id       = @supplier_id)
      AND (@date_from         IS NULL OR po.purchase_order_date >= @date_from)
      AND (@date_to           IS NULL OR po.purchase_order_date <= @date_to)
    ORDER BY po.purchase_order_no, po.line_no;
END;