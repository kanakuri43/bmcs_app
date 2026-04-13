CREATE OR ALTER PROCEDURE dbo.usp_purchases_select
    @purchase_no NVARCHAR(20) = NULL,
    @supplier_id INT          = NULL,
    @date_from   DATE         = NULL,
    @date_to     DATE         = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        p.purchase_id,
        p.purchase_no,
        p.purchase_date,
        p.supplier_id,
        p.supplier_code,
        p.supplier_name,
        p.supplier_postal_code,
        p.supplier_address1,
        p.supplier_address2,
        p.purchase_order_id,
        p.purchase_order_no,
        p.employee_id,
        p.employee_code,
        p.employee_name,
        p.line_no,
        p.product_id,
        p.product_code,
        p.product_name,
        p.quantity,
        p.unit_price,
        p.cost_price,
        p.quantity * p.unit_price AS line_amount,
        p.tax_type_id,
        tt.tax_type_name,
        p.tax_rate_type,
        p.applied_tax_rate,
        p.tax_calc_unit_id,
        tu.tax_calc_unit_name,
        p.tax_fraction_id,
        tf.tax_fraction_name,
        p.line_tax_amount,
        p.slip_tax_amount,
        p.ap_closing_at,
        p.slip_remarks,
        p.line_remarks,
        p.row_version
    FROM purchases p
    INNER JOIN tax_type_classifications      tt ON tt.tax_type_id      = p.tax_type_id
    INNER JOIN tax_calc_unit_classifications tu ON tu.tax_calc_unit_id = p.tax_calc_unit_id
    INNER JOIN tax_fraction_classifications  tf ON tf.tax_fraction_id  = p.tax_fraction_id
    WHERE p.is_deleted = 0
      AND (@purchase_no IS NULL OR p.purchase_no = @purchase_no)
      AND (@supplier_id IS NULL OR p.supplier_id = @supplier_id)
      AND (@date_from   IS NULL OR p.purchase_date >= @date_from)
      AND (@date_to     IS NULL OR p.purchase_date <= @date_to)
    ORDER BY p.purchase_no, p.line_no;
END;