using bmcs_app.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace bmcs_app.Infrastructure.Configurations;

public class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        // DbSet プロパティ名（複数形）から命名変換されるテーブル名が DDL の単数形と
        // 一致しないため、全エンティティで ToTable を明示する。
        builder.ToTable("employee");

        builder.HasKey(e => e.EmployeeCode);

        builder.Property(e => e.EmployeeCode).HasMaxLength(10).IsUnicode(false);
        builder.Property(e => e.EmployeeName).HasMaxLength(40);
        builder.Property(e => e.EmployeeNameKana).HasMaxLength(40);

        builder.ConfigureAuditColumns();
    }
}
