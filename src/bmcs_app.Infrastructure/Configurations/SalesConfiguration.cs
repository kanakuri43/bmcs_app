using bmcs_app.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace bmcs_app.Infrastructure.Configurations;

/// <summary>
/// 売上（sales）。旧 SalesTaxUnitInvoice / SalesTaxUnitSlip / SalesTaxUnitLine の
/// 統合版（010_unify_tax_unit_tables.sql）。税単位固有の税額カラム（SlipTaxAmount /
/// TaxAmount）と BillingNumber の対応は DB の CHECK 制約側で強制する
/// （CK_sales_tax_amount_by_tax_unit / CK_sales_billing_number_by_tax_unit）。
/// </summary>
public class SalesConfiguration : IEntityTypeConfiguration<Sales>
{
    public void Configure(EntityTypeBuilder<Sales> builder)
    {
        builder.ToTable("sales");
        builder.HasKey(e => new { e.SalesSlipNumber, e.LineNumber });

        builder.Property(e => e.SalesSlipNumber).HasMaxLength(20).IsUnicode(false);
        builder.Property(e => e.CustomerCode).HasMaxLength(10).IsUnicode(false);
        builder.Property(e => e.TaxUnit).HasConversion<byte>();
        builder.Property(e => e.CustomerName).HasMaxLength(60);
        builder.Property(e => e.ProductCode).HasMaxLength(20).IsUnicode(false);
        builder.Property(e => e.ProductName).HasMaxLength(60);
        builder.Property(e => e.Specification).HasMaxLength(60);
        builder.Property(e => e.UnitName).HasMaxLength(10);
        builder.Property(e => e.OrderSlipNumber).HasMaxLength(20).IsUnicode(false);
        builder.Property(e => e.BillingNumber).HasMaxLength(20).IsUnicode(false);
        builder.Property(e => e.SlipRemarks).HasMaxLength(200);
        builder.Property(e => e.LineRemarks).HasMaxLength(100);
        builder.Property(e => e.InternalRemarks).HasMaxLength(200);

        builder.Property(e => e.Quantity).HasPrecision(13, 3);
        builder.Property(e => e.UnitPrice).HasPrecision(15, 4);
        builder.Property(e => e.Amount).HasPrecision(15, 2);
        builder.Property(e => e.CostPrice).HasPrecision(15, 4);
        builder.Property(e => e.TaxRate).HasPrecision(5, 2);
        builder.Property(e => e.SlipTaxAmount).HasPrecision(15, 2);
        builder.Property(e => e.TaxAmount).HasPrecision(15, 2);
        builder.Property(e => e.SettledAmount).HasPrecision(15, 2);

        builder.Property(e => e.SlipType).HasConversion<byte>();
        builder.Property(e => e.TaxCategory).HasConversion<byte>();
        builder.Property(e => e.BillingStatus).HasConversion<byte>();
        builder.Property(e => e.SettlementStatus).HasConversion<byte>();

        // ナビゲーションプロパティは持たせないが、SaveChanges の INSERT 順序を
        // EF Core が正しく解決できるよう FK 関係は登録する。
        builder.HasOne<Customer>().WithMany()
            .HasForeignKey(e => new { e.CustomerCode, e.TaxUnit })
            .HasPrincipalKey(c => new { c.CustomerCode, c.TaxUnit })
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Product>().WithMany().HasForeignKey(e => e.ProductCode).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<OrderSlip>().WithMany()
            .HasForeignKey(e => new { e.OrderSlipNumber, e.OrderLineNumber })
            .OnDelete(DeleteBehavior.NoAction);
        // BillingNumber が NULL の行（未請求）は MATCH SIMPLE により FK 検査対象外。
        builder.HasOne<Billing>().WithMany()
            .HasForeignKey(e => new { e.BillingNumber, e.TaxUnit })
            .HasPrincipalKey(b => new { b.BillingNumber, b.TaxUnit })
            .OnDelete(DeleteBehavior.NoAction);

        builder.ConfigureAuditColumns();
    }
}
