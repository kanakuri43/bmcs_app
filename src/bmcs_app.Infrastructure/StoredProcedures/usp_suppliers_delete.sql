CREATE OR ALTER PROCEDURE dbo.usp_suppliers_delete
    @supplier_id INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM suppliers WHERE supplier_id = @supplier_id AND is_deleted = 0)
    BEGIN
        RAISERROR(N'仕入先 ID %d が見つかりません。', 16, 1, @supplier_id);
        RETURN;
    END

    IF EXISTS (SELECT 1 FROM purchase_orders WHERE supplier_id = @supplier_id AND is_deleted = 0)
    BEGIN
        RAISERROR(N'発注伝票が存在する仕入先は削除できません。', 16, 1);
        RETURN;
    END
    IF EXISTS (SELECT 1 FROM purchases WHERE supplier_id = @supplier_id AND is_deleted = 0)
    BEGIN
        RAISERROR(N'仕入伝票が存在する仕入先は削除できません。', 16, 1);
        RETURN;
    END
    IF EXISTS (SELECT 1 FROM payments WHERE supplier_id = @supplier_id AND is_deleted = 0)
    BEGIN
        RAISERROR(N'支払伝票が存在する仕入先は削除できません。', 16, 1);
        RETURN;
    END

    UPDATE suppliers SET is_deleted = 1 WHERE supplier_id = @supplier_id AND is_deleted = 0;
END;