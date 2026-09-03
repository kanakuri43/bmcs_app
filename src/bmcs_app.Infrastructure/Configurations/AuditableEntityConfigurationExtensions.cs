using bmcs_app.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace bmcs_app.Infrastructure.Configurations;

/// <summary>
/// 監査列・共通カラムのマッピングを1箇所にまとめる拡張メソッド。
/// テーブル名・カラム名の変換は UseSnakeCaseNamingConvention に任せるため、
/// ここでは型・精度・Unicode設定のみを行う（docs/database-schema.md 2.0）。
/// </summary>
public static class AuditableEntityConfigurationExtensions
{
    /// <summary>
    /// CreatedBy/CreatedAt/UpdatedBy/UpdatedAt の4列を設定する。
    /// IsDeleted/RowVersion を持たない TrackedEntity 継承エンティティ（連携テーブル・採番テーブル）向け。
    /// </summary>
    public static void ConfigureTrackedColumns<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : TrackedEntity
    {
        builder.Property(e => e.CreatedBy).HasMaxLength(10).IsUnicode(false);
        builder.Property(e => e.UpdatedBy).HasMaxLength(10).IsUnicode(false);
    }

    /// <summary>
    /// TrackedEntity の4列に加えて IsDeleted・RowVersion を設定する。
    /// マスタ・伝票系のほとんどのテーブルが対象。
    /// </summary>
    public static void ConfigureAuditColumns<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : AuditableEntity
    {
        builder.ConfigureTrackedColumns();
        builder.Property(e => e.RowVersion).IsRowVersion();
    }
}
