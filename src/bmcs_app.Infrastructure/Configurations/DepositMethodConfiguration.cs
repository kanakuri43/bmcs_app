using bmcs_app.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace bmcs_app.Infrastructure.Configurations;

public class DepositMethodConfiguration : IEntityTypeConfiguration<DepositMethod>
{
    public void Configure(EntityTypeBuilder<DepositMethod> builder)
    {
        builder.ToTable("deposit_method");

        builder.HasKey(e => e.DepositMethodCode);

        builder.Property(e => e.DepositMethodCode).HasMaxLength(10).IsUnicode(false);
        builder.Property(e => e.DepositMethodName).HasMaxLength(20);

        builder.ConfigureAuditColumns();
    }
}
