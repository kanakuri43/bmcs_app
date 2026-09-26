using bmcs_app.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace bmcs_app.Infrastructure.Configurations;

/// <summary>
/// 月次締め（monthly_closing）。得意先×月末日で1レコード（billing 類似レイアウト。
/// 012_redesign_monthly_closing.sql で全社単位1レコード/月から再設計）。
/// </summary>
public class MonthlyClosingConfiguration : IEntityTypeConfiguration<MonthlyClosing>
{
    public void Configure(EntityTypeBuilder<MonthlyClosing> builder)
    {
        builder.ToTable("monthly_closings");

        builder.HasKey(e => new { e.ClosingDate, e.CustomerCode });

        builder.Property(e => e.CustomerCode).HasMaxLength(10).IsUnicode(false);
        builder.Property(e => e.TaxUnit).HasConversion<byte>();
        builder.Property(e => e.CustomerName).HasMaxLength(60);

        builder.Property(e => e.PreviousBalance).HasPrecision(15, 2);
        builder.Property(e => e.SalesAmount).HasPrecision(15, 2);
        builder.Property(e => e.ReceiptAmount).HasPrecision(15, 2);
        builder.Property(e => e.TaxAmount).HasPrecision(15, 2);
        builder.Property(e => e.ClosingBalance).HasPrecision(15, 2);

        builder.Property(e => e.StandardRateTaxableAmount).HasPrecision(15, 2);
        builder.Property(e => e.StandardRateTaxAmount).HasPrecision(15, 2);
        builder.Property(e => e.ReducedRateTaxableAmount).HasPrecision(15, 2);
        builder.Property(e => e.ReducedRateTaxAmount).HasPrecision(15, 2);
        builder.Property(e => e.TaxExemptAmount).HasPrecision(15, 2);

        builder.Property(e => e.ClosingStatus).HasConversion<byte>();
        builder.Property(e => e.ConfirmedBy).HasMaxLength(10).IsUnicode(false);
        builder.Property(e => e.ReleasedBy).HasMaxLength(10).IsUnicode(false);

        // ナビゲーションプロパティは持たせないが、SaveChanges の INSERT 順序を
        // EF Core が正しく解決できるよう FK 関係は登録する（billing と同じパターン）。
        builder.HasOne<Customer>().WithMany()
            .HasForeignKey(e => new { e.CustomerCode, e.TaxUnit })
            .HasPrincipalKey(c => new { c.CustomerCode, c.TaxUnit })
            .OnDelete(DeleteBehavior.NoAction);

        builder.ConfigureAuditColumns();
    }
}
