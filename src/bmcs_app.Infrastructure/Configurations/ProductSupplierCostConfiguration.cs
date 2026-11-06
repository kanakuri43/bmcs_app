using bmcs_app.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace bmcs_app.Infrastructure.Configurations;

public class ProductSupplierCostConfiguration : IEntityTypeConfiguration<ProductSupplierCost>
{
    public void Configure(EntityTypeBuilder<ProductSupplierCost> builder)
    {
        builder.ToTable("product_supplier_costs");

        builder.HasKey(e => new { e.ProductCode, e.SupplierCode });

        builder.Property(e => e.ProductCode).HasMaxLength(20).IsUnicode(false);
        builder.Property(e => e.SupplierCode).HasMaxLength(10).IsUnicode(false);
        builder.Property(e => e.CostPrice).HasPrecision(15, 4);

        builder.HasOne<Product>().WithMany()
            .HasForeignKey(e => e.ProductCode).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Supplier>().WithMany()
            .HasForeignKey(e => e.SupplierCode).OnDelete(DeleteBehavior.NoAction);

        builder.ConfigureAuditColumns();
    }
}
