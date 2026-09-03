namespace bmcs_app.Domain.Entities;

/// <summary>
/// 論理削除フラグと楽観的排他制御を持つ基底クラス。
/// マスタ・伝票系のほとんどのテーブルが継承する（docs/database-schema.md 2.0）。
/// </summary>
public abstract class AuditableEntity : TrackedEntity
{
    public bool IsDeleted { get; set; }

    public byte[]? RowVersion { get; set; }
}
