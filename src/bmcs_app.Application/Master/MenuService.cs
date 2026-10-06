using bmcs_app.Application.Common;
using bmcs_app.Domain.Entities;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace bmcs_app.Application.Master;

/// <summary>
/// メニュー構成マスタのユースケース（TODO.md 2-7）。項目自体の追加・削除、遷移先（screen_key）・親子関係の変更は
/// 画面から行わず DB（scripts/）に直接投入する。画面から編集できるのは表示名・表示順・必要権限レベル・初期展開だけ。
/// </summary>
public class MenuService(
    BmcsDbContext dbContext,
    ICurrentEmployeeContext currentEmployeeContext,
    ILogger<MenuService> logger)
{
    public Task<List<Menu>> GetMenuTreeAsync(CancellationToken cancellationToken = default)
        => dbContext.Menus
            .AsNoTracking()
            .Where(m => !m.IsDeleted)
            .OrderBy(m => m.DisplayOrder)
            .ToListAsync(cancellationToken);

    /// <summary>メニュー構成マスタ画面の項目更新。親（分類）は必要権限レベルを持たず、子（機能）は初期展開を持たない。</summary>
    public async Task<Menu> UpdateAsync(
        string menuCode,
        string menuName,
        short displayOrder,
        byte? requiredPermissionLevel,
        bool isDefaultExpanded,
        byte[] rowVersion,
        CancellationToken cancellationToken = default)
    {
        var menu = await dbContext.Menus
            .SingleOrDefaultAsync(m => m.MenuCode == menuCode && !m.IsDeleted, cancellationToken)
            ?? throw new MenuValidationException($"メニュー「{menuCode}」が見つかりません。");

        if (string.IsNullOrWhiteSpace(menuName))
        {
            throw new MenuValidationException("表示名は必須です。");
        }

        if (menuName.Trim().Length > 40)
        {
            throw new MenuValidationException("表示名は40文字以内で入力してください。");
        }

        if (displayOrder <= 0)
        {
            throw new MenuValidationException("表示順は1以上で入力してください。");
        }

        var isParent = menu.ParentMenuCode is null;
        if (isParent)
        {
            if (requiredPermissionLevel is not null)
            {
                throw new MenuValidationException("分類（親メニュー）には必要権限レベルを設定できません。");
            }
        }
        else if (requiredPermissionLevel is null or < 1 or > 9)
        {
            throw new MenuValidationException("必要権限レベルは1〜9で入力してください。");
        }

        if (!menu.RowVersion!.SequenceEqual(rowVersion))
        {
            throw new MenuConcurrencyException("他のユーザーが更新しました。再読み込みしてください。");
        }

        menu.MenuName = menuName.Trim();
        menu.DisplayOrder = displayOrder;
        menu.RequiredPermissionLevel = requiredPermissionLevel;
        if (isParent)
        {
            menu.IsDefaultExpanded = isDefaultExpanded;
        }

        menu.UpdatedBy = currentEmployeeContext.EmployeeCode;
        menu.UpdatedAt = DateTime.Now;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new MenuConcurrencyException("他のユーザーが更新しました。再読み込みしてください。", ex);
        }

        logger.LogInformation("メニュー「{MenuCode}」を更新しました。", menuCode);
        return menu;
    }
}

/// <summary>メニュー構成マスタの入力検証エラー。</summary>
public sealed class MenuValidationException(string message) : Exception(message);

/// <summary>楽観的排他制御の競合（他のユーザーによる更新）。</summary>
public sealed class MenuConcurrencyException(string message, Exception? inner = null) : Exception(message, inner);
