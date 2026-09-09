using bmcs_app.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace bmcs_app.Infrastructure.Configurations;

/// <summary>
/// 締め入金（receipt）。旧 ReceiptTaxUnitInvoice / ReceiptTaxUnitSlip の統合版
/// （010_unify_tax_unit_tables.sql）。
/// </summary>
public class ReceiptConfiguration : IEntityTypeConfiguration<Receipt>
{
    public void Configure(EntityTypeBuilder<Receipt> builder)
    {
        builder.ToTable("receipt");
        builder.HasKey(e => new { e.ReceiptSlipNumber, e.LineNumber });

        builder.Property(e => e.ReceiptSlipNumber).HasMaxLength(20).IsUnicode(false);
        builder.Property(e => e.CustomerCode).HasMaxLength(10).IsUnicode(false);
        builder.Property(e => e.TaxUnit).HasConversion<byte>();
        builder.Property(e => e.CustomerName).HasMaxLength(60);
        builder.Property(e => e.BankAccountCode).HasMaxLength(10).IsUnicode(false);
        builder.Property(e => e.BillingNumber).HasMaxLength(20).IsUnicode(false);
        builder.Property(e => e.SlipRemarks).HasMaxLength(200);
        builder.Property(e => e.LineRemarks).HasMaxLength(100);

        builder.Property(e => e.ReceiptAmount).HasPrecision(15, 2);
        builder.Property(e => e.AllocatedAmount).HasPrecision(15, 2);
        builder.Property(e => e.FeeAdjustmentAmount).HasPrecision(15, 2);

        builder.Property(e => e.ReceiptMethod).HasConversion<byte>();
        builder.Property(e => e.AllocationStatus).HasConversion<byte>();

        builder.HasOne<Customer>().WithMany()
            .HasForeignKey(e => new { e.CustomerCode, e.TaxUnit })
            .HasPrincipalKey(c => new { c.CustomerCode, c.TaxUnit })
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<BankAccount>().WithMany().HasForeignKey(e => e.BankAccountCode).OnDelete(DeleteBehavior.NoAction);
        // BillingNumber が NULL の行（前受・過入金）は MATCH SIMPLE により FK 検査対象外。
        builder.HasOne<Billing>().WithMany()
            .HasForeignKey(e => new { e.BillingNumber, e.TaxUnit })
            .HasPrincipalKey(b => new { b.BillingNumber, b.TaxUnit })
            .OnDelete(DeleteBehavior.NoAction);

        builder.ConfigureAuditColumns();
    }
}
