CREATE OR ALTER PROCEDURE dbo.usp_invoice_closing_cancel
    @process_date date,
    @customer_id  int = NULL
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRANSACTION;
    BEGIN TRY

        -- Remove invoice_headers created by the closing
        DELETE FROM invoice_headers
        WHERE  invoice_date = @process_date
          AND  is_deleted   = 0
          AND  (@customer_id IS NULL OR customer_id = @customer_id);

        -- Reset invoiced_at on sales
        UPDATE sales
        SET    invoiced_at = NULL
        WHERE  is_deleted  = 0
          AND  invoiced_at = @process_date
          AND  (@customer_id IS NULL OR customer_id = @customer_id);

        -- Reset invoiced_at on receipts
        UPDATE receipts
        SET    invoiced_at = NULL
        WHERE  is_deleted  = 0
          AND  invoiced_at = @process_date
          AND  (@customer_id IS NULL OR customer_id = @customer_id);

        COMMIT TRANSACTION;

    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
