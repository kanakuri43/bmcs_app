using bmcs_app.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace bmcs_app.Infrastructure.Configurations;

public class PaymentTaxUnitInvoiceConfiguration : IEntityTypeConfiguration<PaymentTaxUnitInvoice>
{
    public void Configure(EntityTypeBuilder<PaymentTaxUnitInvoice> builder)
    {
        builder.ConfigurePaymentTaxUnitColumns("payment_tax_unit_invoice");

        builder.HasOne<BillingTaxUnitInvoice>().WithMany()
            .HasForeignKey(e => e.BillingNumber).OnDelete(DeleteBehavior.NoAction);
    }
}
