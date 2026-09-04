using bmcs_app.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace bmcs_app.Infrastructure;

/// <summary>
/// 販売管理システムの DbContext。
/// テーブル名・カラム名の PascalCase ↔ snake_case 変換は UseSnakeCaseNamingConvention
/// （InfrastructureServiceCollectionExtensions で設定）に任せる。マッピング設定の詳細は
/// Configurations/ 配下の IEntityTypeConfiguration 実装を参照。
/// </summary>
public class BmcsDbContext(DbContextOptions<BmcsDbContext> options) : DbContext(options)
{
    /// <summary>
    /// このインスタンスの識別子。
    /// ウィンドウごとに DbContext のスコープが分かれていることを確認するために使う。
    /// </summary>
    public Guid InstanceId { get; } = Guid.NewGuid();

    public DbSet<Employee> Employees => Set<Employee>();

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<TaxRateMaster> TaxRateMasters => Set<TaxRateMaster>();

    public DbSet<CompanyInfo> CompanyInfos => Set<CompanyInfo>();

    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();

    public DbSet<Menu> Menus => Set<Menu>();

    public DbSet<OrderSlip> OrderSlips => Set<OrderSlip>();

    public DbSet<BillingTaxUnitInvoice> BillingTaxUnitInvoices => Set<BillingTaxUnitInvoice>();

    public DbSet<BillingTaxUnitSlip> BillingTaxUnitSlips => Set<BillingTaxUnitSlip>();

    public DbSet<SalesTaxUnitInvoice> SalesTaxUnitInvoices => Set<SalesTaxUnitInvoice>();

    public DbSet<SalesTaxUnitSlip> SalesTaxUnitSlips => Set<SalesTaxUnitSlip>();

    public DbSet<SalesTaxUnitLine> SalesTaxUnitLines => Set<SalesTaxUnitLine>();

    public DbSet<ReceiptTaxUnitInvoice> ReceiptTaxUnitInvoices => Set<ReceiptTaxUnitInvoice>();

    public DbSet<ReceiptTaxUnitSlip> ReceiptTaxUnitSlips => Set<ReceiptTaxUnitSlip>();

    public DbSet<DetailInvoice> DetailInvoices => Set<DetailInvoice>();

    public DbSet<DetailReceipt> DetailReceipts => Set<DetailReceipt>();

    public DbSet<DetailInvoiceSalesLine> DetailInvoiceSalesLines => Set<DetailInvoiceSalesLine>();

    public DbSet<MonthlyClosing> MonthlyClosings => Set<MonthlyClosing>();

    public DbSet<SlipNumberSequence> SlipNumberSequences => Set<SlipNumberSequence>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BmcsDbContext).Assembly);
    }

    /// <summary>
    /// サーバのバージョン文字列を取得する。テーブルが未作成でも実行できるため、
    /// 接続確認および層をまたいだ非同期アクセスの動作確認に使う。
    /// </summary>
    public async Task<string> GetServerVersionAsync(CancellationToken cancellationToken = default)
    {
        var versions = await Database
            .SqlQuery<string>($"SELECT @@VERSION AS Value")
            .ToListAsync(cancellationToken);

        return versions.FirstOrDefault() ?? string.Empty;
    }
}
