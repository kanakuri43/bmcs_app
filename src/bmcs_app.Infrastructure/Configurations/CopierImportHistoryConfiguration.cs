using bmcs_app.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace bmcs_app.Infrastructure.Configurations;

public class CopierImportHistoryConfiguration : IEntityTypeConfiguration<CopierImportHistory>
{
    public void Configure(EntityTypeBuilder<CopierImportHistory> builder)
    {
        builder.ToTable("copier_import_histories");

        builder.HasKey(e => new { e.MachineNo, e.ClosingDate });

        builder.Property(e => e.MachineNo).HasMaxLength(20).IsUnicode(false);
        builder.Property(e => e.ClosingDate).HasColumnType("date");
        builder.Property(e => e.SalesSlipNumber).HasMaxLength(20).IsUnicode(false);

        builder.HasOne<CopierMachine>().WithMany()
            .HasForeignKey(e => e.MachineNo).OnDelete(DeleteBehavior.NoAction);

        builder.ConfigureTrackedColumns();
    }
}
