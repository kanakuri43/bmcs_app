using bmcs_app.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace bmcs_app.Infrastructure.Configurations;

public class MonthlyClosingConfiguration : IEntityTypeConfiguration<MonthlyClosing>
{
    public void Configure(EntityTypeBuilder<MonthlyClosing> builder)
    {
        builder.ToTable("monthly_closing");

        builder.HasKey(e => e.ClosingYearMonth);

        builder.Property(e => e.ClosingYearMonth).HasMaxLength(6).IsFixedLength().IsUnicode(false);
        builder.Property(e => e.ConfirmedBy).HasMaxLength(10).IsUnicode(false);
        builder.Property(e => e.ReleasedBy).HasMaxLength(10).IsUnicode(false);

        builder.Property(e => e.ClosingStatus).HasConversion<byte>();

        builder.ConfigureAuditColumns();
    }
}
