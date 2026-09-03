using bmcs_app.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace bmcs_app.Infrastructure.Configurations;

public class CompanyInfoConfiguration : IEntityTypeConfiguration<CompanyInfo>
{
    public void Configure(EntityTypeBuilder<CompanyInfo> builder)
    {
        builder.ToTable("company_info");

        builder.HasKey(e => e.CompanyInfoId);

        builder.Property(e => e.CompanyName).HasMaxLength(60);
        builder.Property(e => e.InvoiceRegistrationNumber).HasMaxLength(14).IsUnicode(false);
        builder.Property(e => e.PostalCode).HasMaxLength(8).IsUnicode(false);
        builder.Property(e => e.Address1).HasMaxLength(100);
        builder.Property(e => e.Address2).HasMaxLength(100);
        builder.Property(e => e.PhoneNumber).HasMaxLength(20).IsUnicode(false);
        builder.Property(e => e.FaxNumber).HasMaxLength(20).IsUnicode(false);
        builder.Property(e => e.RepresentativeName).HasMaxLength(40);

        builder.ConfigureAuditColumns();
    }
}
