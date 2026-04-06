CREATE OR ALTER PROCEDURE dbo.usp_ar_closing_cancel
    @process_date date,
    @customer_id  int = NULL
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRANSACTION;
    BEGIN TRY

        -- Remove accounts_receivable_histories created by the closing
        DELETE FROM accounts_receivable_histories
        WHERE  closing_date = @process_date
          AND  is_deleted   = 0
          AND  (@customer_id IS NULL OR customer_id = @customer_id);

        -- Reset ar_aggregated_at on receipts and sales
        UPDATE receipts
        SET    ar_aggregated_at = NULL
        WHERE  is_deleted        = 0
          AND  ar_aggregated_at  = @process_date
          AND  (@customer_id IS NULL OR customer_id = @customer_id);

        UPDATE sales
        SET    ar_aggregated_at = NULL
        WHERE  is_deleted        = 0
          AND  ar_aggregated_at  = @process_date
          AND  (@customer_id IS NULL OR customer_id = @customer_id);

        COMMIT TRANSACTION;

    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
