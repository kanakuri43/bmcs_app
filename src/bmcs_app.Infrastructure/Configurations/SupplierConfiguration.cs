using bmcs_app.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace bmcs_app.Infrastructure.Configurations;

public class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("suppliers");

        builder.HasKey(e => e.SupplierCode);

        builder.Property(e => e.SupplierCode).HasMaxLength(10).IsUnicode(false);
        builder.Property(e => e.SupplierName).HasMaxLength(60);
        builder.Property(e => e.SupplierNameKana).HasMaxLength(60);
        builder.Property(e => e.PostalCode).HasMaxLength(8).IsUnicode(false);
        builder.Property(e => e.Address1).HasMaxLength(100);
        builder.Property(e => e.Address2).HasMaxLength(100);
        builder.Property(e => e.PhoneNumber).HasMaxLength(20).IsUnicode(false);
        builder.Property(e => e.FaxNumber).HasMaxLength(20).IsUnicode(false);
        builder.Property(e => e.ContactPersonName).HasMaxLength(40);
        builder.Property(e => e.Remarks).HasMaxLength(100);

        builder.ConfigureAuditColumns();
    }
}
