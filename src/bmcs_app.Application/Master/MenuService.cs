using bmcs_app.Domain.Entities;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace bmcs_app.Application.Master;

/// <summary>メニュー構成マスタの参照（TODO.md 2-7）。項目自体は DB に直接投入し、画面からの編集は持たない。</summary>
public class MenuService(BmcsDbContext dbContext)
{
    public Task<List<Menu>> GetMenuTreeAsync(CancellationToken cancellationToken = default)
        => dbContext.Menus
            .AsNoTracking()
            .Where(m => !m.IsDeleted)
            .OrderBy(m => m.DisplayOrder)
            .ToListAsync(cancellationToken);
}
