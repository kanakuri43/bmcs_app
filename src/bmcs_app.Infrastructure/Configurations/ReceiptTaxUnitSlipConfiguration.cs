using bmcs_app.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace bmcs_app.Infrastructure.Configurations;

public class ReceiptTaxUnitSlipConfiguration : IEntityTypeConfiguration<ReceiptTaxUnitSlip>
{
    public void Configure(EntityTypeBuilder<ReceiptTaxUnitSlip> builder)
    {
        builder.ConfigureReceiptTaxUnitColumns("receipt_tax_unit_slip");

        builder.HasOne<BillingTaxUnitSlip>().WithMany()
            .HasForeignKey(e => e.BillingNumber).OnDelete(DeleteBehavior.NoAction);
    }
}
