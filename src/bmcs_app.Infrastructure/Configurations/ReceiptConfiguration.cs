using bmcs_app.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace bmcs_app.Infrastructure.Configurations;

/// <summary>
/// 締め入金（receipt）。明細行＝支払手段の内訳（docs/design_document.md 17章、2026-09-15改訂）。
/// 請求への充当は<see cref="ReceiptAllocation"/>が別テーブルで担う。旧 ReceiptTaxUnitInvoice /
/// ReceiptTaxUnitSlip の統合版（010_unify_tax_unit_tables.sql）。
/// </summary>
public class ReceiptConfiguration : IEntityTypeConfiguration<Receipt>
{
    public void Configure(EntityTypeBuilder<Receipt> builder)
    {
        builder.ToTable("receipt");
        builder.HasKey(e => new { e.ReceiptSlipNumber, e.LineNumber });

        builder.Property(e => e.ReceiptSlipNumber).HasMaxLength(20).IsUnicode(false);
        builder.Property(e => e.CustomerCode).HasMaxLength(10).IsUnicode(false);
        builder.Property(e => e.TaxUnit).HasConversion<byte>();
        builder.Property(e => e.CustomerName).HasMaxLength(60);
        builder.Property(e => e.BankAccountCode).HasMaxLength(10).IsUnicode(false);
        builder.Property(e => e.SlipRemarks).HasMaxLength(200);
        builder.Property(e => e.LineRemarks).HasMaxLength(100);

        builder.Property(e => e.Amount).HasPrecision(15, 2);

        builder.Property(e => e.ReceiptMethod).HasConversion<byte>();
        builder.Property(e => e.AllocationStatus).HasConversion<byte>();

        builder.HasOne<Customer>().WithMany()
            .HasForeignKey(e => new { e.CustomerCode, e.TaxUnit })
            .HasPrincipalKey(c => new { c.CustomerCode, c.TaxUnit })
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<BankAccount>().WithMany().HasForeignKey(e => e.BankAccountCode).OnDelete(DeleteBehavior.NoAction);

        builder.ConfigureAuditColumns();
    }
}
