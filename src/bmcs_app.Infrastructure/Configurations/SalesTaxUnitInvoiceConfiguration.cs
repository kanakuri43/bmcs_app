using bmcs_app.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace bmcs_app.Infrastructure.Configurations;

public class SalesTaxUnitInvoiceConfiguration : IEntityTypeConfiguration<SalesTaxUnitInvoice>
{
    public void Configure(EntityTypeBuilder<SalesTaxUnitInvoice> builder)
    {
        builder.ConfigureSalesTaxUnitColumns("sales_tax_unit_invoice");

        builder.Property(e => e.BillingNumber).HasMaxLength(20).IsUnicode(false);
        builder.HasOne<BillingTaxUnitInvoice>().WithMany()
            .HasForeignKey(e => e.BillingNumber).OnDelete(DeleteBehavior.NoAction);
    }
}
