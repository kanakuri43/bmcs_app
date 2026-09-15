using bmcs_app.Application.Billing;
using bmcs_app.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace bmcs_app.Application.Tests.Billing;

/// <summary>
/// 請求書の印刷データ取得（TODO.md 10-5）の結合テスト。開発用ライブDB（172.16.3.171）に対して
/// 実行する。読み取り専用（保存しない）のため、専用のテスト得意先を新設せず、既存のseedデータ
/// （`scripts/seed_dev_data.sql`）に対する回帰検知テストとして実装する
/// （`CustomerLedgerQueryServiceTests`と同じ方針）。
/// </summary>
public class InvoiceServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    [Fact]
    public async Task 請求単位の請求書は税率別内訳の金額がヘッダーの確定値と一致し税率ラベルを明細行から拝借する()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = Resolve(scope);

        var data = await service.GetByNumberAsync("BIL_INV001");

        Assert.NotNull(data);
        Assert.Equal(TaxUnit.Invoice, data!.TaxUnit);
        Assert.Equal(10000.00m, data.SalesAmount);
        Assert.Equal(1000.00m, data.TaxTotal);
        Assert.Equal(11000.00m, data.CurrentBillingAmount);

        var bucket = Assert.Single(data.TaxBreakdowns);
        Assert.Equal(TaxCategory.Standard, bucket.TaxCategory);
        Assert.Equal(10m, bucket.TaxRate);
        Assert.Equal(10000.00m, bucket.TaxableAmount);
        Assert.Equal(1000.00m, bucket.TaxAmount);

        // 明細は sales.billing_number = 'BIL_INV001' の行（SALINV002）から組み立てる。
        var line = Assert.Single(data.Lines);
        Assert.Equal("SALINV002", line.SalesSlipNumber);
    }

    [Fact]
    public async Task 伝票単位の請求書も同様に取得できる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = Resolve(scope);

        var data = await service.GetByNumberAsync("BIL_SLP001");

        Assert.NotNull(data);
        Assert.Equal(TaxUnit.Slip, data!.TaxUnit);
        Assert.Equal(8000.00m, data.SalesAmount);
        Assert.Equal(800.00m, data.TaxTotal);
        Assert.Equal(8800.00m, data.CurrentBillingAmount);
        Assert.Single(data.Lines, l => l.SalesSlipNumber == "SALSLP002");
    }

    [Fact]
    public async Task 解除済みの請求書は明細0件でヘッダーの確定金額のみ返る()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = Resolve(scope);

        // 締め解除で sales.billing_number は NULL に戻るため、解除済みの請求番号を指定すると
        // 明細は組み立てられない（`docs/design_document.md` 12-1節と対称の非破壊ヘッダー方式）。
        var data = await service.GetByNumberAsync("BIL_INV002");

        Assert.NotNull(data);
        Assert.Empty(data!.Lines);
        Assert.Equal(5000.00m, data.SalesAmount);
        Assert.Equal(500.00m, data.TaxTotal);
        Assert.Equal(5500.00m, data.CurrentBillingAmount);
    }

    [Fact]
    public async Task 存在しない請求番号はnullを返す()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = Resolve(scope);

        var data = await service.GetByNumberAsync("__NOT_EXIST__");

        Assert.Null(data);
    }

    private static InvoiceService Resolve(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<InvoiceService>();
}
