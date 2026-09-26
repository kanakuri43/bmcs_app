using bmcs_app.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace bmcs_app.Infrastructure.Configurations;

/// <summary>
/// 締め入金の請求への充当（receipt_allocation）。<see cref="Receipt"/>から分離した内部データ
/// （016_split_receipt_allocation.sql）。
/// </summary>
public class ReceiptAllocationConfiguration : IEntityTypeConfiguration<ReceiptAllocation>
{
    public void Configure(EntityTypeBuilder<ReceiptAllocation> builder)
    {
        builder.ToTable("receipt_allocations");
        builder.HasKey(e => new { e.ReceiptSlipNumber, e.LineNumber });

        builder.Property(e => e.ReceiptSlipNumber).HasMaxLength(20).IsUnicode(false);
        builder.Property(e => e.CustomerCode).HasMaxLength(10).IsUnicode(false);
        builder.Property(e => e.TaxUnit).HasConversion<byte>();
        builder.Property(e => e.BillingNumber).HasMaxLength(20).IsUnicode(false);

        builder.Property(e => e.AllocatedAmount).HasPrecision(15, 2);
        builder.Property(e => e.FeeAdjustmentAmount).HasPrecision(15, 2);

        builder.HasOne<Customer>().WithMany()
            .HasForeignKey(e => new { e.CustomerCode, e.TaxUnit })
            .HasPrincipalKey(c => new { c.CustomerCode, c.TaxUnit })
            .OnDelete(DeleteBehavior.NoAction);
        // BillingNumber が NULL の行（前受・過入金）は MATCH SIMPLE により FK 検査対象外。
        builder.HasOne<Billing>().WithMany()
            .HasForeignKey(e => new { e.BillingNumber, e.TaxUnit })
            .HasPrincipalKey(b => new { b.BillingNumber, b.TaxUnit })
            .OnDelete(DeleteBehavior.NoAction);

        builder.ConfigureAuditColumns();
    }
}
