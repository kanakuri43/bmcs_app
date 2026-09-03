using bmcs_app.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace bmcs_app.Infrastructure.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("product");

        builder.HasKey(e => e.ProductCode);

        builder.Property(e => e.ProductCode).HasMaxLength(20).IsUnicode(false);
        builder.Property(e => e.ProductName).HasMaxLength(60);
        builder.Property(e => e.ProductNameKana).HasMaxLength(60);
        builder.Property(e => e.Specification).HasMaxLength(60);
        builder.Property(e => e.UnitName).HasMaxLength(10);

        builder.Property(e => e.StandardUnitPriceExclTax).HasPrecision(15, 4);
        builder.Property(e => e.StandardUnitPriceInclTax).HasPrecision(15, 4);
        builder.Property(e => e.StandardCostPrice).HasPrecision(15, 4);

        builder.Property(e => e.TaxCategory).HasConversion<byte>();

        builder.ConfigureAuditColumns();
    }
}
