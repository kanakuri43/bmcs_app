CREATE OR ALTER PROCEDURE dbo.usp_suppliers_upsert
    @supplier_id      INT           = NULL,
    @supplier_code    NVARCHAR(20),
    @supplier_name    NVARCHAR(100),
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
        RAISERROR(N'締め日は 1〜27 または 99（月末）を指定してください。', 16, 1);
        RETURN;
    END
    IF NOT EXISTS (SELECT 1 FROM tax_fraction_classifications WHERE tax_fraction_id = @tax_fraction_id AND is_deleted = 0)
    BEGIN
        RAISERROR(N'税端数区分が見つかりません。', 16, 1);
        RETURN;
    END
    IF NOT EXISTS (SELECT 1 FROM tax_calc_unit_classifications WHERE tax_calc_unit_id = @tax_calc_unit_id AND is_deleted = 0)
    BEGIN
        RAISERROR(N'税計算単位区分が見つかりません。', 16, 1);
        RETURN;
    END
    IF @employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees WHERE employee_id = @employee_id AND is_deleted = 0)
    BEGIN
        RAISERROR(N'担当社員が見つかりません。', 16, 1);
        RETURN;
    END
    IF EXISTS (
        SELECT 1 FROM suppliers
        WHERE supplier_code = @supplier_code
          AND is_deleted = 0
          AND (@supplier_id IS NULL OR supplier_id <> @supplier_id)
    )
    BEGIN
        RAISERROR(N'仕入先コード %s はすでに使用されています。', 16, 1, @supplier_code);
        RETURN;
    END

    IF @supplier_id IS NULL
    BEGIN
        INSERT INTO suppliers (supplier_code, supplier_name, closing_day, tax_fraction_id, tax_calc_unit_id, employee_id, postal_code, address1, address2, is_deleted)
        VALUES (@supplier_code, @supplier_name, @closing_day, @tax_fraction_id, @tax_calc_unit_id, @employee_id, @postal_code, @address1, @address2, 0);
    END
    ELSE
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM suppliers WHERE supplier_id = @supplier_id AND is_deleted = 0)
        BEGIN
            RAISERROR(N'仕入先 ID %d が見つかりません。', 16, 1, @supplier_id);
            RETURN;
        END
        UPDATE suppliers
        SET supplier_code    = @supplier_code,
            supplier_name    = @supplier_name,
            closing_day      = @closing_day,
            tax_fraction_id  = @tax_fraction_id,
            tax_calc_unit_id = @tax_calc_unit_id,
            employee_id      = @employee_id,
            postal_code      = @postal_code,
            address1         = @address1,
            address2         = @address2
        WHERE supplier_id = @supplier_id AND is_deleted = 0;
    END
END;