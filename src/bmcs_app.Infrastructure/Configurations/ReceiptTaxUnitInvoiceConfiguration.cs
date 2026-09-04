using bmcs_app.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace bmcs_app.Infrastructure.Configurations;

public class ReceiptTaxUnitInvoiceConfiguration : IEntityTypeConfiguration<ReceiptTaxUnitInvoice>
{
    public void Configure(EntityTypeBuilder<ReceiptTaxUnitInvoice> builder)
    {
        builder.ConfigureReceiptTaxUnitColumns("receipt_tax_unit_invoice");

        builder.HasOne<BillingTaxUnitInvoice>().WithMany()
            .HasForeignKey(e => e.BillingNumber).OnDelete(DeleteBehavior.NoAction);
    }
}
