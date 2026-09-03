using bmcs_app.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace bmcs_app.Infrastructure.Configurations;

public class DetailInvoiceConfiguration : IEntityTypeConfiguration<DetailInvoice>
{
    public void Configure(EntityTypeBuilder<DetailInvoice> builder)
    {
        builder.ToTable("detail_invoice");

        builder.HasKey(e => e.DetailInvoiceNumber);

        builder.Property(e => e.DetailInvoiceNumber).HasMaxLength(20).IsUnicode(false);
        builder.Property(e => e.CustomerCode).HasMaxLength(10).IsUnicode(false);
        builder.Property(e => e.CustomerName).HasMaxLength(60);
        builder.Property(e => e.AddresseeName).HasMaxLength(60);

        builder.Property(e => e.SalesAmount).HasPrecision(15, 2);
        builder.Property(e => e.TaxAmount).HasPrecision(15, 2);
        builder.Property(e => e.TotalAmount).HasPrecision(15, 2);

        // 数字を含む列名は snake_case 変換で意図通りにならないため明示する
        // （TaxUnitConfigurationExtensions.ConfigureBillingTaxUnitColumns と同じ理由）。
        builder.Property(e => e.Taxable10Amount).HasColumnName("taxable_10_amount").HasPrecision(15, 2);
        builder.Property(e => e.Tax10Amount).HasColumnName("tax_10_amount").HasPrecision(15, 2);
        builder.Property(e => e.Reduced8Amount).HasColumnName("reduced_8_amount").HasPrecision(15, 2);
        builder.Property(e => e.Tax8Amount).HasColumnName("tax_8_amount").HasPrecision(15, 2);
        builder.Property(e => e.TaxExemptAmount).HasPrecision(15, 2);

        builder.Property(e => e.InvoiceStatus).HasConversion<byte>();
        builder.Property(e => e.IssuedBy).HasMaxLength(10).IsUnicode(false);
        builder.Property(e => e.CancelledBy).HasMaxLength(10).IsUnicode(false);

        builder.HasOne<Customer>().WithMany().HasForeignKey(e => e.CustomerCode).OnDelete(DeleteBehavior.NoAction);

        builder.ConfigureAuditColumns();
    }
}
