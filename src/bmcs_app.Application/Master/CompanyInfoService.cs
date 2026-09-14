using bmcs_app.Application.Common;
using bmcs_app.Domain.Entities;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace bmcs_app.Application.Master;

/// <summary>
/// 自社情報マスタのユースケース（TODO.md 2-4）。1レコード運用（<see cref="CompanyInfo.CompanyInfoId"/> は常に1）のため、
/// 他マスタと違いコード検索・新規登録・無効化の概念を持たない（画面を開くと常に唯一の行を読み込み、保存は upsert）。
/// </summary>
public class CompanyInfoService(
    BmcsDbContext dbContext,
    ICurrentEmployeeContext currentEmployeeContext,
    ILogger<CompanyInfoService> logger)
{
    private const byte CompanyInfoId = 1;

    public Task<CompanyInfo?> GetAsync(CancellationToken cancellationToken = default)
        => dbContext.CompanyInfos
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.CompanyInfoId == CompanyInfoId, cancellationToken);

    public async Task<CompanyInfo> SaveAsync(CompanyInfo companyInfo, CancellationToken cancellationToken = default)
    {
        var current = await dbContext.CompanyInfos
            .SingleOrDefaultAsync(c => c.CompanyInfoId == CompanyInfoId, cancellationToken);

        var employeeCode = currentEmployeeContext.EmployeeCode;
        var now = DateTime.Now;

        if (current is null)
        {
            companyInfo.CompanyInfoId = CompanyInfoId;
            companyInfo.CreatedBy = employeeCode;
            companyInfo.CreatedAt = now;
            companyInfo.UpdatedBy = employeeCode;
            companyInfo.UpdatedAt = now;
            companyInfo.IsDeleted = false;

            dbContext.CompanyInfos.Add(companyInfo);
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation("自社情報を新規登録しました。");
            return companyInfo;
        }

        if (!current.RowVersion!.SequenceEqual(companyInfo.RowVersion!))
        {
            throw new CompanyInfoConcurrencyException("他のユーザーが更新しました。再読み込みしてください。");
        }

        current.CompanyName = companyInfo.CompanyName;
        current.InvoiceRegistrationNumber = companyInfo.InvoiceRegistrationNumber;
        current.PostalCode = companyInfo.PostalCode;
        current.Address1 = companyInfo.Address1;
        current.Address2 = companyInfo.Address2;
        current.PhoneNumber = companyInfo.PhoneNumber;
        current.FaxNumber = companyInfo.FaxNumber;
        current.RepresentativeName = companyInfo.RepresentativeName;
        current.UpdatedBy = employeeCode;
        current.UpdatedAt = now;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new CompanyInfoConcurrencyException("他のユーザーが更新しました。再読み込みしてください。", ex);
        }

        logger.LogInformation("自社情報を更新しました。");
        return current;
    }
}

/// <summary>楽観的排他制御の競合（他のユーザーによる更新）。</summary>
public sealed class CompanyInfoConcurrencyException(string message, Exception? inner = null) : Exception(message, inner);
