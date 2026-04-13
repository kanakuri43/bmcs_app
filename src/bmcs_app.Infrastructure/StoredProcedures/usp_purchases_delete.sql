CREATE OR ALTER PROCEDURE dbo.usp_purchases_delete
    @purchase_no NVARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM purchases WHERE purchase_no = @purchase_no AND is_deleted = 0)
    BEGIN
        RAISERROR(N'伝票番号 %s が見つかりません。', 16, 1, @purchase_no);
        RETURN;
    END

    IF EXISTS (
        SELECT 1 FROM purchases
        WHERE purchase_no = @purchase_no AND is_deleted = 0
          AND ap_closing_at IS NOT NULL
    )
    BEGIN
        RAISERROR(N'集計済みの伝票は削除できません。', 16, 1);
        RETURN;
    END

    BEGIN TRANSACTION;
    UPDATE purchases SET is_deleted = 1 WHERE purchase_no = @purchase_no AND is_deleted = 0;
    COMMIT TRANSACTION;
END;