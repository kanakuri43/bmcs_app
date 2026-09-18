using bmcs_app.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace bmcs_app.Infrastructure.Configurations;

public class OrderSlipConfiguration : IEntityTypeConfiguration<OrderSlip>
{
    public void Configure(EntityTypeBuilder<OrderSlip> builder)
    {
        builder.ToTable("order_slip");

        builder.HasKey(e => new { e.OrderSlipNumber, e.LineNumber });

        builder.Property(e => e.OrderSlipNumber).HasMaxLength(20).IsUnicode(false);
        builder.Property(e => e.CustomerCode).HasMaxLength(10).IsUnicode(false);
        builder.Property(e => e.CustomerName).HasMaxLength(60);
        builder.Property(e => e.SubCustomerId).HasMaxLength(20).IsUnicode(false);
        builder.Property(e => e.ProductCode).HasMaxLength(20).IsUnicode(false);
        builder.Property(e => e.ProductName).HasMaxLength(60);
        builder.Property(e => e.Specification).HasMaxLength(60);
        builder.Property(e => e.UnitName).HasMaxLength(10);
        builder.Property(e => e.SlipRemarks).HasMaxLength(200);
        builder.Property(e => e.LineRemarks).HasMaxLength(100);
        builder.Property(e => e.InternalRemarks).HasMaxLength(200);

        builder.Property(e => e.OrderQuantity).HasPrecision(13, 3);
        builder.Property(e => e.UnitPrice).HasPrecision(15, 4);
        builder.Property(e => e.Amount).HasPrecision(15, 2);
        builder.Property(e => e.CostPrice).HasPrecision(15, 4);
        builder.Property(e => e.TaxRate).HasPrecision(5, 2);
        builder.Property(e => e.AllocatedQuantity).HasPrecision(13, 3);
        builder.Property(e => e.SalesConfirmedQuantity).HasPrecision(13, 3);

        builder.Property(e => e.TaxCategory).HasConversion<byte>();
        builder.Property(e => e.OrderStatus).HasConversion<byte>();

        builder.HasOne<Customer>().WithMany().HasForeignKey(e => e.CustomerCode).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Product>().WithMany().HasForeignKey(e => e.ProductCode).OnDelete(DeleteBehavior.NoAction);

        builder.ConfigureAuditColumns();
    }
}
