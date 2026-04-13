CREATE OR ALTER PROCEDURE dbo.usp_payments_delete
    @payment_no NVARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM payments WHERE payment_no = @payment_no AND is_deleted = 0)
    BEGIN
        RAISERROR(N'伝票番号 %s が見つかりません。', 16, 1, @payment_no);
        RETURN;
    END

    IF EXISTS (
        SELECT 1 FROM payments
        WHERE payment_no = @payment_no AND is_deleted = 0
          AND ap_closing_at IS NOT NULL
    )
    BEGIN
        RAISERROR(N'集計済みの伝票は削除できません。', 16, 1);
        RETURN;
    END

    BEGIN TRANSACTION;
    UPDATE payments SET is_deleted = 1 WHERE payment_no = @payment_no AND is_deleted = 0;
    COMMIT TRANSACTION;
END;