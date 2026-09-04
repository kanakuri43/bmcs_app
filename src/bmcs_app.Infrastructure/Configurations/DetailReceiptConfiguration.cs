using bmcs_app.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace bmcs_app.Infrastructure.Configurations;

public class DetailReceiptConfiguration : IEntityTypeConfiguration<DetailReceipt>
{
    public void Configure(EntityTypeBuilder<DetailReceipt> builder)
    {
        builder.ToTable("detail_receipt");

        builder.HasKey(e => new { e.DetailReceiptNumber, e.LineNumber });

        builder.Property(e => e.DetailReceiptNumber).HasMaxLength(20).IsUnicode(false);
        builder.Property(e => e.CustomerCode).HasMaxLength(10).IsUnicode(false);
        builder.Property(e => e.CustomerName).HasMaxLength(60);
        builder.Property(e => e.BankAccountCode).HasMaxLength(10).IsUnicode(false);
        builder.Property(e => e.TargetSalesSlipNumber).HasMaxLength(20).IsUnicode(false);
        builder.Property(e => e.TargetDetailInvoiceNumber).HasMaxLength(20).IsUnicode(false);

        builder.Property(e => e.ReceiptAmount).HasPrecision(15, 2);
        builder.Property(e => e.AllocatedAmount).HasPrecision(15, 2);
        builder.Property(e => e.FeeAdjustmentAmount).HasPrecision(15, 2);

        builder.Property(e => e.ReceiptMethod).HasConversion<byte>();
        builder.Property(e => e.TargetType).HasConversion<byte>();
        builder.Property(e => e.AllocationStatus).HasConversion<byte>();

        builder.HasOne<Customer>().WithMany().HasForeignKey(e => e.CustomerCode).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<BankAccount>().WithMany().HasForeignKey(e => e.BankAccountCode).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<SalesTaxUnitLine>().WithMany()
            .HasForeignKey(e => new { e.TargetSalesSlipNumber, e.TargetSalesLineNumber })
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<DetailInvoice>().WithMany()
            .HasForeignKey(e => e.TargetDetailInvoiceNumber).OnDelete(DeleteBehavior.NoAction);

        builder.ConfigureAuditColumns();
    }
}
