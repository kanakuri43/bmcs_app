using bmcs_app.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace bmcs_app.Infrastructure.Configurations;

public class DetailPaymentConfiguration : IEntityTypeConfiguration<DetailPayment>
{
    public void Configure(EntityTypeBuilder<DetailPayment> builder)
    {
        builder.ToTable("detail_payment");

        builder.HasKey(e => new { e.DetailPaymentNumber, e.LineNumber });

        builder.Property(e => e.DetailPaymentNumber).HasMaxLength(20).IsUnicode(false);
        builder.Property(e => e.CustomerCode).HasMaxLength(10).IsUnicode(false);
        builder.Property(e => e.CustomerName).HasMaxLength(60);
        builder.Property(e => e.BankAccountCode).HasMaxLength(10).IsUnicode(false);
        builder.Property(e => e.TargetSalesSlipNumber).HasMaxLength(20).IsUnicode(false);
        builder.Property(e => e.TargetDetailInvoiceNumber).HasMaxLength(20).IsUnicode(false);

        builder.Property(e => e.PaymentAmount).HasPrecision(15, 2);
        builder.Property(e => e.AllocatedAmount).HasPrecision(15, 2);
        builder.Property(e => e.FeeAdjustmentAmount).HasPrecision(15, 2);

        builder.Property(e => e.PaymentMethod).HasConversion<byte>();
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
