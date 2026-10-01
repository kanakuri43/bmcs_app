using bmcs_app.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace bmcs_app.Infrastructure.Configurations;

public class MenuConfiguration : IEntityTypeConfiguration<Menu>
{
    public void Configure(EntityTypeBuilder<Menu> builder)
    {
        builder.ToTable("menus");

        builder.HasKey(e => e.MenuCode);

        builder.Property(e => e.MenuCode).HasMaxLength(20).IsUnicode(false);
        builder.Property(e => e.ParentMenuCode).HasMaxLength(20).IsUnicode(false);
        builder.Property(e => e.MenuName).HasMaxLength(40);
        builder.Property(e => e.ScreenKey).HasMaxLength(40).IsUnicode(false);

        // is_default_expanded は DB 側に DEFAULT 1 があるが、HasDefaultValue は付けない
        // （付けると CLR 既定値と同じ値の扱いが曖昧になるため。行の投入は SQL で行いアプリは読むだけ）。
        builder.HasOne<Menu>()
            .WithMany()
            .HasForeignKey(e => e.ParentMenuCode)
            .OnDelete(DeleteBehavior.NoAction);

        builder.ConfigureAuditColumns();
    }
}
