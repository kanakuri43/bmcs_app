using bmcs_app.Application.Common;
using bmcs_app.Domain.Entities;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace bmcs_app.Application.Master;

/// <summary>
/// 仕入先／商品／原価マスタのユースケース。仕入先1件分の原価を商品の一覧として読み込み、
/// 変更した行をまとめて（1回の SaveChanges で）登録する。
/// </summary>
public class ProductSupplierCostService(
    BmcsDbContext dbContext,
    ICurrentEmployeeContext currentEmployeeContext,
    ILogger<ProductSupplierCostService> logger)
{
    /// <summary>decimal(15,4) に収まる最大値。</summary>
    private const decimal MaxCostPrice = 99_999_999_999.9999m;

    /// <summary>
    /// 伝票明細で商品を選んだときの仕入先と原価の初期値。仕入先は商品の主仕入先（なければ <c>null</c>）で、
    /// 原価はその仕入先の原価（有効な行がなければ商品マスタの標準原価）。
    /// </summary>
    public async Task<(string? SupplierCode, decimal CostPrice)> ResolveDefaultAsync(
        string productCode, CancellationToken cancellationToken = default)
    {
        var product = await dbContext.Products
            .AsNoTracking()
            .Where(p => p.ProductCode == productCode)
            .Select(p => new { p.MainSupplierCode, p.StandardCostPrice })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ProductSupplierCostValidationException($"商品コード「{productCode}」は見つかりません。");

        var costPrice = await ResolveAsync(productCode, product.MainSupplierCode, product.StandardCostPrice, cancellationToken);
        return (product.MainSupplierCode, costPrice);
    }

    /// <summary>
    /// 指定した仕入先の原価。<paramref name="supplierCode"/> が <c>null</c>、またはその仕入先の有効な原価行がなければ、
    /// 商品マスタの標準原価。
    /// </summary>
    public async Task<decimal> ResolveCostAsync(
        string productCode, string? supplierCode, CancellationToken cancellationToken = default)
    {
        var standardCost = await dbContext.Products
            .AsNoTracking()
            .Where(p => p.ProductCode == productCode)
            .Select(p => (decimal?)p.StandardCostPrice)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ProductSupplierCostValidationException($"商品コード「{productCode}」は見つかりません。");

        return await ResolveAsync(productCode, supplierCode, standardCost, cancellationToken);
    }

    private async Task<decimal> ResolveAsync(
        string productCode, string? supplierCode, decimal standardCost, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(supplierCode))
        {
            return standardCost;
        }

        var supplierCost = await dbContext.ProductSupplierCosts
            .AsNoTracking()
            .Where(c => c.ProductCode == productCode && c.SupplierCode == supplierCode && !c.IsDeleted)
            .Select(c => (decimal?)c.CostPrice)
            .SingleOrDefaultAsync(cancellationToken);
        return supplierCost ?? standardCost;
    }

    /// <summary>
    /// 商品検索モーダル（マスタ軸）が商品マスタに LEFT JOIN する、選択できる仕入先別原価の全件（商品コード・仕入先コード順）。
    /// 有効な原価行のうち、有効な仕入先のものだけ。無効な仕入先の行は、明細に指定できない仕入先なので出さない。
    /// </summary>
    public Task<List<SelectableSupplierCost>> GetSelectableCostsAsync(CancellationToken cancellationToken = default)
        => (from c in dbContext.ProductSupplierCosts.AsNoTracking()
            join s in dbContext.Suppliers.AsNoTracking() on c.SupplierCode equals s.SupplierCode
            where !c.IsDeleted && !s.IsDeleted
            orderby c.ProductCode, c.SupplierCode
            select new SelectableSupplierCost(c.ProductCode, c.SupplierCode, s.SupplierName, c.CostPrice))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// 有効な商品の全件に、仕入先の原価を付けて返す。無効化済みの行の原価は null（未登録扱い）で、
    /// RowVersion だけ持たせる（登録時の競合検出と、無効化済みの行の復活に使う）。
    /// </summary>
    public async Task<IReadOnlyList<ProductSupplierCostSetting>> GetSettingsAsync(
        string supplierCode, CancellationToken cancellationToken = default)
    {
        var products = await dbContext.Products
            .AsNoTracking()
            .Where(p => !p.IsDeleted)
            .OrderBy(p => p.ProductCode)
            .Select(p => new { p.ProductCode, p.ProductName, p.StandardCostPrice })
            .ToListAsync(cancellationToken);
        var costs = await dbContext.ProductSupplierCosts
            .AsNoTracking()
            .Where(c => c.SupplierCode == supplierCode)
            .ToDictionaryAsync(c => c.ProductCode, cancellationToken);

        return products.Select(p =>
        {
            costs.TryGetValue(p.ProductCode, out var cost);
            return new ProductSupplierCostSetting(
                p.ProductCode,
                p.ProductName,
                p.StandardCostPrice,
                cost is { IsDeleted: false } ? cost.CostPrice : null,
                cost?.RowVersion);
        }).ToList();
    }

    /// <summary>
    /// 変更した行をまとめて登録する（全件成功か全件失敗）。原価が <c>null</c> の変更は、登録済みなら無効化、未登録なら何もしない。
    /// 原価がある変更は、行が無ければ追加、あれば更新し、無効化済みの行は有効に戻す。
    /// </summary>
    public async Task SaveSettingsAsync(
        string supplierCode,
        IReadOnlyList<ProductSupplierCostChange> changes,
        CancellationToken cancellationToken = default)
    {
        foreach (var change in changes)
        {
            if (change.CostPrice is { } costPrice && (costPrice < 0m || costPrice > MaxCostPrice))
            {
                // 返品はマイナス数量で表すため、原価自体は 0 以上とする（DB の CHECK 制約と同じ）。
                throw new ProductSupplierCostValidationException(
                    $"商品「{change.ProductCode}」: 原価は 0 以上 {MaxCostPrice:N4} 以下で入力してください。");
            }
        }

        try
        {
            await EnsureActiveSupplierAndProductsAsync(
                supplierCode,
                changes.Where(c => c.CostPrice is not null).Select(c => c.ProductCode),
                cancellationToken);

            if (changes.Count > 0)
            {
                await ApplyChangesAsync(supplierCode, changes, cancellationToken);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ProductSupplierCostConcurrencyException("他のユーザーが更新しました。再読み込みしてください。", ex);
        }
        finally
        {
            // 失敗時に未保存の変更が追跡に残らないようにする（成功時も、次の読込を最新にするため問題ない）。
            dbContext.ChangeTracker.Clear();
        }

        logger.LogInformation(
            "仕入先別の原価を登録しました。SupplierCode={SupplierCode} Changes={Count}", supplierCode, changes.Count);
    }

    private async Task ApplyChangesAsync(
        string supplierCode, IReadOnlyList<ProductSupplierCostChange> changes, CancellationToken cancellationToken)
    {
        var employeeCode = currentEmployeeContext.EmployeeCode;
        var now = DateTime.Now;

        var codes = changes.Select(c => c.ProductCode).ToList();
        var existing = await dbContext.ProductSupplierCosts
            .Where(c => c.SupplierCode == supplierCode && codes.Contains(c.ProductCode))
            .ToDictionaryAsync(c => c.ProductCode, cancellationToken);

        foreach (var change in changes)
        {
            existing.TryGetValue(change.ProductCode, out var current);
            EnsureNotModified(current?.RowVersion, change.RowVersion);

            if (change.CostPrice is not { } costPrice)
            {
                if (current is { IsDeleted: false })
                {
                    current.IsDeleted = true;
                    Touch(current, employeeCode, now);
                }

                continue;
            }

            if (current is null)
            {
                dbContext.ProductSupplierCosts.Add(new ProductSupplierCost
                {
                    ProductCode = change.ProductCode,
                    SupplierCode = supplierCode,
                    CostPrice = costPrice,
                    CreatedBy = employeeCode,
                    CreatedAt = now,
                    UpdatedBy = employeeCode,
                    UpdatedAt = now,
                });
                continue;
            }

            current.CostPrice = costPrice;
            current.IsDeleted = false;
            Touch(current, employeeCode, now);
        }
    }

    /// <summary>読込時の RowVersion（行が無ければ <c>null</c>）と現在の行が違えば、他のユーザーが更新している。</summary>
    private static void EnsureNotModified(byte[]? currentRowVersion, byte[]? loadedRowVersion)
    {
        var same = currentRowVersion is null
            ? loadedRowVersion is null
            : loadedRowVersion is not null && currentRowVersion.SequenceEqual(loadedRowVersion);
        if (!same)
        {
            throw new ProductSupplierCostConcurrencyException("他のユーザーが更新しました。再読み込みしてください。");
        }
    }

    private static void Touch(AuditableEntity entity, string employeeCode, DateTime now)
    {
        entity.UpdatedBy = employeeCode;
        entity.UpdatedAt = now;
    }

    private async Task EnsureActiveSupplierAndProductsAsync(
        string supplierCode, IEnumerable<string> productCodes, CancellationToken cancellationToken)
    {
        var supplierActive = await dbContext.Suppliers
            .AsNoTracking()
            .AnyAsync(s => s.SupplierCode == supplierCode && !s.IsDeleted, cancellationToken);
        if (!supplierActive)
        {
            throw new ProductSupplierCostValidationException(
                $"仕入先コード「{supplierCode}」は登録されていないか、無効です。");
        }

        var codes = productCodes.Distinct().ToList();
        if (codes.Count == 0)
        {
            return;
        }

        var activeCodes = await dbContext.Products
            .AsNoTracking()
            .Where(p => codes.Contains(p.ProductCode) && !p.IsDeleted)
            .Select(p => p.ProductCode)
            .ToListAsync(cancellationToken);
        var missing = codes.Except(activeCodes).FirstOrDefault();
        if (missing is not null)
        {
            throw new ProductSupplierCostValidationException(
                $"商品コード「{missing}」は登録されていないか、無効です。");
        }
    }
}

/// <summary>一覧の1行分。原価は有効な登録がなければ <c>null</c>。RowVersion は無効化済みの行でも持つ。</summary>
public sealed record ProductSupplierCostSetting(
    string ProductCode,
    string ProductName,
    decimal StandardCostPrice,
    decimal? CostPrice,
    byte[]? RowVersion);

/// <summary>商品検索で選べる仕入先別原価の1行。</summary>
public sealed record SelectableSupplierCost(string ProductCode, string SupplierCode, string SupplierName, decimal CostPrice);

/// <summary>原価の変更。<paramref name="CostPrice"/> が <c>null</c> なら無効化。<paramref name="RowVersion"/> は読込時の値。</summary>
public sealed record ProductSupplierCostChange(string ProductCode, decimal? CostPrice, byte[]? RowVersion);

/// <summary>仕入先／商品／原価マスタの業務ルール違反（形式チェック済みの入力に対する業務的な拒否）。</summary>
public sealed class ProductSupplierCostValidationException(string message) : Exception(message);

/// <summary>楽観的排他制御の競合（他のユーザーによる更新）。</summary>
public sealed class ProductSupplierCostConcurrencyException(string message, Exception? inner = null) : Exception(message, inner);
