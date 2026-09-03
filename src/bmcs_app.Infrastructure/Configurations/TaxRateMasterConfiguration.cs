using bmcs_app.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace bmcs_app.Infrastructure.Configurations;

public class TaxRateMasterConfiguration : IEntityTypeConfiguration<TaxRateMaster>
{
    public void Configure(EntityTypeBuilder<TaxRateMaster> builder)
    {
        builder.ToTable("tax_rate_master");

        builder.HasKey(e => e.EffectiveDate);

        builder.Property(e => e.StandardTaxRate).HasPrecision(5, 2);
        builder.Property(e => e.ReducedTaxRate).HasPrecision(5, 2);

        builder.ConfigureAuditColumns();
    }
}
