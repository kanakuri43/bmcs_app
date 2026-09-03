using bmcs_app.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace bmcs_app.Infrastructure.Configurations;

public class SalesTaxUnitSlipConfiguration : IEntityTypeConfiguration<SalesTaxUnitSlip>
{
    public void Configure(EntityTypeBuilder<SalesTaxUnitSlip> builder)
    {
        builder.ConfigureSalesTaxUnitColumns("sales_tax_unit_slip");

        builder.Property(e => e.SlipTaxAmount).HasPrecision(15, 2);
        builder.Property(e => e.BillingNumber).HasMaxLength(20).IsUnicode(false);
        builder.HasOne<BillingTaxUnitSlip>().WithMany()
            .HasForeignKey(e => e.BillingNumber).OnDelete(DeleteBehavior.NoAction);
    }
}
