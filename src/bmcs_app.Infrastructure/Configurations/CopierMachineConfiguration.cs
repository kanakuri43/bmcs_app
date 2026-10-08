using bmcs_app.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace bmcs_app.Infrastructure.Configurations;

public class CopierMachineConfiguration : IEntityTypeConfiguration<CopierMachine>
{
    public void Configure(EntityTypeBuilder<CopierMachine> builder)
    {
        builder.ToTable("copier_machines");

        builder.HasKey(e => e.MachineNo);

        builder.Property(e => e.MachineNo).HasMaxLength(20).IsUnicode(false);
        builder.Property(e => e.CustomerCode).HasMaxLength(10).IsUnicode(false);
        builder.Property(e => e.MachineModel).HasMaxLength(60);
        builder.Property(e => e.Remarks).HasMaxLength(100);

        builder.HasOne<Customer>().WithMany()
            .HasForeignKey(e => e.CustomerCode).OnDelete(DeleteBehavior.NoAction);

        builder.ConfigureAuditColumns();
    }
}
