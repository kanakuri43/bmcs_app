using bmcs_app.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace bmcs_app.Infrastructure.Configurations;

public class SalesTaxUnitLineConfiguration : IEntityTypeConfiguration<SalesTaxUnitLine>
{
    public void Configure(EntityTypeBuilder<SalesTaxUnitLine> builder)
    {
        builder.ConfigureSalesTaxUnitColumns("sales_tax_unit_line");

        builder.Property(e => e.TaxAmount).HasPrecision(15, 2);
    }
}
