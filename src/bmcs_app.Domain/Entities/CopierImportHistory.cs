namespace bmcs_app.Domain.Entities;

/// <summary>
/// コピー機売上取込履歴（copier_import_histories）。主キー（機番＋締日）で二重取込を防ぐ。
/// 連携テーブルのため論理削除・RowVersion を持たない（TrackedEntity を継承）。
/// SalesSlipNumber は sales の主キーが複合のため FK を張らない論理参照。
/// </summary>
public class CopierImportHistory : TrackedEntity
{
    public required string MachineNo { get; set; }

    public required DateOnly ClosingDate { get; set; }

    public required string SalesSlipNumber { get; set; }
}
