using bmcs_app.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace bmcs_app.Infrastructure.Configurations;

public class BillingTaxUnitInvoiceConfiguration : IEntityTypeConfiguration<BillingTaxUnitInvoice>
{
    public void Configure(EntityTypeBuilder<BillingTaxUnitInvoice> builder)
        => builder.ConfigureBillingTaxUnitColumns("billing_tax_unit_invoice");
}
