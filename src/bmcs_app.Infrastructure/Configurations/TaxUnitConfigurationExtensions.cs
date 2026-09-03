using bmcs_app.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace bmcs_app.Infrastructure.Configurations;

/// <summary>
/// 税単位別テーブル（BillingTaxUnit* / SalesTaxUnit* / PaymentTaxUnit*）の
/// 共通プロパティ設定をまとめる拡張メソッド。エンティティ側の共通基底クラス
/// （BillingTaxUnitBase 等）と対になる。
/// </summary>
public static class TaxUnitConfigurationExtensions
{
    public static void ConfigureBillingTaxUnitColumns<TEntity>(this EntityTypeBuilder<TEntity> builder, string tableName)
        where TEntity : BillingTaxUnitBase
    {
        builder.ToTable(tableName);
        builder.HasKey(e => e.BillingNumber);

        builder.Property(e => e.BillingNumber).HasMaxLength(20).IsUnicode(false);
        builder.Property(e => e.CustomerCode).HasMaxLength(10).IsUnicode(false);
        builder.Property(e => e.CustomerName).HasMaxLength(60);
        builder.Property(e => e.ClosingYearMonth).HasMaxLength(6).IsFixedLength().IsUnicode(false);

        builder.Property(e => e.PreviousBalance).HasPrecision(15, 2);
        builder.Property(e => e.PaymentAmount).HasPrecision(15, 2);
        builder.Property(e => e.SalesAmount).HasPrecision(15, 2);
        builder.Property(e => e.TaxAmount).HasPrecision(15, 2);
        builder.Property(e => e.CurrentBillingAmount).HasPrecision(15, 2);

        builder.Property(e => e.StandardRateTaxableAmount).HasPrecision(15, 2);
        builder.Property(e => e.StandardRateTaxAmount).HasPrecision(15, 2);
        builder.Property(e => e.ReducedRateTaxableAmount).HasPrecision(15, 2);
        builder.Property(e => e.ReducedRateTaxAmount).HasPrecision(15, 2);
        builder.Property(e => e.TaxExemptAmount).HasPrecision(15, 2);

        builder.Property(e => e.BillingStatus).HasConversion<byte>();
        builder.Property(e => e.ConfirmedBy).HasMaxLength(10).IsUnicode(false);
        builder.Property(e => e.ReleasedBy).HasMaxLength(10).IsUnicode(false);

        // ナビゲーションプロパティは持たせないが、SaveChanges の INSERT 順序を
        // EF Core が正しく解決できるよう FK 関係は登録する。
        builder.HasOne<Customer>().WithMany().HasForeignKey(e => e.CustomerCode).OnDelete(DeleteBehavior.NoAction);

        builder.ConfigureAuditColumns();
    }

    public static void ConfigureSalesTaxUnitColumns<TEntity>(this EntityTypeBuilder<TEntity> builder, string tableName)
        where TEntity : SalesTaxUnitBase
    {
        builder.ToTable(tableName);
        builder.HasKey(e => new { e.SalesSlipNumber, e.LineNumber });

        builder.Property(e => e.SalesSlipNumber).HasMaxLength(20).IsUnicode(false);
        builder.Property(e => e.CustomerCode).HasMaxLength(10).IsUnicode(false);
        builder.Property(e => e.CustomerName).HasMaxLength(60);
        builder.Property(e => e.ProductCode).HasMaxLength(20).IsUnicode(false);
        builder.Property(e => e.ProductName).HasMaxLength(60);
        builder.Property(e => e.Specification).HasMaxLength(60);
        builder.Property(e => e.UnitName).HasMaxLength(10);
        builder.Property(e => e.OrderSlipNumber).HasMaxLength(20).IsUnicode(false);

        builder.Property(e => e.Quantity).HasPrecision(13, 3);
        builder.Property(e => e.UnitPrice).HasPrecision(15, 4);
        builder.Property(e => e.Amount).HasPrecision(15, 2);
        builder.Property(e => e.CostPrice).HasPrecision(15, 4);
        builder.Property(e => e.TaxRate).HasPrecision(5, 2);
        builder.Property(e => e.SettledAmount).HasPrecision(15, 2);

        builder.Property(e => e.SlipType).HasConversion<byte>();
        builder.Property(e => e.TaxCategory).HasConversion<byte>();
        builder.Property(e => e.BillingStatus).HasConversion<byte>();
        builder.Property(e => e.SettlementStatus).HasConversion<byte>();

        builder.HasOne<Customer>().WithMany().HasForeignKey(e => e.CustomerCode).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Product>().WithMany().HasForeignKey(e => e.ProductCode).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<OrderSlip>().WithMany()
            .HasForeignKey(e => new { e.OrderSlipNumber, e.OrderLineNumber })
            .OnDelete(DeleteBehavior.NoAction);

        builder.ConfigureAuditColumns();
    }

    public static void ConfigurePaymentTaxUnitColumns<TEntity>(this EntityTypeBuilder<TEntity> builder, string tableName)
        where TEntity : PaymentTaxUnitBase
    {
        builder.ToTable(tableName);
        builder.HasKey(e => new { e.PaymentSlipNumber, e.LineNumber });

        builder.Property(e => e.PaymentSlipNumber).HasMaxLength(20).IsUnicode(false);
        builder.Property(e => e.CustomerCode).HasMaxLength(10).IsUnicode(false);
        builder.Property(e => e.CustomerName).HasMaxLength(60);
        builder.Property(e => e.BankAccountCode).HasMaxLength(10).IsUnicode(false);
        builder.Property(e => e.BillingNumber).HasMaxLength(20).IsUnicode(false);

        builder.Property(e => e.PaymentAmount).HasPrecision(15, 2);
        builder.Property(e => e.AllocatedAmount).HasPrecision(15, 2);
        builder.Property(e => e.FeeAdjustmentAmount).HasPrecision(15, 2);

        builder.Property(e => e.PaymentMethod).HasConversion<byte>();
        builder.Property(e => e.AllocationStatus).HasConversion<byte>();

        builder.HasOne<Customer>().WithMany().HasForeignKey(e => e.CustomerCode).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<BankAccount>().WithMany().HasForeignKey(e => e.BankAccountCode).OnDelete(DeleteBehavior.NoAction);

        builder.ConfigureAuditColumns();
    }
}
