CREATE OR ALTER PROCEDURE dbo.usp_invoice_closing_cancel
    @process_date date,
    @customer_id  int = NULL
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRANSACTION;
    BEGIN TRY

        UPDATE sales
        SET    invoiced_at = NULL
        WHERE  is_deleted  = 0
          AND  CAST(invoiced_at AS date) = @process_date
          AND  (@customer_id IS NULL OR customer_id = @customer_id);

        COMMIT TRANSACTION;

    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
