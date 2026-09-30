using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Calculations;

/// <summary>
/// 得意先元帳のマージ・時系列化・残高推移の算出（TODO.md 8-1）。SQLビュー・GROUP BY は使わず、
/// 呼び出し元（<c>CustomerLedgerQueryService</c>）がロードした得意先の全期間の売上・入金・請求を
/// アプリ側でマージする（docs/architecture.md 7章・10章）。
///
/// 残高は税込で扱う。<see cref="TaxUnit.Invoice"/>（請求単位）は伝票時点で消費税額を持てない
/// （CHECK制約 <c>CK_sales_tax_amount_by_tax_unit</c>）ため、消費税を独立した明細行として
/// 時系列に挿入することで税込残高を成立させる（TODO.md 8-1 D-1）。
///
/// 入金の残高影響は常に入金日付の独立行（<see cref="LedgerEntryKind.Receipt"/>）でのみ発生させる。
/// 都度得意先の売上行に同居する入金情報は消込の証跡（<see cref="LedgerReceiptPairing"/>）であり、
/// 金額を持たないため残高に影響しない。売上と入金が月をまたいでも各月末の売掛残高が
/// 日付どおり正確になり、TODO.md 9-1（月次締め）の暦月末残高と矛盾しない（D-2）。
///
/// 繰越は全期間積み上げで算出する（<c>monthly_closing</c> / <c>billing.previous_balance</c> を
/// 起点に使わない。M-11「都度集計する・残高キャッシュ列は持たない」と整合させるため。D-4）。
///
/// 請求集約元（<c>!Customer.IsBillingRoot</c>）は取引履歴のみモードへ分岐する（<see cref="BuildTransactionHistoryOnly"/>、
/// TODO.md 12-E）。請求集約先はこのクラスの通常経路のまま無改修で、呼び出し元がグループ全体の
/// <c>Sales</c>／<c>Receipts</c>を渡すだけで残高がグループ合算になる（<c>Customer.CustomerCode</c>は
/// このクラスのどこでも参照しないため）。docs/design_document.md 28章参照。
/// </summary>
public static class CustomerLedgerBuilder
{
    private const int KindOrderSales = 1;
    private const int KindOrderTax = 2;
    private const int KindOrderReceipt = 3;
    private const string ProvisionalTaxGroupKey = "~";

    public static CustomerLedgerResult Build(CustomerLedgerInput input)
    {
        if (input.PeriodFrom > input.PeriodTo)
        {
            throw new ArgumentException("期間の開始日は終了日以前にしてください。", nameof(input));
        }

        if (!input.Customer.IsBillingRoot)
        {
            return BuildTransactionHistoryOnly(input);
        }

        var customer = input.Customer;
        var dayBeforeFrom = input.PeriodFrom.AddDays(-1);
        var depositMethodNameByCode = input.DepositMethods.ToDictionary(m => m.DepositMethodCode, m => m.DepositMethodName);

        var entries = new List<SortableEntry>();

        AddSalesEntries(entries, input, customer, depositMethodNameByCode);

        if (customer.TaxUnit == TaxUnit.Slip)
        {
            AddSlipTaxEntries(entries, input.SalesLines);
        }
        else if (customer.TaxUnit == TaxUnit.Invoice)
        {
            AddInvoiceTaxEntries(entries, input, customer, dayBeforeFrom);
        }

        AddReceiptEntries(entries, input, customer, depositMethodNameByCode);

        entries.Sort((a, b) => a.Key.CompareTo(b.Key));

        var provisionalTaxBeforeFrom = customer.TaxUnit == TaxUnit.Invoice
            ? ProvisionalTaxAsOf(customer, input.SalesLines, dayBeforeFrom)
            : 0m;

        var openingBalance = entries
            .Where(e => e.Key.Date < input.PeriodFrom)
            .Sum(e => e.Entry.BalanceDelta)
            + provisionalTaxBeforeFrom;

        var periodEntries = entries
            .Where(e => e.Key.Date >= input.PeriodFrom && e.Key.Date <= input.PeriodTo)
            .ToList();

        var salesTotal = periodEntries
            .Where(e => e.Entry.Kind == LedgerEntryKind.Sales)
            .Sum(e => e.Entry.DebitAmount ?? 0m);
        var taxTotal = periodEntries
            .Where(e => e.Entry.Kind == LedgerEntryKind.ConsumptionTax)
            .Sum(e => e.Entry.DebitAmount ?? 0m);
        var receiptTotal = periodEntries
            .Where(e => e.Entry.Kind == LedgerEntryKind.Receipt)
            .Sum(e => e.Entry.ReceiptAmount ?? 0m);

        var resultEntries = new List<CustomerLedgerEntry>
        {
            new()
            {
                Kind = LedgerEntryKind.OpeningBalance,
                EntryDate = dayBeforeFrom,
                Balance = openingBalance,
            },
        };

        var runningBalance = openingBalance;
        foreach (var item in periodEntries)
        {
            runningBalance += item.Entry.BalanceDelta;
            resultEntries.Add(item.Entry with { Balance = runningBalance });
        }

        return new CustomerLedgerResult(resultEntries, openingBalance, salesTotal, taxTotal, receiptTotal, runningBalance);
    }

    /// <summary>
    /// 請求集約元（<c>!Customer.IsBillingRoot</c>）の取引履歴のみモード（TODO.md 12-E、
    /// docs/design_document.md 28-2節 #6）。売掛残高・請求・入金は請求集約先に集約されるため、
    /// この得意先自身の売上行のみを組み立てて返す（消費税行・入金行・前月繰越行・残高累積は行わない）。
    /// 請求集約元は必ず締め得意先（<see cref="TaxUnit.Invoice"/>／<see cref="TaxUnit.Slip"/>。
    /// 業務ルール・DB CHECK制約）なので、<see cref="AddSalesEntries"/> 内の
    /// <see cref="TaxUnit.Line"/> 用の消込証跡ペアリングには到達しない。
    /// </summary>
    private static CustomerLedgerResult BuildTransactionHistoryOnly(CustomerLedgerInput input)
    {
        var entries = new List<SortableEntry>();
        AddSalesEntries(entries, input, input.Customer, EmptyDepositMethodNamesByCode);
        entries.Sort((a, b) => a.Key.CompareTo(b.Key));

        var periodEntries = entries
            .Where(e => e.Key.Date >= input.PeriodFrom && e.Key.Date <= input.PeriodTo)
            .Select(e => e.Entry)
            .ToList();

        var salesTotal = periodEntries
            .Where(e => e.Kind == LedgerEntryKind.Sales)
            .Sum(e => e.DebitAmount ?? 0m);

        return new CustomerLedgerResult(
            periodEntries, OpeningBalance: 0m, salesTotal, TaxTotal: 0m, ReceiptTotal: 0m, ClosingBalance: 0m,
            IsTransactionHistoryOnly: true);
    }

    /// <summary>
    /// <see cref="TaxUnit.Invoice"/> の未締め区間（<c>billing_number IS NULL</c> かつ
    /// <c>slip_date &lt;= asOf</c>）の消費税を仮計算する。6-1 の <c>BillingClosingService</c> と
    /// 同じ「未請求の全行を1グループとして税率ごとに1回丸める」方式
    /// （<see cref="ConsumptionTaxCalculator.CalculateExternalTaxBuckets"/>）を使うため、
    /// 締めた瞬間の <c>billing.tax_amount</c> と一致する。<see cref="TaxUnit.Slip"/>／
    /// <see cref="TaxUnit.Line"/> は常に 0。
    /// </summary>
    public static decimal ProvisionalTaxAsOf(Customer customer, IReadOnlyList<Sales> salesLines, DateOnly asOf)
        => ProvisionalTaxBucketsAsOf(customer, salesLines, asOf).Sum(b => b.TaxAmount);

    /// <summary>
    /// <see cref="ProvisionalTaxAsOf"/> の税率別内訳版（月次締めの税率別カラム用。TODO.md 9-1）。
    /// <see cref="TaxUnit.Invoice"/> 以外は空。
    /// </summary>
    public static IReadOnlyList<TaxRateBucket> ProvisionalTaxBucketsAsOf(
        Customer customer, IReadOnlyList<Sales> salesLines, DateOnly asOf)
    {
        if (customer.TaxUnit != TaxUnit.Invoice)
        {
            return [];
        }

        var unbilledLines = salesLines
            .Where(s => s.BillingNumber is null && s.SlipDate <= asOf)
            .Select(s => new TaxLine(s.TaxCategory, s.TaxRate, s.Amount));

        return ConsumptionTaxCalculator.CalculateExternalTaxBuckets(unbilledLines, customer.RoundingType);
    }

    private static void AddSalesEntries(
        List<SortableEntry> entries, CustomerLedgerInput input, Customer customer,
        IReadOnlyDictionary<string, string> depositMethodNameByCode)
    {
        var traceBySalesLine = customer.TaxUnit == TaxUnit.Line
            ? LedgerReceiptPairing.Pair(input.DetailReceiptLines, input.DetailInvoiceLinks)
            : EmptyTrace;

        foreach (var sales in input.SalesLines)
        {
            var traces = traceBySalesLine.TryGetValue((sales.SalesSlipNumber, sales.LineNumber), out var list)
                ? list
                : [];
            var firstTrace = traces.Count > 0 ? traces[0] : null;

            entries.Add(new SortableEntry(
                new SortKey(sales.SlipDate, KindOrderSales, sales.SalesSlipNumber, sales.LineNumber, 0),
                new CustomerLedgerEntry
                {
                    Kind = LedgerEntryKind.Sales,
                    EntryDate = sales.SlipDate,
                    SlipType = sales.SlipType,
                    SalesSlipNumber = sales.SalesSlipNumber,
                    SalesLineNumber = sales.LineNumber,
                    ProductCode = sales.ProductCode,
                    ProductName = sales.ProductName,
                    CustomerCode = sales.CustomerCode,
                    CustomerName = sales.CustomerName,
                    TaxCategory = sales.TaxCategory,
                    TaxRate = sales.TaxRate,
                    Quantity = sales.Quantity,
                    UnitPrice = sales.UnitPrice,
                    DebitAmount = sales.Amount,
                    IncludedTaxAmount = sales.TaxAmount,
                    BillingStatus = sales.BillingStatus,
                    BillingNumber = sales.BillingNumber,
                    SettlementStatus = sales.SettlementStatus,
                    ReceiptDate = firstTrace?.ReceiptDate,
                    ReceiptSlipNumber = firstTrace?.DetailReceiptNumber,
                    ReceiptLineNumber = firstTrace?.LineNumber,
                    DepositMethodName = firstTrace is null
                        ? null : depositMethodNameByCode.GetValueOrDefault(firstTrace.DepositMethodCode),
                    Remarks = sales.LineRemarks,
                }));

            // 2件目以降の証跡は、売上側の列を空欄にした行として直下に続ける（D-3）。
            for (var i = 1; i < traces.Count; i++)
            {
                var trace = traces[i];
                entries.Add(new SortableEntry(
                    new SortKey(sales.SlipDate, KindOrderSales, sales.SalesSlipNumber, sales.LineNumber, i),
                    new CustomerLedgerEntry
                    {
                        Kind = LedgerEntryKind.Sales,
                        EntryDate = sales.SlipDate,
                        ReceiptDate = trace.ReceiptDate,
                        ReceiptSlipNumber = trace.DetailReceiptNumber,
                        ReceiptLineNumber = trace.LineNumber,
                        DepositMethodName = depositMethodNameByCode.GetValueOrDefault(trace.DepositMethodCode),
                    }));
            }
        }
    }

    /// <summary>tax_unit=Slip: 伝票ごとに1回だけ、その伝票の直下に消費税行を挿入する。</summary>
    private static void AddSlipTaxEntries(List<SortableEntry> entries, IReadOnlyList<Sales> salesLines)
    {
        foreach (var group in salesLines.GroupBy(s => s.SalesSlipNumber))
        {
            var first = group.First();
            if (first.SlipTaxAmount is not { } taxAmount)
            {
                continue;
            }

            entries.Add(new SortableEntry(
                new SortKey(first.SlipDate, KindOrderSales, first.SalesSlipNumber, short.MaxValue, 0),
                new CustomerLedgerEntry
                {
                    Kind = LedgerEntryKind.ConsumptionTax,
                    EntryDate = first.SlipDate,
                    SalesSlipNumber = first.SalesSlipNumber,
                    DebitAmount = taxAmount,
                }));
        }
    }

    /// <summary>tax_unit=Invoice: 確定済み請求の消費税（確定値）＋ 未締め区間の仮計算消費税。</summary>
    private static void AddInvoiceTaxEntries(
        List<SortableEntry> entries, CustomerLedgerInput input, Customer customer, DateOnly dayBeforeFrom)
    {
        foreach (var billing in input.ConfirmedBillings)
        {
            entries.Add(new SortableEntry(
                new SortKey(billing.BillingDate, KindOrderTax, billing.BillingNumber, 0, 0),
                new CustomerLedgerEntry
                {
                    Kind = LedgerEntryKind.ConsumptionTax,
                    EntryDate = billing.BillingDate,
                    BillingNumber = billing.BillingNumber,
                    DebitAmount = billing.TaxAmount,
                }));
        }

        var provisionalToDate = ProvisionalTaxAsOf(customer, input.SalesLines, input.PeriodTo);
        var provisionalBeforeFrom = ProvisionalTaxAsOf(customer, input.SalesLines, dayBeforeFrom);
        var provisionalInPeriod = provisionalToDate - provisionalBeforeFrom;
        if (provisionalInPeriod == 0m)
        {
            return;
        }

        entries.Add(new SortableEntry(
            new SortKey(input.PeriodTo, KindOrderTax, ProvisionalTaxGroupKey, 0, 0),
            new CustomerLedgerEntry
            {
                Kind = LedgerEntryKind.ConsumptionTax,
                EntryDate = input.PeriodTo,
                DebitAmount = provisionalInPeriod,
                Remarks = "未締め分・仮計算",
            }));
    }

    /// <summary>入金は常に入金日付の独立行として残高に反映する（D-2）。</summary>
    private static void AddReceiptEntries(
        List<SortableEntry> entries, CustomerLedgerInput input, Customer customer,
        IReadOnlyDictionary<string, string> depositMethodNameByCode)
    {
        if (customer.TaxUnit == TaxUnit.Line)
        {
            foreach (var line in input.DetailReceiptLines)
            {
                entries.Add(new SortableEntry(
                    new SortKey(line.ReceiptDate, KindOrderReceipt, line.DetailReceiptNumber, line.LineNumber, 0),
                    new CustomerLedgerEntry
                    {
                        Kind = LedgerEntryKind.Receipt,
                        EntryDate = line.ReceiptDate,
                        ReceiptDate = line.ReceiptDate,
                        ReceiptSlipNumber = line.DetailReceiptNumber,
                        ReceiptLineNumber = line.LineNumber,
                        DepositMethodName = depositMethodNameByCode.GetValueOrDefault(line.DepositMethodCode),
                        ReceiptAmount = line.AllocatedAmount,
                        Remarks = BuildDetailReceiptTargetRemarks(line),
                    }));
            }

            return;
        }

        foreach (var line in input.ReceiptLines)
        {
            entries.Add(new SortableEntry(
                new SortKey(line.ReceiptDate, KindOrderReceipt, line.ReceiptSlipNumber, line.LineNumber, 0),
                new CustomerLedgerEntry
                {
                    Kind = LedgerEntryKind.Receipt,
                    EntryDate = line.ReceiptDate,
                    ReceiptDate = line.ReceiptDate,
                    ReceiptSlipNumber = line.ReceiptSlipNumber,
                    ReceiptLineNumber = line.LineNumber,
                    DepositMethodName = depositMethodNameByCode.GetValueOrDefault(line.DepositMethodCode),
                    ReceiptAmount = line.Amount,
                    Remarks = line.LineRemarks,
                    CustomerCode = line.CustomerCode,
                    CustomerName = line.CustomerName,
                }));
        }
    }

    private static string? BuildDetailReceiptTargetRemarks(DetailReceipt line)
    {
        var target = line.TargetType == DetailReceiptTargetType.SalesLine
            ? $"充当先: {line.TargetSalesSlipNumber}-{line.TargetSalesLineNumber}"
            : $"充当先: 明細請求書 {line.TargetDetailInvoiceNumber}";

        return string.IsNullOrEmpty(line.LineRemarks) ? target : $"{target} {line.LineRemarks}";
    }

    private static readonly IReadOnlyDictionary<(string, short), IReadOnlyList<DetailReceipt>> EmptyTrace
        = new Dictionary<(string, short), IReadOnlyList<DetailReceipt>>();

    private static readonly IReadOnlyDictionary<string, string> EmptyDepositMethodNamesByCode
        = new Dictionary<string, string>();

    /// <summary>時系列の並び順キー。同日は 売上 → 消費税 → 入金 の順（KindOrder）。</summary>
    private readonly record struct SortKey(DateOnly Date, int KindOrder, string GroupNumber, int SubOrder, int PairOrder)
        : IComparable<SortKey>
    {
        public int CompareTo(SortKey other)
        {
            var cmp = Date.CompareTo(other.Date);
            if (cmp != 0)
            {
                return cmp;
            }

            cmp = KindOrder.CompareTo(other.KindOrder);
            if (cmp != 0)
            {
                return cmp;
            }

            cmp = string.CompareOrdinal(GroupNumber, other.GroupNumber);
            if (cmp != 0)
            {
                return cmp;
            }

            cmp = SubOrder.CompareTo(other.SubOrder);
            return cmp != 0 ? cmp : PairOrder.CompareTo(other.PairOrder);
        }
    }

    private readonly record struct SortableEntry(SortKey Key, CustomerLedgerEntry Entry);
}
