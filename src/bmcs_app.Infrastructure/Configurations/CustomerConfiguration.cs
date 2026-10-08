using bmcs_app.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace bmcs_app.Infrastructure.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");

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
        builder.Property(e => e.BillingCustomerCode).HasMaxLength(10).IsUnicode(false);
        builder.Property(e => e.BankAccountCode1).HasMaxLength(10).IsUnicode(false);
        builder.Property(e => e.BankAccountCode2).HasMaxLength(10).IsUnicode(false);

        builder.Property(e => e.TaxUnit).HasConversion<byte>();
        builder.Property(e => e.RoundingType).HasConversion<byte>();

        // IsBillingRoot は DB の永続化計算列 is_billing_root に対応するが、EF にはマップしない
        // （INSERT時に書き込もうとして失敗するため。アプリ側は BillingCustomerCode == CustomerCode
        // で同じ判定ができる）。billing_parent_root_flag（常に1の定数計算列）も同様にマップしない。
        builder.Ignore(e => e.IsBillingRoot);

        // CK_customers_tax_unit_closing_day 等の CHECK 制約は DB 側で
        // 既に強制済みのため、EF Core 側では再定義しない（マイグレーションを使わない方針）。

        // Sales / Receipt / Billing から複合FK (customer_code, tax_unit) で参照される
        // ための代替キー（UQ_customers_code_tax_unit、010_unify_tax_unit_tables.sql）。
        // 「伝票の税単位は得意先マスタの税区分と必ず一致する」をDBに強制させるため。
        builder.HasAlternateKey(e => new { e.CustomerCode, e.TaxUnit });

        // ナビゲーションプロパティは持たせない方針だが、複数エンティティを同一
        // SaveChanges で保存したときの INSERT 順序を EF Core が正しく解決できるよう、
        // FK 関係だけは登録する（DB 側の FK 制約と対応させる）。
        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(e => e.SalesEmployeeCode)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<BankAccount>()
            .WithMany()
            .HasForeignKey(e => e.BankAccountCode1)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<BankAccount>()
            .WithMany()
            .HasForeignKey(e => e.BankAccountCode2)
            .OnDelete(DeleteBehavior.NoAction);

        // 請求集約先の自己参照複合FK（FK_customers_billing_customer、
        // scripts/020_add_billing_customer_code.sql）は billing_parent_root_flag
        // という定数の計算列を含むため EF Core では表現できない。DB側のみで強制する。
        // 帰結として、請求集約先と請求集約元を同一 SaveChanges で同時に新規作成する
        // ことはできない（得意先マスタ画面は1件ずつ保存するため実害はない）。

        builder.ConfigureAuditColumns();
    }
}
