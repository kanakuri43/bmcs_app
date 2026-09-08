using bmcs_app.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace bmcs_app.Infrastructure.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customer");

        builder.HasKey(e => e.CustomerCode);

        builder.Property(e => e.CustomerCode).HasMaxLength(10).IsUnicode(false);
        builder.Property(e => e.CustomerName).HasMaxLength(60);
        builder.Property(e => e.CustomerNameKana).HasMaxLength(60);
        builder.Property(e => e.PostalCode).HasMaxLength(8).IsUnicode(false);
        builder.Property(e => e.Address1).HasMaxLength(100);
        builder.Property(e => e.Address2).HasMaxLength(100);
        builder.Property(e => e.PhoneNumber).HasMaxLength(20).IsUnicode(false);
        builder.Property(e => e.FaxNumber).HasMaxLength(20).IsUnicode(false);
        builder.Property(e => e.ContactPersonName).HasMaxLength(40);
        builder.Property(e => e.SalesEmployeeCode).HasMaxLength(10).IsUnicode(false);

        builder.Property(e => e.TaxUnit).HasConversion<byte>();
        builder.Property(e => e.RoundingType).HasConversion<byte>();

        // CK_customer_tax_unit_closing_day 等の CHECK 制約は DB 側（Phase 1-5）で
        // 既に強制済みのため、EF Core 側では再定義しない（マイグレーションを使わない方針）。

        // Sales / Receipt / Billing から複合FK (customer_code, tax_unit) で参照される
        // ための代替キー（UQ_customer_code_tax_unit、010_unify_tax_unit_tables.sql）。
        // 「伝票の税単位は得意先マスタの税区分と必ず一致する」をDBに強制させるため。
        builder.HasAlternateKey(e => new { e.CustomerCode, e.TaxUnit });

        // ナビゲーションプロパティは持たせない方針だが、複数エンティティを同一
        // SaveChanges で保存したときの INSERT 順序を EF Core が正しく解決できるよう、
        // FK 関係だけは登録する（DB 側の FK 制約と対応させる）。
        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(e => e.SalesEmployeeCode)
            .OnDelete(DeleteBehavior.NoAction);

        builder.ConfigureAuditColumns();
    }
}
