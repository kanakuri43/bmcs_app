using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Tests.Calculations;

/// <summary>
/// 得意先元帳のマージ・残高推移のテスト（TODO.md 8-1）。完了条件「残高推移が手計算と一致する」を
/// 開発用DBのseedデータ（scripts/seed_dev_data.sql）と同じ形の3得意先（CUS001/CUS002/CUS003）で
/// 直接検証する。数値は事前に手計算済み（docs/design_document.md 21章）。
/// </summary>
public class CustomerLedgerBuilderTests
{
    private static readonly DateOnly PeriodFrom = new(2026, 7, 1);
    private static readonly DateOnly PeriodTo = new(2026, 8, 31);

    private const string CashMethod = "CASH";
    private const string BankTransferMethod = "TRANSFER";

    private static readonly IReadOnlyList<DepositMethod> TestDepositMethods =
    [
        NewDepositMethod(CashMethod, "現金", requiresBankAccount: false),
        NewDepositMethod(BankTransferMethod, "振込", requiresBankAccount: true),
    ];

    private static DepositMethod NewDepositMethod(string code, string name, bool requiresBankAccount) => new()
    {
        DepositMethodCode = code,
        DepositMethodName = name,
        RequiresBankAccount = requiresBankAccount,
        RequiresBillDueDate = false,
        DisplayOrder = 1,
        CreatedBy = "TEST",
        CreatedAt = DateTime.Now,
        UpdatedBy = "TEST",
        UpdatedAt = DateTime.Now,
    };

    // ---- CUS001（締め・請求単位・締日20・切捨・課税10%） ----

    [Fact]
    public void CUS001_請求単位の残高推移が手計算と一致する()
    {
        var customer = NewCustomer("CUS001", TaxUnit.Invoice, RoundingType.Floor);

        var sales = new[]
        {
            NewSales("SALINV002", 1, new DateOnly(2026, 7, 15), customer, 10_000m,
                billingNumber: "BIL_INV001", billingStatus: BillingLinkStatus.Billed,
                settlementStatus: SettlementStatus.PartiallySettled, settledAmount: 4_000m),
            NewSales("SALINV001", 1, new DateOnly(2026, 8, 10), customer, 5_000m),
        };

        var billings = new[]
        {
            NewBilling("BIL_INV001", customer, new DateOnly(2026, 7, 20), taxAmount: 1_000m, currentBillingAmount: 11_000m),
        };

        var receipts = new[]
        {
            NewReceipt("RCP_INV001", 1, new DateOnly(2026, 7, 25), customer, BankTransferMethod, 4_000m),
            NewReceipt("RCP_INV002", 1, new DateOnly(2026, 8, 5), customer, CashMethod, 3_000m),
        };

        var input = new CustomerLedgerInput(
            customer, PeriodFrom, PeriodTo, sales, billings, receipts, [], [], TestDepositMethods);

        var result = CustomerLedgerBuilder.Build(input);

        Assert.Equal(0m, result.OpeningBalance);
        Assert.Equal(15_000m, result.SalesTotal);
        Assert.Equal(1_500m, result.TaxTotal);
        Assert.Equal(7_000m, result.ReceiptTotal);
        Assert.Equal(9_500m, result.ClosingBalance);
        Assert.True(result.IsBalanced);

        // 残高の遷移そのものを1行ずつ検証する（10,000 → 11,000 → 7,000 → 4,000 → 9,000 → 9,500）。
        var balances = result.Entries.Select(e => e.Balance).ToList();
        Assert.Equal([0m, 10_000m, 11_000m, 7_000m, 4_000m, 9_000m, 9_500m], balances);

        // 請求締め済みの消費税（確定値）は billing.CurrentBillingAmount と一致する。
        Assert.Equal(11_000m, result.Entries[2].Balance);

        // 未締め区間の仮計算消費税は期末日付、切捨・10%で 500 円。
        var provisionalTax = result.Entries[^1];
        Assert.Equal(LedgerEntryKind.ConsumptionTax, provisionalTax.Kind);
        Assert.Equal(PeriodTo, provisionalTax.EntryDate);
        Assert.Equal(500m, provisionalTax.DebitAmount);
    }

    [Fact]
    public void CUS001_期間を切り替えても繰越が一貫する()
    {
        var customer = NewCustomer("CUS001", TaxUnit.Invoice, RoundingType.Floor);
        var sales = new[]
        {
            NewSales("SALINV002", 1, new DateOnly(2026, 7, 15), customer, 10_000m,
                billingNumber: "BIL_INV001", billingStatus: BillingLinkStatus.Billed,
                settlementStatus: SettlementStatus.PartiallySettled, settledAmount: 4_000m),
            NewSales("SALINV001", 1, new DateOnly(2026, 8, 10), customer, 5_000m),
        };
        var billings = new[]
        {
            NewBilling("BIL_INV001", customer, new DateOnly(2026, 7, 20), taxAmount: 1_000m, currentBillingAmount: 11_000m),
        };
        var receipts = new[]
        {
            NewReceipt("RCP_INV001", 1, new DateOnly(2026, 7, 25), customer, BankTransferMethod, 4_000m),
            NewReceipt("RCP_INV002", 1, new DateOnly(2026, 8, 5), customer, CashMethod, 3_000m),
        };

        var fullPeriod = CustomerLedgerBuilder.Build(new CustomerLedgerInput(
            customer, PeriodFrom, PeriodTo, sales, billings, receipts, [], [], TestDepositMethods));

        var augustOnly = CustomerLedgerBuilder.Build(new CustomerLedgerInput(
            customer, new DateOnly(2026, 8, 1), PeriodTo, sales, billings, receipts, [], [], TestDepositMethods));

        // 7月末残高 7,000 が8月分の繰越として引き継がれる。
        Assert.Equal(7_000m, augustOnly.OpeningBalance);
        Assert.Equal(fullPeriod.ClosingBalance, augustOnly.ClosingBalance);
        Assert.True(augustOnly.IsBalanced);
    }

    // ---- CUS002（締め・伝票単位・締日99末日・四捨五入・課税10%） ----

    [Fact]
    public void CUS002_伝票単位の残高推移が手計算と一致する()
    {
        var customer = NewCustomer("CUS002", TaxUnit.Slip, RoundingType.RoundHalfUp);

        var sales = new[]
        {
            NewSales("SALSLP002", 1, new DateOnly(2026, 7, 10), customer, 8_000m,
                slipTaxAmount: 800m, billingNumber: "BIL_SLP001", billingStatus: BillingLinkStatus.Billed,
                settlementStatus: SettlementStatus.FullySettled, settledAmount: 8_000m),
            NewSales("SALSLP001", 1, new DateOnly(2026, 8, 11), customer, 3_000m, slipTaxAmount: 300m),
        };

        var receipts = new[]
        {
            NewReceipt("RCP_SLP001", 1, new DateOnly(2026, 8, 1), customer, BankTransferMethod, 8_000m),
            NewReceipt("RCP_SLP001", 2, new DateOnly(2026, 8, 1), customer, CashMethod, 2_000m),
        };

        var input = new CustomerLedgerInput(
            customer, PeriodFrom, PeriodTo, sales, [], receipts, [], [], TestDepositMethods);

        var result = CustomerLedgerBuilder.Build(input);

        Assert.Equal(0m, result.OpeningBalance);
        Assert.Equal(11_000m, result.SalesTotal);
        Assert.Equal(1_100m, result.TaxTotal);
        Assert.Equal(10_000m, result.ReceiptTotal);
        Assert.Equal(2_100m, result.ClosingBalance);
        Assert.True(result.IsBalanced);

        // 8,000 → 8,800 → 800 → -1,200(過入金) → 1,800 → 2,100
        var balances = result.Entries.Select(e => e.Balance).ToList();
        Assert.Equal([0m, 8_000m, 8_800m, 800m, -1_200m, 1_800m, 2_100m], balances);

        // 過入金による一時的なマイナス残高を正常系として許容する（D-2/前受・過入金）。
        Assert.Contains(result.Entries, e => e.Balance < 0m);
    }

    [Fact]
    public void CUS002_伝票単位の消費税は伝票直下に1回だけ挿入される()
    {
        var customer = NewCustomer("CUS002", TaxUnit.Slip, RoundingType.RoundHalfUp);

        // 同日に2伝票。各伝票のslip_tax_amountが自分の伝票の直下に来ることを確認する。
        var sales = new[]
        {
            NewSales("SALA", 1, new DateOnly(2026, 7, 10), customer, 1_000m, slipTaxAmount: 100m),
            NewSales("SALB", 1, new DateOnly(2026, 7, 10), customer, 2_000m, slipTaxAmount: 200m),
        };

        var result = CustomerLedgerBuilder.Build(
            new CustomerLedgerInput(customer, PeriodFrom, PeriodTo, sales, [], [], [], [], TestDepositMethods));

        var kinds = result.Entries.Skip(1).Select(e => (e.Kind, e.SalesSlipNumber, e.DebitAmount)).ToList();
        Assert.Equal(
            [
                (LedgerEntryKind.Sales, "SALA", 1_000m),
                (LedgerEntryKind.ConsumptionTax, "SALA", (decimal?)100m),
                (LedgerEntryKind.Sales, "SALB", 2_000m),
                (LedgerEntryKind.ConsumptionTax, "SALB", (decimal?)200m),
            ],
            kinds);
    }

    // ---- CUS003（都度・内税明細単位・切上・軽減8%） ----

    [Fact]
    public void CUS003_内税明細単位の残高推移が手計算と一致する()
    {
        var customer = NewCustomer("CUS003", TaxUnit.Line, RoundingType.Ceiling);

        var sales = new[]
        {
            NewSales("SALLIN002", 1, new DateOnly(2026, 7, 20), customer, 8_250m,
                billingStatus: BillingLinkStatus.Billed, settlementStatus: SettlementStatus.FullySettled, settledAmount: 8_250m),
            NewSales("SALLIN003", 1, new DateOnly(2026, 7, 25), customer, 2_750m,
                settlementStatus: SettlementStatus.FullySettled, settledAmount: 2_750m),
            NewSales("SALLIN001", 1, new DateOnly(2026, 8, 12), customer, 11_000m),
            NewSales("SALLIN004", 1, new DateOnly(2026, 8, 13), customer, -1_100m, slipType: SlipType.Return),
        };

        var detailReceipts = new[]
        {
            NewDetailReceiptDirect("DRC001", 1, new DateOnly(2026, 7, 25), customer, CashMethod,
                2_750m, "SALLIN003", 1),
            NewDetailReceiptViaInvoice("DRC002", 1, new DateOnly(2026, 7, 22), customer, BankTransferMethod,
                8_250m, "DIV001"),
        };

        var invoiceLinks = new[]
        {
            NewInvoiceLink("DIV001", "SALLIN002", 1),
        };

        var input = new CustomerLedgerInput(
            customer, PeriodFrom, PeriodTo, sales, [], [], detailReceipts, invoiceLinks, TestDepositMethods);

        var result = CustomerLedgerBuilder.Build(input);

        Assert.Equal(0m, result.OpeningBalance);
        Assert.Equal(20_900m, result.SalesTotal);
        Assert.Equal(0m, result.TaxTotal); // 内税なので消費税行は作らない
        Assert.Equal(11_000m, result.ReceiptTotal);
        Assert.Equal(9_900m, result.ClosingBalance);
        Assert.True(result.IsBalanced);

        var balances = result.Entries.Select(e => e.Balance).ToList();
        Assert.Equal([0m, 8_250m, 0m, 2_750m, 0m, 11_000m, 9_900m], balances);

        // SALLIN002の売上行には、明細請求書DIV001経由の入金(DRC002)が証跡として同居する（金額は載せない）。
        var salesLine002 = result.Entries.Single(e => e.SalesSlipNumber == "SALLIN002");
        Assert.Equal("DRC002", salesLine002.ReceiptSlipNumber);
        Assert.Equal(new DateOnly(2026, 7, 22), salesLine002.ReceiptDate);
        Assert.Null(salesLine002.ReceiptAmount);
    }

    [Fact]
    public void CUS003_複数の入金が同じ売上行に紐づく場合は継続行で表示され残高は独立行でのみ変化する()
    {
        var customer = NewCustomer("CUS003", TaxUnit.Line, RoundingType.Ceiling);

        var sales = new[]
        {
            NewSales("SALX", 1, new DateOnly(2026, 7, 1), customer, 5_000m,
                settlementStatus: SettlementStatus.FullySettled, settledAmount: 5_000m),
        };

        var detailReceipts = new[]
        {
            NewDetailReceiptDirect("DRC101", 1, new DateOnly(2026, 7, 5), customer, CashMethod,
                2_000m, "SALX", 1),
            NewDetailReceiptDirect("DRC102", 1, new DateOnly(2026, 7, 10), customer, BankTransferMethod,
                3_000m, "SALX", 1),
        };

        var result = CustomerLedgerBuilder.Build(new CustomerLedgerInput(
            customer, PeriodFrom, PeriodTo, sales, [], [], detailReceipts, [], TestDepositMethods));

        // 売上行(証跡=DRC101) → 継続行(証跡=DRC102、売上側は空欄) → DRC101入金 → DRC102入金 の4行構成。
        var salesRows = result.Entries.Where(e => e.EntryDate == new DateOnly(2026, 7, 1) && e.Kind == LedgerEntryKind.Sales).ToList();
        Assert.Equal(2, salesRows.Count);
        Assert.Equal(5_000m, salesRows[0].DebitAmount);
        Assert.Equal("DRC101", salesRows[0].ReceiptSlipNumber);
        Assert.Null(salesRows[1].DebitAmount);
        Assert.Null(salesRows[1].SalesSlipNumber);
        Assert.Equal("DRC102", salesRows[1].ReceiptSlipNumber);
        Assert.Null(salesRows[0].ReceiptAmount);
        Assert.Null(salesRows[1].ReceiptAmount);

        // 残高は 5,000 → (証跡行×2は変化なし) → 3,000 → 0 の独立入金行でのみ変化する。
        var balances = result.Entries.Select(e => e.Balance).ToList();
        Assert.Equal([0m, 5_000m, 5_000m, 3_000m, 0m], balances);
    }

    [Fact]
    public void CUS003_明細請求書に連携する売上明細行が見つからなくても例外にならず入金は独立行で計上される()
    {
        var customer = NewCustomer("CUS003", TaxUnit.Line, RoundingType.Ceiling);

        var sales = Array.Empty<Sales>();
        var detailReceipts = new[]
        {
            // 取消済み明細請求書を指す入金（連携行が既に削除されているデータ異常のケース）。
            NewDetailReceiptViaInvoice("DRC999", 1, new DateOnly(2026, 7, 1), customer, CashMethod, 1_000m, "DIV999"),
        };

        var result = CustomerLedgerBuilder.Build(new CustomerLedgerInput(
            customer, PeriodFrom, PeriodTo, sales, [], [], detailReceipts, [], TestDepositMethods));

        Assert.Equal(-1_000m, result.ClosingBalance);
        Assert.True(result.IsBalanced);
        var receiptRow = Assert.Single(result.Entries, e => e.Kind == LedgerEntryKind.Receipt);
        Assert.Equal(1_000m, receiptRow.ReceiptAmount);
    }

    // ---- 汎用の境界値テスト ----

    [Fact]
    public void 期間内にエントリが0件でも繰越行だけ返り残高は変化しない()
    {
        var customer = NewCustomer("CUS999", TaxUnit.Invoice, RoundingType.Floor);

        var result = CustomerLedgerBuilder.Build(
            new CustomerLedgerInput(customer, PeriodFrom, PeriodTo, [], [], [], [], [], TestDepositMethods));

        var entry = Assert.Single(result.Entries);
        Assert.Equal(LedgerEntryKind.OpeningBalance, entry.Kind);
        Assert.Equal(0m, result.OpeningBalance);
        Assert.Equal(0m, result.ClosingBalance);
    }

    [Fact]
    public void 開始日が終了日より後なら例外()
    {
        var customer = NewCustomer("CUS999", TaxUnit.Invoice, RoundingType.Floor);

        Assert.Throws<ArgumentException>(() => CustomerLedgerBuilder.Build(
            new CustomerLedgerInput(customer, PeriodTo, PeriodFrom, [], [], [], [], [], TestDepositMethods)));
    }

    [Fact]
    public void 請求単位の未締め区間は複数税率にまたがっても税率ごとに1回だけ丸められる()
    {
        var customer = NewCustomer("CUS998", TaxUnit.Invoice, RoundingType.Floor);
        var sales = new[]
        {
            // 標準10%の2行は同じ(区分,税率)グループなので合算してから1回だけ丸める。
            // 行ごとに丸めると floor(100.5)+floor(100.5)=100+100=200 になるが、
            // 合算してから丸めると floor(201.0)=201 になり、この違いで「1回だけ丸め」を検証する。
            NewSales("SALQ", 1, new DateOnly(2026, 7, 1), customer, 1_005m,
                taxCategory: TaxCategory.Standard, taxRate: 10m),
            NewSales("SALQ", 2, new DateOnly(2026, 7, 1), customer, 1_005m,
                taxCategory: TaxCategory.Standard, taxRate: 10m),
            NewSales("SALQ", 3, new DateOnly(2026, 7, 1), customer, 1_005m,
                taxCategory: TaxCategory.Reduced, taxRate: 8m),
            NewSales("SALQ", 4, new DateOnly(2026, 7, 1), customer, 500m,
                taxCategory: TaxCategory.TaxExempt, taxRate: 0m),
        };

        var tax = CustomerLedgerBuilder.ProvisionalTaxAsOf(customer, sales, new DateOnly(2026, 7, 1));

        // 標準(2,010円 × 10% = 201.0 → 切捨 201) + 軽減(1,005円 × 8% = 80.4 → 切捨 80) + 非課税(0) = 281
        Assert.Equal(281m, tax);
    }

    // ---- テストデータ組み立て用ヘルパー ----

    private static Customer NewCustomer(string code, TaxUnit taxUnit, RoundingType roundingType) => new()
    {
        CustomerCode = code,
        CustomerName = $"テスト得意先{code}",
        ClosingDay = taxUnit == TaxUnit.Line ? (byte)0 : (byte)20,
        TaxUnit = taxUnit,
        RoundingType = roundingType,
        BillingCustomerCode = code,
        PrintRepresentativeFlag = false,
        CreatedBy = "TEST",
        CreatedAt = DateTime.Now,
        UpdatedBy = "TEST",
        UpdatedAt = DateTime.Now,
    };

    private static Sales NewSales(
        string slipNumber, short lineNumber, DateOnly slipDate, Customer customer, decimal amount,
        SlipType slipType = SlipType.Sales,
        TaxCategory taxCategory = TaxCategory.Standard, decimal taxRate = 10m,
        decimal? slipTaxAmount = null, decimal? taxAmount = null,
        string? billingNumber = null, BillingLinkStatus billingStatus = BillingLinkStatus.Unbilled,
        SettlementStatus settlementStatus = SettlementStatus.Unsettled, decimal settledAmount = 0m) => new()
    {
        SalesSlipNumber = slipNumber,
        LineNumber = lineNumber,
        SlipDate = slipDate,
        CustomerCode = customer.CustomerCode,
        TaxUnit = customer.TaxUnit,
        CustomerName = customer.CustomerName,
        SlipType = slipType,
        ProductCode = "PRD001",
        ProductName = "テスト商品",
        Quantity = 1m,
        UnitPrice = amount,
        Amount = amount,
        CostPrice = 0m,
        TaxCategory = taxCategory,
        TaxRate = taxRate,
        SlipTaxAmount = slipTaxAmount,
        TaxAmount = taxAmount,
        DeliveryNoteIssueCount = 0,
        BillingStatus = billingStatus,
        SettlementStatus = settlementStatus,
        SettledAmount = settledAmount,
        BillingNumber = billingNumber,
        CreatedBy = "TEST",
        CreatedAt = DateTime.Now,
        UpdatedBy = "TEST",
        UpdatedAt = DateTime.Now,
    };

    private static Billing NewBilling(
        string billingNumber, Customer customer, DateOnly billingDate, decimal taxAmount, decimal currentBillingAmount) => new()
    {
        BillingNumber = billingNumber,
        CustomerCode = customer.CustomerCode,
        TaxUnit = customer.TaxUnit,
        CustomerName = customer.CustomerName,
        BillingDate = billingDate,
        ClosingYearMonth = billingDate.ToString("yyyyMM"),
        PreviousBalance = 0m,
        ReceiptAmount = 0m,
        SalesAmount = 0m,
        TaxAmount = taxAmount,
        CurrentBillingAmount = currentBillingAmount,
        StandardRateTaxableAmount = 0m,
        StandardRateTaxAmount = 0m,
        ReducedRateTaxableAmount = 0m,
        ReducedRateTaxAmount = 0m,
        TaxExemptAmount = 0m,
        BillingStatus = BillingStatus.Confirmed,
        ConfirmedAt = DateTime.Now,
        ConfirmedBy = "TEST",
        CreatedBy = "TEST",
        CreatedAt = DateTime.Now,
        UpdatedBy = "TEST",
        UpdatedAt = DateTime.Now,
    };

    private static Receipt NewReceipt(
        string receiptSlipNumber, short lineNumber, DateOnly receiptDate, Customer customer,
        string depositMethodCode, decimal amount) => new()
    {
        ReceiptSlipNumber = receiptSlipNumber,
        LineNumber = lineNumber,
        ReceiptDate = receiptDate,
        CustomerCode = customer.CustomerCode,
        TaxUnit = customer.TaxUnit,
        CustomerName = customer.CustomerName,
        DepositMethodCode = depositMethodCode,
        BankAccountCode = depositMethodCode == BankTransferMethod ? "BNK001" : null,
        Amount = amount,
        AllocationStatus = AllocationStatus.FullyAllocated,
        CreatedBy = "TEST",
        CreatedAt = DateTime.Now,
        UpdatedBy = "TEST",
        UpdatedAt = DateTime.Now,
    };

    private static DetailReceipt NewDetailReceiptDirect(
        string detailReceiptNumber, short lineNumber, DateOnly receiptDate, Customer customer,
        string depositMethodCode, decimal amount, string targetSalesSlipNumber, short targetSalesLineNumber) => new()
    {
        DetailReceiptNumber = detailReceiptNumber,
        LineNumber = lineNumber,
        ReceiptDate = receiptDate,
        CustomerCode = customer.CustomerCode,
        CustomerName = customer.CustomerName,
        DepositMethodCode = depositMethodCode,
        BankAccountCode = depositMethodCode == BankTransferMethod ? "BNK001" : null,
        ReceiptAmount = amount,
        TargetType = DetailReceiptTargetType.SalesLine,
        TargetSalesSlipNumber = targetSalesSlipNumber,
        TargetSalesLineNumber = targetSalesLineNumber,
        TargetDetailInvoiceNumber = null,
        AllocatedAmount = amount,
        FeeAdjustmentAmount = 0m,
        AllocationStatus = AllocationStatus.FullyAllocated,
        CreatedBy = "TEST",
        CreatedAt = DateTime.Now,
        UpdatedBy = "TEST",
        UpdatedAt = DateTime.Now,
    };

    private static DetailReceipt NewDetailReceiptViaInvoice(
        string detailReceiptNumber, short lineNumber, DateOnly receiptDate, Customer customer,
        string depositMethodCode, decimal amount, string targetDetailInvoiceNumber) => new()
    {
        DetailReceiptNumber = detailReceiptNumber,
        LineNumber = lineNumber,
        ReceiptDate = receiptDate,
        CustomerCode = customer.CustomerCode,
        CustomerName = customer.CustomerName,
        DepositMethodCode = depositMethodCode,
        BankAccountCode = depositMethodCode == BankTransferMethod ? "BNK001" : null,
        ReceiptAmount = amount,
        TargetType = DetailReceiptTargetType.DetailInvoice,
        TargetSalesSlipNumber = null,
        TargetSalesLineNumber = null,
        TargetDetailInvoiceNumber = targetDetailInvoiceNumber,
        AllocatedAmount = amount,
        FeeAdjustmentAmount = 0m,
        AllocationStatus = AllocationStatus.FullyAllocated,
        CreatedBy = "TEST",
        CreatedAt = DateTime.Now,
        UpdatedBy = "TEST",
        UpdatedAt = DateTime.Now,
    };

    private static DetailInvoiceSalesLine NewInvoiceLink(string detailInvoiceNumber, string salesSlipNumber, short salesLineNumber) => new()
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
