using bmcs_app.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace bmcs_app.Infrastructure.Configurations;

public class DetailInvoiceConfiguration : IEntityTypeConfiguration<DetailInvoice>
{
    public void Configure(EntityTypeBuilder<DetailInvoice> builder)
    {
        builder.ToTable("detail_invoices");

        builder.HasKey(e => e.DetailInvoiceNumber);

        builder.Property(e => e.DetailInvoiceNumber).HasMaxLength(20).IsUnicode(false);
        builder.Property(e => e.CustomerCode).HasMaxLength(10).IsUnicode(false);
        builder.Property(e => e.CustomerName).HasMaxLength(60);
        builder.Property(e => e.AddresseeName).HasMaxLength(60);

        builder.Property(e => e.SalesAmount).HasPrecision(15, 2);
        builder.Property(e => e.TaxAmount).HasPrecision(15, 2);
        builder.Property(e => e.TotalAmount).HasPrecision(15, 2);

        builder.Property(e => e.StandardRateTaxableAmount).HasPrecision(15, 2);
        builder.Property(e => e.StandardRateTaxAmount).HasPrecision(15, 2);
        builder.Property(e => e.ReducedRateTaxableAmount).HasPrecision(15, 2);
        builder.Property(e => e.ReducedRateTaxAmount).HasPrecision(15, 2);
        builder.Property(e => e.TaxExemptAmount).HasPrecision(15, 2);

        builder.Property(e => e.InvoiceStatus).HasConversion<byte>();
        builder.Property(e => e.IssuedBy).HasMaxLength(10).IsUnicode(false);
        builder.Property(e => e.CancelledBy).HasMaxLength(10).IsUnicode(false);

        builder.HasOne<Customer>().WithMany().HasForeignKey(e => e.CustomerCode).OnDelete(DeleteBehavior.NoAction);

        builder.ConfigureAuditColumns();
    }
}
