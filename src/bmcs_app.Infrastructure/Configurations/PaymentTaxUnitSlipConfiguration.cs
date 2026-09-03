using bmcs_app.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace bmcs_app.Infrastructure.Configurations;

public class PaymentTaxUnitSlipConfiguration : IEntityTypeConfiguration<PaymentTaxUnitSlip>
{
    public void Configure(EntityTypeBuilder<PaymentTaxUnitSlip> builder)
    {
        builder.ConfigurePaymentTaxUnitColumns("payment_tax_unit_slip");

        builder.HasOne<BillingTaxUnitSlip>().WithMany()
            .HasForeignKey(e => e.BillingNumber).OnDelete(DeleteBehavior.NoAction);
    }
}
