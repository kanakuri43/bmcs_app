using bmcs_app.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace bmcs_app.Infrastructure.Configurations;

public class BillingTaxUnitSlipConfiguration : IEntityTypeConfiguration<BillingTaxUnitSlip>
{
    public void Configure(EntityTypeBuilder<BillingTaxUnitSlip> builder)
        => builder.ConfigureBillingTaxUnitColumns("billing_tax_unit_slip");
}
