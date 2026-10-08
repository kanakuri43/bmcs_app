using bmcs_app.Application.Billing;
using bmcs_app.Application.Closing;
using bmcs_app.Application.Common;
using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CustomerEntity = bmcs_app.Domain.Entities.Customer;
using ReceiptAllocationEntity = bmcs_app.Domain.Entities.ReceiptAllocation;
using ReceiptEntity = bmcs_app.Domain.Entities.Receipt;

namespace bmcs_app.Application.Receipt;

/// <summary>
/// 入金入力画面のユースケース。締め得意先（<see cref="TaxUnit.Invoice"/>／
/// <see cref="TaxUnit.Slip"/>）専用。都度得意先（<see cref="TaxUnit.Line"/>）の入金は
/// 明細入金画面が担う。
///
/// 明細行は支払手段の内訳（入金方法＋金額）であり、利用者が直接入力する
/// （docs/design_document.md 17章）。請求への充当は、明細行の合計額を
/// 確定済み<c>billing</c>の未消込残額へ古い順（<c>billing_date</c>→<c>billing_number</c>）に
/// 自動配分して<see cref="ReceiptAllocationEntity"/>として保存する内部データであり、画面には
/// 表示しない（利用者にとって重要なのは充当先ではなく残高のため）。配分そのものは新規の
/// Domainクラスを作らず、既存の<see cref="SettlementAllocator"/>をそのまま流用する。
/// 全額を割り当てきれない残額（前受・過入金）は<c>billing_number = NULL</c>の1行にまとめる。
///
/// 振込手数料差額の入力はこの画面のスコープ外。既存伝票の訂正・取消は
/// <see cref="UpdateAsync"/>／<see cref="CancelSlipAsync"/> が担う。
///
/// 編集ロックは<see cref="EvaluateEditLockAsync"/>が判定する。売上の編集ロック4条件のうち①②④は`sales`側にのみ
/// 適用し、`receipt`自体には適用しない（充当完了直後にほぼ必ずなるため、適用すると訂正・取消できる
/// 入金がほぼ無くなり訂正・取消機能の目的と矛盾する）。代わりに、月次締めに加えて
/// 「請求締めスナップショット」（対象の<c>receipt_date</c>が、その得意先の確定済み<c>billing</c>の
/// 集計期間に含まれるか）を独自にロック条件とする。
/// <see cref="bmcs_app.Application.Billing.BillingClosingService"/>が締め時点で
/// <c>billing.CurrentBillingAmount</c>へ<c>receipt.Amount</c>の合計を焼き込み、以後
/// 誰も再計算しないため、この期間の`receipt`を取消・訂正すると確定済み請求の残高が永久に狂う
/// （詳細は docs/design_document.md 19章）。
/// </summary>
public class ReceiptEntryService(
    BmcsDbContext dbContext,
    SettlementService settlementService,
    SlipNumberService slipNumberService,
    BillingClosedDateService billingClosedDateService,
    MonthlyClosedService monthlyClosedService,
    ICurrentEmployeeContext currentEmployeeContext,
    ILogger<ReceiptEntryService> logger)
{
    /// <summary>
    /// 得意先の請求残高を返す（画面の「請求残高」表示用。保存しない）。
    /// </summary>
    /// <exception cref="ReceiptEntryException">得意先が存在しない、都度得意先、または請求集約元の場合。</exception>
    public async Task<CustomerReceivableSummary> GetReceivableSummaryAsync(
        string customerCode, CancellationToken cancellationToken = default)
    {
        await GetClosingCustomerAsync(customerCode, cancellationToken);

        var outstanding = await GetOutstandingBillingsAsync(customerCode, cancellationToken);
        if (outstanding.Count == 0)
        {
            return new CustomerReceivableSummary(0m, null, null, 0m);
        }

        var latest = outstanding.OrderByDescending(o => o.BillingDate).ThenByDescending(o => o.BillingNumber).First();
        return new CustomerReceivableSummary(
            outstanding.Sum(o => o.Outstanding), latest.BillingNumber, latest.BillingDate, latest.Outstanding);
    }

    /// <summary>
    /// 新規に入金伝票を登録する。明細行（支払手段の内訳）はそのまま<c>receipt</c>へ保存し、
    /// その合計額を確定済み請求へ古い順に配分した結果を<c>receipt_allocation</c>へ保存する。
    /// </summary>
    /// <exception cref="ReceiptEntryException">
    /// 得意先が存在しない、都度得意先、請求集約元（親子請求、docs/design_document.md 28章。業務ルール4:
    /// 入金は請求集約先にだけ入る）、明細行が0件、明細行の合計額が0以下、振込の行で入金先口座が
    /// 未指定、手形の行で手形期日が未指定、または入金日付が請求締め済み期間（design_document.md 25章）の場合。
    /// </exception>
    public async Task<string> SaveNewAsync(
        string customerCode,
        DateOnly receiptDate,
        string? slipRemarks,
        IReadOnlyList<ReceiptLineInput> lines,
        CancellationToken cancellationToken = default)
    {
        await ValidateLinesAsync(lines, cancellationToken);

        var dateCheck = await billingClosedDateService.CheckAsync(customerCode, receiptDate, "入金日付", cancellationToken);
        if (!dateCheck.IsAllowed)
        {
            throw new ReceiptEntryException(dateCheck.Reason!);
        }

        var monthlyClosedReason = await monthlyClosedService.CheckEntryAsync(customerCode, receiptDate, "入金日付", cancellationToken);
        if (monthlyClosedReason is not null)
        {
            throw new ReceiptEntryException(monthlyClosedReason);
        }

        var receiptAmount = lines.Sum(l => l.Amount);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var customer = await GetClosingCustomerAsync(customerCode, cancellationToken);
        var outstanding = await GetOutstandingBillingsAsync(customerCode, cancellationToken);
        var allocationLines = BuildAllocationLines(outstanding, receiptAmount);

        var receiptSlipNumber = await slipNumberService.NextAsync(SlipNumberKind.ReceiptSlip, cancellationToken);
        var employeeCode = currentEmployeeContext.EmployeeCode;
        var now = DateTime.Now;

        var receiptEntities = new List<ReceiptEntity>(lines.Count);
        short lineNumber = 1;
        foreach (var line in lines)
        {
            receiptEntities.Add(new ReceiptEntity
            {
                ReceiptSlipNumber = receiptSlipNumber,
                LineNumber = lineNumber++,
                ReceiptDate = receiptDate,
                CustomerCode = customer.CustomerCode,
                TaxUnit = customer.TaxUnit,
                CustomerName = customer.CustomerName,
                DepositMethodCode = line.DepositMethodCode,
                BankAccountCode = line.BankAccountCode,
                BillDueDate = line.BillDueDate,
                Amount = line.Amount,
                AllocationStatus = AllocationStatus.Unallocated,
                SlipRemarks = slipRemarks,
                LineRemarks = line.LineRemarks,
                CreatedBy = employeeCode,
                CreatedAt = now,
                UpdatedBy = employeeCode,
                UpdatedAt = now,
            });
        }

        dbContext.Receipts.AddRange(receiptEntities);

        var allocationEntities = new List<ReceiptAllocationEntity>(allocationLines.Count);
        short allocationLineNumber = 1;
        foreach (var allocation in allocationLines)
        {
            allocationEntities.Add(new ReceiptAllocationEntity
            {
                ReceiptSlipNumber = receiptSlipNumber,
                LineNumber = allocationLineNumber++,
                CustomerCode = customer.CustomerCode,
                TaxUnit = customer.TaxUnit,
                BillingNumber = allocation.BillingNumber,
                AllocatedAmount = allocation.AllocatedAmount,
                FeeAdjustmentAmount = 0m,
                CreatedBy = employeeCode,
                CreatedAt = now,
                UpdatedBy = employeeCode,
                UpdatedAt = now,
            });
        }

        dbContext.ReceiptAllocations.AddRange(allocationEntities);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            throw new ReceiptEntryException("入金の保存に失敗しました。", ex);
        }

        await settlementService.RecalculateForBillingGroupAsync(customer.CustomerCode, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "入金を登録しました。ReceiptSlipNumber={ReceiptSlipNumber} CustomerCode={CustomerCode} 行数={LineCount}",
            receiptSlipNumber, customer.CustomerCode, receiptEntities.Count);

        return receiptSlipNumber;
    }

    /// <summary>入金No.で1件取得する（読み取り専用表示用・訂正の読込元の両方に使う。保存しない）。</summary>
    public Task<List<ReceiptEntity>> GetByNumberAsync(
        string receiptSlipNumber, CancellationToken cancellationToken = default)
        => dbContext.Receipts
            .AsNoTracking()
            .Where(r => r.ReceiptSlipNumber == receiptSlipNumber && !r.IsDeleted)
            .OrderBy(r => r.LineNumber)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// 対象の`receipt`行が訂正・取消不可かどうかを判定する。
    /// ①月次締め: 得意先・伝票日付の年月に対応する確定済み<c>monthly_closing</c>が存在する
    /// （請求集約先の行が確定済みの場合も含む。判定は<c>MonthlyClosedService</c>に集約）。
    /// ②請求締めスナップショット: 伝票日付が、その得意先の確定済み<c>billing</c>のうち最新の
    /// <c>billing_date</c>以前（＝いずれかの確定済み請求の集計期間に含まれる）。
    /// このクラスの doc comment を参照（なぜ充当完了・請求への充当自体をロック条件にしないか）。
    /// </summary>
    public async Task<SalesEditLock> EvaluateEditLockAsync(
        IReadOnlyList<ReceiptEntity> lines, CancellationToken cancellationToken = default)
    {
        var customerCode = lines[0].CustomerCode;
        var receiptDate = lines[0].ReceiptDate;

        var monthlyClosingConfirmed = await monthlyClosedService.IsClosedAsync(customerCode, receiptDate, cancellationToken);
        if (monthlyClosingConfirmed)
        {
            return new SalesEditLock(true, "月次締め済みのため訂正・取消できません。");
        }

        var latestConfirmedBillingDate = await billingClosedDateService.GetLatestConfirmedBillingDateAsync(
            customerCode, cancellationToken);

        if (latestConfirmedBillingDate is not null && receiptDate <= latestConfirmedBillingDate.Value)
        {
            return new SalesEditLock(
                true, "請求締め済みの期間の入金のため訂正・取消できません。先に締め解除してから操作してください。");
        }

        return SalesEditLock.Unlocked;
    }

    /// <summary>
    /// 既存の入金伝票を訂正する。元伝票の直接修正であり、赤伝は発行しない。
    /// 明細行（支払手段の内訳）の追加・更新・削除を行い、請求への充当（<see cref="ReceiptAllocationEntity"/>）は
    /// 新しい合計額で全面再構築する（充当は導出データであり、ユーザーが直接編集するものではないため）。
    /// </summary>
    /// <param name="receiptSlipNumber">対象の入金伝票番号。</param>
    /// <param name="receiptDate">訂正後の入金日付（伝票単位の値）。</param>
    /// <param name="slipRemarks">訂正後の伝票摘要（伝票単位の値）。</param>
    /// <param name="lines">
    /// 保存後にあるべき明細行の集合。既存行は読込時と同じ<see cref="ReceiptLineCorrection.LineNumber"/>を
    /// 維持すること。新規追加する行は<c>0</c>を指定する（本メソッドが採番する）。読込時にあった行番号の
    /// うち含まれないものは論理削除する。
    /// </param>
    /// <param name="loadedLineNumbers">
    /// 画面が伝票を読み込んだ時点の明細行番号の集合（<see cref="SlipConcurrencyGuard"/>と同じ理由で
    /// 呼び出し元が読込時にキャプチャしておく）。
    /// </param>
    /// <exception cref="ReceiptEntryException">
    /// 対象の入金が存在しない、編集ロックに該当する、明細行の内容が不正、または訂正後の入金日付が
    /// 請求締め済み期間（design_document.md 25章）の場合。
    /// </exception>
    /// <exception cref="SlipConcurrencyException">他のユーザーが同じ伝票を更新済みの場合。</exception>
    public async Task UpdateAsync(
        string receiptSlipNumber,
        DateOnly receiptDate,
        string? slipRemarks,
        IReadOnlyList<ReceiptLineCorrection> lines,
        IReadOnlyList<short> loadedLineNumbers,
        CancellationToken cancellationToken = default)
    {
        await ValidateLinesAsync(lines.Select(l => l.ToInput()).ToList(), cancellationToken);

        var employeeCode = currentEmployeeContext.EmployeeCode;
        var now = DateTime.Now;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var currentLines = await dbContext.Receipts
            .Where(r => r.ReceiptSlipNumber == receiptSlipNumber && !r.IsDeleted)
            .ToListAsync(cancellationToken);

        if (currentLines.Count == 0)
        {
            throw new ReceiptEntryException($"入金が見つかりません。ReceiptSlipNumber={receiptSlipNumber}");
        }

        SlipConcurrencyGuard.EnsureLineSetUnchanged(
            loadedLineNumbers, currentLines.Select(l => l.LineNumber).ToList());

        var lockResultBefore = await EvaluateEditLockAsync(currentLines, cancellationToken);
        if (lockResultBefore.IsLocked)
        {
            throw new ReceiptEntryException(lockResultBefore.Reason!);
        }

        var customerCode = currentLines[0].CustomerCode;
        var customerName = currentLines[0].CustomerName;
        var taxUnit = currentLines[0].TaxUnit;

        var currentByLineNumber = currentLines.ToDictionary(l => l.LineNumber);
        var incomingKept = lines.Where(l => l.LineNumber != 0).ToList();
        var incomingNew = lines.Where(l => l.LineNumber == 0).ToList();

        var keptLineNumbers = incomingKept.Select(l => l.LineNumber).ToHashSet();
        if (incomingKept.Any(l => !currentByLineNumber.ContainsKey(l.LineNumber)))
        {
            throw new ReceiptEntryException("存在しない明細行番号が指定されています。");
        }

        // 読込時にあったが今回の一覧に含まれない行は論理削除する（物理削除しない）。
        foreach (var current in currentLines.Where(l => !keptLineNumbers.Contains(l.LineNumber)))
        {
            current.IsDeleted = true;
        }

        foreach (var incoming in incomingKept)
        {
            var current = currentByLineNumber[incoming.LineNumber];
            current.DepositMethodCode = incoming.DepositMethodCode;
            current.BankAccountCode = incoming.BankAccountCode;
            current.BillDueDate = incoming.BillDueDate;
            current.Amount = incoming.Amount;
            current.LineRemarks = incoming.LineRemarks;
        }

        var nextLineNumber = (short)(currentLines.Max(l => l.LineNumber) + 1);
        var newEntities = new List<ReceiptEntity>(incomingNew.Count);
        foreach (var incoming in incomingNew)
        {
            newEntities.Add(new ReceiptEntity
            {
                ReceiptSlipNumber = receiptSlipNumber,
                LineNumber = nextLineNumber++,
                ReceiptDate = receiptDate,
                CustomerCode = customerCode,
                TaxUnit = taxUnit,
                CustomerName = customerName,
                DepositMethodCode = incoming.DepositMethodCode,
                BankAccountCode = incoming.BankAccountCode,
                BillDueDate = incoming.BillDueDate,
                Amount = incoming.Amount,
                AllocationStatus = AllocationStatus.Unallocated,
                SlipRemarks = slipRemarks,
                LineRemarks = incoming.LineRemarks,
                CreatedBy = employeeCode,
                CreatedAt = now,
                UpdatedBy = employeeCode,
                UpdatedAt = now,
                IsDeleted = false,
            });
        }

        dbContext.Receipts.AddRange(newEntities);

        var activeLines = currentLines.Where(l => !l.IsDeleted).Concat(newEntities).ToList();
        foreach (var line in activeLines)
        {
            line.ReceiptDate = receiptDate;
            line.SlipRemarks = slipRemarks;
        }

        // 訂正後（新状態）で編集ロック対象にならないかも確認する（例: 入金日付を確定済み請求の
        // 集計期間・確定済み月次締め年月へ動かす訂正を防ぐ）。
        var lockResultAfter = await EvaluateEditLockAsync(activeLines, cancellationToken);
        if (lockResultAfter.IsLocked)
        {
            throw new ReceiptEntryException($"訂正後の内容は編集ロック対象になるため保存できません: {lockResultAfter.Reason}");
        }

        var dateCheckAfter = await billingClosedDateService.CheckAsync(customerCode, receiptDate, "入金日付", cancellationToken);
        if (!dateCheckAfter.IsAllowed)
        {
            throw new ReceiptEntryException(dateCheckAfter.Reason!);
        }

        // 読み込んだ全行（削除された行を含む）を更新対象に含め、rowversion の照合を全行で効かせる
        // （docs/architecture.md 9章）。
        SlipConcurrencyGuard.TouchAll(currentLines, employeeCode, now);

        var currentAllocations = await dbContext.ReceiptAllocations
            .Where(a => a.ReceiptSlipNumber == receiptSlipNumber && !a.IsDeleted)
            .ToListAsync(cancellationToken);
        foreach (var allocation in currentAllocations)
        {
            allocation.IsDeleted = true;
        }
        SlipConcurrencyGuard.TouchAll(currentAllocations, employeeCode, now);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new SlipConcurrencyException("他のユーザーが更新しました。再読み込みしてください。", ex);
        }

        // 充当（receipt_allocation）は導出データのため、新しい合計額で全面再構築する。上の
        // SaveChangesAsync で自伝票の旧充当を確定させてから請求残高を問い合わせる必要がある
        // （GetOutstandingBillingsAsync はサーバー側クエリで ChangeTracker 上の未コミット変更を
        // 見ないため。docs/architecture.md 6章に許容ケースとして追記済み）。
        var outstanding = await GetOutstandingBillingsAsync(customerCode, cancellationToken);
        var newTotal = activeLines.Sum(l => l.Amount);
        var newAllocationLines = BuildAllocationLines(outstanding, newTotal);

        var allocationLineNumber = (short)(
            currentAllocations.Count == 0 ? 1 : currentAllocations.Max(a => a.LineNumber) + 1);
        var newAllocationEntities = new List<ReceiptAllocationEntity>(newAllocationLines.Count);
        foreach (var allocation in newAllocationLines)
        {
            newAllocationEntities.Add(new ReceiptAllocationEntity
            {
                ReceiptSlipNumber = receiptSlipNumber,
                LineNumber = allocationLineNumber++,
                CustomerCode = customerCode,
                TaxUnit = taxUnit,
                BillingNumber = allocation.BillingNumber,
                AllocatedAmount = allocation.AllocatedAmount,
                FeeAdjustmentAmount = 0m,
                CreatedBy = employeeCode,
                CreatedAt = now,
                UpdatedBy = employeeCode,
                UpdatedAt = now,
            });
        }

        dbContext.ReceiptAllocations.AddRange(newAllocationEntities);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            throw new ReceiptEntryException("入金の保存に失敗しました。", ex);
        }

        await settlementService.RecalculateForBillingGroupAsync(customerCode, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("入金を訂正しました。ReceiptSlipNumber={ReceiptSlipNumber}", receiptSlipNumber);
    }

    /// <summary>
    /// 入金伝票を取消する。全明細行と、紐づく充当（<see cref="ReceiptAllocationEntity"/>）を
    /// 論理削除する（物理削除しない）。編集ロックに該当する伝票は取消できない。
    /// </summary>
    /// <exception cref="ReceiptEntryException">対象の入金が存在しない、または編集ロックに該当する場合。</exception>
    /// <exception cref="SlipConcurrencyException">他のユーザーが同じ伝票を更新済みの場合。</exception>
    public async Task CancelSlipAsync(string receiptSlipNumber, CancellationToken cancellationToken = default)
    {
        var employeeCode = currentEmployeeContext.EmployeeCode;
        var now = DateTime.Now;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var lines = await dbContext.Receipts
            .Where(r => r.ReceiptSlipNumber == receiptSlipNumber && !r.IsDeleted)
            .ToListAsync(cancellationToken);

        if (lines.Count == 0)
        {
            throw new ReceiptEntryException($"入金が見つかりません。ReceiptSlipNumber={receiptSlipNumber}");
        }

        var lockResult = await EvaluateEditLockAsync(lines, cancellationToken);
        if (lockResult.IsLocked)
        {
            throw new ReceiptEntryException(lockResult.Reason!);
        }

        var allocations = await dbContext.ReceiptAllocations
            .Where(a => a.ReceiptSlipNumber == receiptSlipNumber && !a.IsDeleted)
            .ToListAsync(cancellationToken);

        foreach (var line in lines)
        {
            line.IsDeleted = true;
            line.UpdatedBy = employeeCode;
            line.UpdatedAt = now;
        }

        foreach (var allocation in allocations)
        {
            allocation.IsDeleted = true;
            allocation.UpdatedBy = employeeCode;
            allocation.UpdatedAt = now;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new SlipConcurrencyException("他のユーザーが更新しました。再読み込みしてください。", ex);
        }

        await settlementService.RecalculateForBillingGroupAsync(lines[0].CustomerCode, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "入金を取消しました。ReceiptSlipNumber={ReceiptSlipNumber} 行数={LineCount}", receiptSlipNumber, lines.Count);
    }

    /// <summary>
    /// 「振込なら口座必須」「手形なら期日必須」の対応は<see cref="Domain.Entities.DepositMethod"/>の
    /// フラグに基づく（マスタ駆動。DBのCHECK制約では他テーブル
    /// 参照ができないため、この検証がクロステーブル整合性の唯一の担保点になる）。
    /// </summary>
    private async Task ValidateLinesAsync(IReadOnlyList<ReceiptLineInput> lines, CancellationToken cancellationToken)
    {
        if (lines.Count == 0)
        {
            throw new ReceiptEntryException("明細行を1件以上入力してください。");
        }

        if (lines.Sum(l => l.Amount) <= 0m)
        {
            throw new ReceiptEntryException("入金額の合計は0より大きい値を入力してください。");
        }

        var codes = lines.Select(l => l.DepositMethodCode).Distinct().ToList();
        var depositMethods = await dbContext.DepositMethods
            .AsNoTracking()
            .Where(m => codes.Contains(m.DepositMethodCode))
            .ToDictionaryAsync(m => m.DepositMethodCode, cancellationToken);

        foreach (var line in lines)
        {
            if (!depositMethods.TryGetValue(line.DepositMethodCode, out var method))
            {
                throw new ReceiptEntryException($"入金方法「{line.DepositMethodCode}」は見つかりません。");
            }

            if (method.RequiresBankAccount && string.IsNullOrWhiteSpace(line.BankAccountCode))
            {
                throw new ReceiptEntryException($"{method.DepositMethodName}の行は入金先口座を指定してください。");
            }

            if (method.RequiresBillDueDate && line.BillDueDate is null)
            {
                throw new ReceiptEntryException($"{method.DepositMethodName}の行は手形期日を指定してください。");
            }
        }
    }

    private async Task<CustomerEntity> GetClosingCustomerAsync(
        string customerCode, CancellationToken cancellationToken)
    {
        var customer = await dbContext.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerCode == customerCode && !c.IsDeleted, cancellationToken)
            ?? throw new ReceiptEntryException($"得意先コード「{customerCode}」が見つかりません。");

        if (customer.TaxUnit == TaxUnit.Line)
        {
            throw new ReceiptEntryException(
                "都度得意先（内税明細単位）はこの画面では入金登録できません。明細入金画面をご利用ください。");
        }

        if (!customer.IsBillingRoot)
        {
            throw new ReceiptEntryException(
                $"「{customer.CustomerName}」は請求集約元です。入金は請求集約先「{customer.BillingCustomerCode}」で登録してください。");
        }

        return customer;
    }

    /// <summary>
    /// 確定済み<c>billing</c>のうち未消込残額が0でないものを、古い順（<c>billing_date</c>→
    /// <c>billing_number</c>）で返す。未消込残額 = <c>current_billing_amount</c> − 既存
    /// <c>receipt_allocation</c>の充当済額（<c>allocated_amount + fee_adjustment_amount</c>）。
    /// </summary>
    private async Task<List<OutstandingBilling>> GetOutstandingBillingsAsync(
        string customerCode, CancellationToken cancellationToken)
    {
        var billings = await dbContext.Billings
            .AsNoTracking()
            .Where(b => b.CustomerCode == customerCode && !b.IsDeleted && b.BillingStatus == BillingStatus.Confirmed)
            .OrderBy(b => b.BillingDate).ThenBy(b => b.BillingNumber)
            .ToListAsync(cancellationToken);

        if (billings.Count == 0)
        {
            return [];
        }

        var billingNumbers = billings.Select(b => b.BillingNumber).ToList();
        var allocatedByBilling = await dbContext.ReceiptAllocations
            .AsNoTracking()
            .Where(a => !a.IsDeleted && a.BillingNumber != null && billingNumbers.Contains(a.BillingNumber!))
            .GroupBy(a => a.BillingNumber!)
            .Select(g => new { BillingNumber = g.Key, Total = g.Sum(a => a.AllocatedAmount + a.FeeAdjustmentAmount) })
            .ToDictionaryAsync(x => x.BillingNumber, x => x.Total, cancellationToken);

        return billings
            .Select(b => new OutstandingBilling(
                b.BillingNumber,
                b.BillingDate,
                b.CurrentBillingAmount - allocatedByBilling.GetValueOrDefault(b.BillingNumber)))
            .Where(o => o.Outstanding != 0m)
            .ToList();
    }

    /// <summary>
    /// <see cref="SettlementAllocator"/>を請求単位の配分に流用する。未消込残額を
    /// 古い順に整列した対象額として渡し、入金額をその順に充当する。全額充当できない残額
    /// （前受・過入金）は<c>billing_number = NULL</c>の1行にまとめる。
    /// </summary>
    private static List<ReceiptAllocationLine> BuildAllocationLines(
        IReadOnlyList<OutstandingBilling> outstanding, decimal receiptAmount)
    {
        var targets = outstanding.Select(o => o.Outstanding).ToList();
        var allocations = SettlementAllocator.Allocate(targets, receiptAmount);

        var lines = new List<ReceiptAllocationLine>();
        for (var i = 0; i < outstanding.Count; i++)
        {
            if (allocations[i] != 0m)
            {
                lines.Add(new ReceiptAllocationLine(outstanding[i].BillingNumber, allocations[i]));
            }
        }

        var remainder = receiptAmount - allocations.Sum();
        if (remainder != 0m || lines.Count == 0)
        {
            lines.Add(new ReceiptAllocationLine(null, remainder));
        }

        return lines;
    }

    private sealed record OutstandingBilling(string BillingNumber, DateOnly BillingDate, decimal Outstanding);
}

/// <summary>入金明細行の入力（支払手段の内訳）。</summary>
public sealed record ReceiptLineInput(
    string DepositMethodCode,
    string? BankAccountCode,
    DateOnly? BillDueDate,
    decimal Amount,
    string? LineRemarks);

/// <summary>
/// 訂正（<see cref="ReceiptEntryService.UpdateAsync"/>）用の入金明細行の入力。<see cref="ReceiptLineInput"/>に
/// 永続行番号を加えたもの。<see cref="LineNumber"/>が<c>0</c>の行は新規追加を意味する
/// （<see cref="bmcs_app.Application.Sales.SalesService.UpdateAsync"/>と同じ規約）。
/// </summary>
public sealed record ReceiptLineCorrection(
    short LineNumber,
    string DepositMethodCode,
    string? BankAccountCode,
    DateOnly? BillDueDate,
    decimal Amount,
    string? LineRemarks)
{
    public ReceiptLineInput ToInput() => new(DepositMethodCode, BankAccountCode, BillDueDate, Amount, LineRemarks);
}

/// <summary>
/// 入金額の配分結果（請求単位。内部データ）。<see cref="BillingNumber"/>が<c>null</c>の行は
/// 前受・過入金（充当先未定）を表す。
/// </summary>
public sealed record ReceiptAllocationLine(string? BillingNumber, decimal AllocatedAmount);

/// <summary>得意先の請求残高（画面の「請求残高」表示用）。</summary>
public sealed record CustomerReceivableSummary(
    decimal OutstandingTotal, string? LatestBillingNumber, DateOnly? LatestBillingDate, decimal LatestBillingAmount);

/// <summary>入金入力の業務ルール違反。</summary>
public sealed class ReceiptEntryException(string message, Exception? inner = null) : Exception(message, inner);
