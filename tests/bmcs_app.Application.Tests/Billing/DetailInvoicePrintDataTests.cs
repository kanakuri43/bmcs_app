using bmcs_app.Application.Billing;
using bmcs_app.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace bmcs_app.Application.Tests.Billing;

/// <summary>
/// 明細請求書の印刷データ取得（<see cref="DetailInvoiceService.GetPrintDataAsync"/>、TODO.md 10-5）
/// の結合テスト。読み取り専用（保存しない）のため、専用のテスト得意先を新設せず、既存のseedデータ
/// （`scripts/seed_dev_data.sql`）に対する回帰検知テストとして実装する
/// （<see cref="InvoiceServiceTests"/>と同じ方針）。
/// </summary>
public class DetailInvoicePrintDataTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    [Fact]
    public async Task 発行済みの明細請求書は税率別内訳の金額がヘッダーの確定値と一致し税率ラベルを明細行から拝借する()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = Resolve(scope);

        var data = await service.GetPrintDataAsync("DIV001");

        Assert.Equal("石山小学校5年1組 佐藤先生", data.AddresseeName);
        Assert.Equal(7638.00m, data.TaxExcludedTotal);
        Assert.Equal(612.00m, data.TaxTotal);
        Assert.Equal(8250.00m, data.GrandTotal);

        var bucket = Assert.Single(data.TaxBreakdowns);
        Assert.Equal(TaxCategory.Reduced, bucket.TaxCategory);
        Assert.Equal(8m, bucket.TaxRate);
        Assert.Equal(7638.00m, bucket.TaxableAmount);
        Assert.Equal(612.00m, bucket.TaxAmount);

        var line = Assert.Single(data.Lines);
        Assert.Equal("SALLIN002", line.SalesSlipNumber);
    }

    [Fact]
    public async Task 取消済みの明細請求書は明細0件でヘッダーの確定金額のみ返り税率は0でフォールバックする()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = Resolve(scope);

        // 取消で連携行（detail_invoice_sales_line）が物理削除されるため、明細は組み立てられない
        // （`docs/design_document.md` 12-1節の非破壊ヘッダー方式）。
        var data = await service.GetPrintDataAsync("DIV002");

        Assert.Empty(data.Lines);
        Assert.Equal(3000.00m, data.TaxExcludedTotal);
        Assert.Equal(240.00m, data.TaxTotal);
        Assert.Equal(3240.00m, data.GrandTotal);

        var bucket = Assert.Single(data.TaxBreakdowns);
        Assert.Equal(TaxCategory.Reduced, bucket.TaxCategory);
        Assert.Equal(0m, bucket.TaxRate);
    }

    [Fact]
    public async Task 存在しない明細請求書番号は例外になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = Resolve(scope);

        await Assert.ThrowsAsync<DetailInvoiceException>(() => service.GetPrintDataAsync("__NOT_EXIST__"));
    }

    private static DetailInvoiceService Resolve(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<DetailInvoiceService>();
}
