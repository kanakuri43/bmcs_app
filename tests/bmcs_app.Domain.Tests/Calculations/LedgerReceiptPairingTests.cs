using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Tests.Calculations;

/// <summary>
/// 都度得意先の売上明細行への消込証跡の紐づけのテスト。
/// 表示専用（残高計算には使わない）ため、金額の按分ではなく「どの売上明細行にどの明細入金行が
/// 証跡として結びつくか」だけを検証する。
/// </summary>
public class LedgerReceiptPairingTests
{
    [Fact]
    public void 直接指定は対象の売上明細行に証跡として結びつく()
    {
        var receipt = NewDirect("DRC001", 1, "SAL001", 1);

        var result = LedgerReceiptPairing.Pair([receipt], []);

        var traces = Assert.Single(result);
        Assert.Equal(("SAL001", (short)1), traces.Key);
        Assert.Equal("DRC001", Assert.Single(traces.Value).DetailReceiptNumber);
    }

    [Fact]
    public void 明細請求書指定は連携する全ての売上明細行に証跡として結びつく()
    {
        var receipt = NewViaInvoice("DRC002", 1, "DIV001");
        var links = new[]
        {
            NewLink("DIV001", "SAL010", 1),
            NewLink("DIV001", "SAL010", 2),
        };

        var result = LedgerReceiptPairing.Pair([receipt], links);

        Assert.Equal(2, result.Count);
        Assert.Equal("DRC002", Assert.Single(result[("SAL010", (short)1)]).DetailReceiptNumber);
        Assert.Equal("DRC002", Assert.Single(result[("SAL010", (short)2)]).DetailReceiptNumber);
    }

    [Fact]
    public void 同じ売上明細行への複数の入金は入金日付の古い順に並ぶ()
    {
        var later = NewDirect("DRC020", 1, "SAL099", 1, receiptDate: new DateOnly(2026, 8, 1));
        var earlier = NewDirect("DRC010", 1, "SAL099", 1, receiptDate: new DateOnly(2026, 7, 1));

        var result = LedgerReceiptPairing.Pair([later, earlier], []);

        var traces = result[("SAL099", (short)1)];
        Assert.Equal(["DRC010", "DRC020"], traces.Select(t => t.DetailReceiptNumber));
    }

    [Fact]
    public void 連携する売上明細行が見つからない明細請求書指定は証跡を作らない()
    {
        var receipt = NewViaInvoice("DRC030", 1, "DIV999");

        var result = LedgerReceiptPairing.Pair([receipt], []);

        Assert.Empty(result);
    }

    private static DetailReceipt NewDirect(
        string detailReceiptNumber, short lineNumber, string salesSlipNumber, short salesLineNumber,
        DateOnly? receiptDate = null) => new()
    {
        DetailReceiptNumber = detailReceiptNumber,
        LineNumber = lineNumber,
        ReceiptDate = receiptDate ?? new DateOnly(2026, 7, 1),
        CustomerCode = "CUS003",
        CustomerName = "テスト得意先",
        DepositMethodCode = "CASH",
        ReceiptAmount = 1_000m,
        TargetType = DetailReceiptTargetType.SalesLine,
        TargetSalesSlipNumber = salesSlipNumber,
        TargetSalesLineNumber = salesLineNumber,
        AllocatedAmount = 1_000m,
        FeeAdjustmentAmount = 0m,
        AllocationStatus = AllocationStatus.FullyAllocated,
        CreatedBy = "TEST",
        CreatedAt = DateTime.Now,
        UpdatedBy = "TEST",
        UpdatedAt = DateTime.Now,
    };

    private static DetailReceipt NewViaInvoice(string detailReceiptNumber, short lineNumber, string invoiceNumber) => new()
    {
        DetailReceiptNumber = detailReceiptNumber,
        LineNumber = lineNumber,
        ReceiptDate = new DateOnly(2026, 7, 1),
        CustomerCode = "CUS003",
        CustomerName = "テスト得意先",
        DepositMethodCode = "TRANSFER",
        ReceiptAmount = 1_000m,
        TargetType = DetailReceiptTargetType.DetailInvoice,
        TargetDetailInvoiceNumber = invoiceNumber,
        AllocatedAmount = 1_000m,
        FeeAdjustmentAmount = 0m,
        AllocationStatus = AllocationStatus.FullyAllocated,
        CreatedBy = "TEST",
        CreatedAt = DateTime.Now,
        UpdatedBy = "TEST",
        UpdatedAt = DateTime.Now,
    };

    private static DetailInvoiceSalesLine NewLink(string detailInvoiceNumber, string salesSlipNumber, short salesLineNumber) => new()
    {
        DetailInvoiceNumber = detailInvoiceNumber,
        SalesSlipNumber = salesSlipNumber,
        SalesLineNumber = salesLineNumber,
        CreatedBy = "TEST",
        CreatedAt = DateTime.Now,
        UpdatedBy = "TEST",
        UpdatedAt = DateTime.Now,
    };
}
