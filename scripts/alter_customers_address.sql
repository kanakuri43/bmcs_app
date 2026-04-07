ALTER PROCEDURE dbo.usp_customers_upsert
    @customer_id      INT           = NULL,
    @customer_code    NVARCHAR(20),
    @customer_name    NVARCHAR(100),
    @closing_day      TINYINT,
    @tax_fraction_id  INT,
    @tax_calc_unit_id INT,
    @employee_id      INT           = NULL,
    @postal_code      NVARCHAR(8)   = NULL,
    @address1         NVARCHAR(100) = NULL,
    @address2         NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @closing_day NOT BETWEEN 1 AND 27 AND @closing_day <> 99
    BEGIN
        RAISERROR(N'Closing day must be 1-27 or 99 (end of month).', 16, 1);
        RETURN;
    END
    IF NOT EXISTS (SELECT 1 FROM tax_fraction_classifications WHERE tax_fraction_id = @tax_fraction_id AND is_deleted = 0)
    BEGIN
        RAISERROR(N'Tax fraction classification not found or deleted.', 16, 1);
        RETURN;
    END
    IF NOT EXISTS (SELECT 1 FROM tax_calc_unit_classifications WHERE tax_calc_unit_id = @tax_calc_unit_id AND is_deleted = 0)
    BEGIN
        RAISERROR(N'Tax calc unit classification not found or deleted.', 16, 1);
        RETURN;
    END
    IF @employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees WHERE employee_id = @employee_id AND is_deleted = 0)
    BEGIN
        RAISERROR(N'Employee not found or deleted.', 16, 1);
        RETURN;
    END
    IF EXISTS (
        SELECT 1 FROM customers
        WHERE customer_code = @customer_code
          AND is_deleted = 0
          AND (@customer_id IS NULL OR customer_id <> @customer_id)
    )
    BEGIN
        RAISERROR(N'Customer code %s is already in use.', 16, 1, @customer_code);
        RETURN;
    END

    IF @customer_id IS NULL
    BEGIN
        INSERT INTO customers (customer_code, customer_name, closing_day, tax_fraction_id, tax_calc_unit_id, employee_id, postal_code, address1, address2, is_deleted)
        VALUES (@customer_code, @customer_name, @closing_day, @tax_fraction_id, @tax_calc_unit_id, @employee_id, @postal_code, @address1, @address2, 0);
    END
    ELSE
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM customers WHERE customer_id = @customer_id AND is_deleted = 0)
        BEGIN
            RAISERROR(N'Customer ID %d not found or deleted.', 16, 1, @customer_id);
            RETURN;
        END
        UPDATE customers
        SET customer_code    = @customer_code,
            customer_name    = @customer_name,
            closing_day      = @closing_day,
            tax_fraction_id  = @tax_fraction_id,
            tax_calc_unit_id = @tax_calc_unit_id,
            employee_id      = @employee_id,
            postal_code      = @postal_code,
            address1         = @address1,
            address2         = @address2
        WHERE customer_id = @customer_id AND is_deleted = 0;
    END
END
