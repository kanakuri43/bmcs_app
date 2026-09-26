using bmcs_app.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace bmcs_app.Infrastructure.Configurations;

public class BankAccountConfiguration : IEntityTypeConfiguration<BankAccount>
{
    public void Configure(EntityTypeBuilder<BankAccount> builder)
    {
        builder.ToTable("bank_accounts");

        builder.HasKey(e => e.BankAccountCode);

        builder.Property(e => e.BankAccountCode).HasMaxLength(10).IsUnicode(false);
        builder.Property(e => e.BankName).HasMaxLength(40);
        builder.Property(e => e.BranchName).HasMaxLength(40);
        builder.Property(e => e.AccountNumber).HasMaxLength(10).IsUnicode(false);
        builder.Property(e => e.AccountHolderName).HasMaxLength(60);

        builder.Property(e => e.AccountType).HasConversion<byte>();

        builder.ConfigureAuditColumns();
    }
}
