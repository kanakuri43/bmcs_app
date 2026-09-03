namespace bmcs_app.Domain.Entities;

/// <summary>
/// 作成者・更新者の記録のみを持つ基底クラス。
/// 論理削除・楽観的排他制御を持たないテーブル（連携テーブル・採番テーブル）が継承する。
/// 通常のテーブルは <see cref="AuditableEntity"/> を継承する。
/// </summary>
public abstract class TrackedEntity
{
    public required string CreatedBy { get; set; }

    public required DateTime CreatedAt { get; set; }

    public required string UpdatedBy { get; set; }

    public required DateTime UpdatedAt { get; set; }
}
