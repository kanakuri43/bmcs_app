using bmcs_app.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace bmcs_app.Infrastructure.Configurations;

public class DetailInvoiceSalesLineConfiguration : IEntityTypeConfiguration<DetailInvoiceSalesLine>
{
    public void Configure(EntityTypeBuilder<DetailInvoiceSalesLine> builder)
    {
        builder.ToTable("detail_invoice_sales_line");

        builder.HasKey(e => new { e.DetailInvoiceNumber, e.SalesSlipNumber, e.SalesLineNumber });

        builder.Property(e => e.DetailInvoiceNumber).HasMaxLength(20).IsUnicode(false);
        builder.Property(e => e.SalesSlipNumber).HasMaxLength(20).IsUnicode(false);

        // 二重請求防止: 1つの売上明細行が紐づく明細請求書は最大1つ（DB側で UNIQUE 制約済み）。
        builder.HasIndex(e => new { e.SalesSlipNumber, e.SalesLineNumber }).IsUnique();

        builder.HasOne<DetailInvoice>().WithMany()
            .HasForeignKey(e => e.DetailInvoiceNumber).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<SalesTaxUnitLine>().WithMany()
            .HasForeignKey(e => new { e.SalesSlipNumber, e.SalesLineNumber })
            .OnDelete(DeleteBehavior.NoAction);

        builder.ConfigureTrackedColumns();
    }
}
