using bmcs_app.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace bmcs_app.Infrastructure.Configurations;

public class CustomerProductPriceConfiguration : IEntityTypeConfiguration<CustomerProductPrice>
{
    public void Configure(EntityTypeBuilder<CustomerProductPrice> builder)
    {
        builder.ToTable("customer_product_prices");

        builder.HasKey(e => new { e.CustomerCode, e.ProductCode });

        builder.Property(e => e.CustomerCode).HasMaxLength(10).IsUnicode(false);
        builder.Property(e => e.ProductCode).HasMaxLength(20).IsUnicode(false);
        builder.Property(e => e.UnitPrice).HasPrecision(15, 4);

        builder.HasOne<Customer>().WithMany()
            .HasForeignKey(e => e.CustomerCode).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Product>().WithMany()
            .HasForeignKey(e => e.ProductCode).OnDelete(DeleteBehavior.NoAction);

        builder.ConfigureAuditColumns();
    }
}
