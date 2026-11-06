using bmcs_app.Application.Common;
using bmcs_app.Domain.Entities;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace bmcs_app.Application.Master;

/// <summary>仕入先マスタのユースケース。</summary>
public class SupplierService(
    BmcsDbContext dbContext,
    ICurrentEmployeeContext currentEmployeeContext,
    ILogger<SupplierService> logger)
{
    /// <summary>有効な仕入先の全件（コード順）。検索モーダル用。</summary>
    public Task<List<Supplier>> GetSuppliersAsync(CancellationToken cancellationToken = default)
        => dbContext.Suppliers
            .AsNoTracking()
            .Where(s => !s.IsDeleted)
            .OrderBy(s => s.SupplierCode)
            .ToListAsync(cancellationToken);

    /// <summary>無効化済みも含めてコードで取得する（訂正で読み込んだ明細の仕入先名の表示用・マスタ画面の読込用）。</summary>
    public Task<Supplier?> GetByCodeAsync(string supplierCode, CancellationToken cancellationToken = default)
        => dbContext.Suppliers
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.SupplierCode == supplierCode, cancellationToken);

    /// <summary>有効な仕入先だけをコードで取得する（伝票明細・商品マスタでの指定の検証用）。</summary>
    public Task<Supplier?> GetActiveByCodeAsync(string supplierCode, CancellationToken cancellationToken = default)
        => dbContext.Suppliers
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.SupplierCode == supplierCode && !s.IsDeleted, cancellationToken);

    public async Task<Supplier> CreateAsync(Supplier supplier, CancellationToken cancellationToken = default)
    {
        var exists = await dbContext.Suppliers
            .AsNoTracking()
            .AnyAsync(s => s.SupplierCode == supplier.SupplierCode, cancellationToken);
        if (exists)
        {
            throw new SupplierValidationException($"仕入先コード「{supplier.SupplierCode}」は既に登録されています。");
        }

        var employeeCode = currentEmployeeContext.EmployeeCode;
        var now = DateTime.Now;
        supplier.CreatedBy = employeeCode;
        supplier.CreatedAt = now;
        supplier.UpdatedBy = employeeCode;
        supplier.UpdatedAt = now;
        supplier.IsDeleted = false;

        dbContext.Suppliers.Add(supplier);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("仕入先を登録しました。SupplierCode={SupplierCode}", supplier.SupplierCode);
        return supplier;
    }

    public async Task<Supplier> UpdateAsync(Supplier supplier, CancellationToken cancellationToken = default)
    {
        var current = await GetTrackedAsync(supplier, cancellationToken);

        current.SupplierName = supplier.SupplierName;
        current.SupplierNameKana = supplier.SupplierNameKana;
        current.PostalCode = supplier.PostalCode;
        current.Address1 = supplier.Address1;
        current.Address2 = supplier.Address2;
        current.PhoneNumber = supplier.PhoneNumber;
        current.FaxNumber = supplier.FaxNumber;
        current.ContactPersonName = supplier.ContactPersonName;
        current.Remarks = supplier.Remarks;
        current.UpdatedBy = currentEmployeeContext.EmployeeCode;
        current.UpdatedAt = DateTime.Now;

        await SaveAsync(cancellationToken);

        logger.LogInformation("仕入先を更新しました。SupplierCode={SupplierCode}", supplier.SupplierCode);
        return current;
    }

    /// <summary>
    /// 仕入先を無効化する。有効な商品が主仕入先として指定している間は無効化できない。
    /// 仕入先別原価の行・過去の伝票明細が参照していても無効化できる（原価行は無効な仕入先では使われず、明細は保存済みの値を保持する）。
    /// </summary>
    public async Task DeactivateAsync(Supplier supplier, CancellationToken cancellationToken = default)
    {
        var current = await GetTrackedAsync(supplier, cancellationToken);

        var isMainSupplier = await dbContext.Products
            .AsNoTracking()
            .AnyAsync(p => !p.IsDeleted && p.MainSupplierCode == current.SupplierCode, cancellationToken);
        if (isMainSupplier)
        {
            throw new SupplierValidationException(
                "この仕入先を主仕入先に指定している商品があるため無効化できません。先に商品マスタの指定を外してください。");
        }

        current.IsDeleted = true;
        current.UpdatedBy = currentEmployeeContext.EmployeeCode;
        current.UpdatedAt = DateTime.Now;

        await SaveAsync(cancellationToken);

        logger.LogInformation("仕入先を無効化しました。SupplierCode={SupplierCode}", supplier.SupplierCode);
    }

    private async Task<Supplier> GetTrackedAsync(Supplier supplier, CancellationToken cancellationToken)
    {
        var current = await dbContext.Suppliers
            .SingleOrDefaultAsync(s => s.SupplierCode == supplier.SupplierCode, cancellationToken)
            ?? throw new SupplierValidationException($"仕入先コード「{supplier.SupplierCode}」は見つかりません。");

        if (!current.RowVersion!.SequenceEqual(supplier.RowVersion!))
        {
            throw new SupplierConcurrencyException("他のユーザーが更新しました。再読み込みしてください。");
        }

        return current;
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new SupplierConcurrencyException("他のユーザーが更新しました。再読み込みしてください。", ex);
        }
    }
}

/// <summary>仕入先マスタの業務ルール違反（形式チェック済みの入力に対する業務的な拒否）。</summary>
public sealed class SupplierValidationException(string message) : Exception(message);

/// <summary>楽観的排他制御の競合（他のユーザーによる更新）。</summary>
public sealed class SupplierConcurrencyException(string message, Exception? inner = null) : Exception(message, inner);
