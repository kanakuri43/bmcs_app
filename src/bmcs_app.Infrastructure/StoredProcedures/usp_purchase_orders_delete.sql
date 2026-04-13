CREATE OR ALTER PROCEDURE dbo.usp_purchase_orders_delete
    @purchase_order_no NVARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM purchase_orders WHERE purchase_order_no = @purchase_order_no AND is_deleted = 0)
    BEGIN
        RAISERROR(N'伝票番号 %s が見つかりません。', 16, 1, @purchase_order_no);
        RETURN;
    END

    IF EXISTS (SELECT 1 FROM purchases WHERE purchase_order_no = @purchase_order_no AND is_deleted = 0)
    BEGIN
        RAISERROR(N'仕入登録済みの発注は削除できません。', 16, 1);
        RETURN;
    END

    BEGIN TRANSACTION;
    UPDATE purchase_orders SET is_deleted = 1 WHERE purchase_order_no = @purchase_order_no AND is_deleted = 0;
    COMMIT TRANSACTION;
END;