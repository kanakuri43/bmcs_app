using bmcs_app.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace bmcs_app.Infrastructure.Configurations;

/// <summary>
/// 請求データ（billing）。旧 BillingTaxUnitInvoice / BillingTaxUnitSlip の統合版
/// （010_unify_tax_unit_tables.sql）。
/// </summary>
public class BillingConfiguration : IEntityTypeConfiguration<Billing>
{
    public void Configure(EntityTypeBuilder<Billing> builder)
    {
        builder.ToTable("billing");
        builder.HasKey(e => e.BillingNumber);

        builder.Property(e => e.BillingNumber).HasMaxLength(20).IsUnicode(false);
        builder.Property(e => e.CustomerCode).HasMaxLength(10).IsUnicode(false);
        builder.Property(e => e.TaxUnit).HasConversion<byte>();
        builder.Property(e => e.CustomerName).HasMaxLength(60);
        builder.Property(e => e.ClosingYearMonth).HasMaxLength(6).IsFixedLength().IsUnicode(false);

        builder.Property(e => e.PreviousBalance).HasPrecision(15, 2);
        builder.Property(e => e.ReceiptAmount).HasPrecision(15, 2);
        builder.Property(e => e.SalesAmount).HasPrecision(15, 2);
        builder.Property(e => e.TaxAmount).HasPrecision(15, 2);
        builder.Property(e => e.CurrentBillingAmount).HasPrecision(15, 2);

        builder.Property(e => e.StandardRateTaxableAmount).HasPrecision(15, 2);
        builder.Property(e => e.StandardRateTaxAmount).HasPrecision(15, 2);
        builder.Property(e => e.ReducedRateTaxableAmount).HasPrecision(15, 2);
        builder.Property(e => e.ReducedRateTaxAmount).HasPrecision(15, 2);
        builder.Property(e => e.TaxExemptAmount).HasPrecision(15, 2);

        builder.Property(e => e.BillingStatus).HasConversion<byte>();
        builder.Property(e => e.ConfirmedBy).HasMaxLength(10).IsUnicode(false);
        builder.Property(e => e.ReleasedBy).HasMaxLength(10).IsUnicode(false);

        // Sales / Receipt から複合FK (billing_number, tax_unit) で参照されるための
        // 代替キー（UQ_billing_number_tax_unit）。税単位をまたいで請求データを
        // 参照できないことをDBに強制させるため。
        builder.HasAlternateKey(e => new { e.BillingNumber, e.TaxUnit });

        // ナビゲーションプロパティは持たせないが、SaveChanges の INSERT 順序を
        // EF Core が正しく解決できるよう FK 関係は登録する。
        builder.HasOne<Customer>().WithMany()
            .HasForeignKey(e => new { e.CustomerCode, e.TaxUnit })
            .HasPrincipalKey(c => new { c.CustomerCode, c.TaxUnit })
            .OnDelete(DeleteBehavior.NoAction);

        builder.ConfigureAuditColumns();
    }
}
