using bmcs_app.Application.Master;
using bmcs_app.Domain.Entities;
using bmcs_app.Services;
using bmcs_app.Views.Common;

namespace bmcs_app.ViewModels.Common;

/// <summary>
/// 伝票明細行の仕入先・原価の確定処理。受注入力・売上入力で共用する。
/// 仕入先の確定は <see cref="SlipLineViewModel.ApplySupplier"/> に一本化し、原価は
/// <see cref="ProductSupplierCostService"/> で「仕入先の原価 → 標準原価」の順に解決する。
/// </summary>
public sealed class SlipLineSupplierHelper(
    SupplierService supplierService,
    ProductSupplierCostService costService,
    WindowService windowService)
{
    /// <summary>商品を選んだ直後の初期値（主仕入先とその原価）を行へ設定する。商品を差し替えた行は、選んでいた仕入先を破棄して引き直す。</summary>
    public async Task ApplyDefaultAsync(SlipLineViewModel line)
    {
        var (supplierCode, costPrice) = await costService.ResolveDefaultAsync(line.ProductCode);
        line.ApplySupplier(supplierCode, await FindNameAsync(supplierCode), costPrice);
    }

    /// <summary>保存済みの明細（受注から売上化・複写・訂正の読込）の仕入先と原価をそのまま行へ設定する。原価は引き直さない。</summary>
    public async Task RestoreAsync(SlipLineViewModel line, string? supplierCode, decimal costPrice)
        => line.ApplySupplier(supplierCode, await FindNameAsync(supplierCode), costPrice);

    /// <summary>仕入先欄で Space を押したとき、仕入先検索モーダルから選んだ仕入先の原価に引き直す。</summary>
    public async Task OpenLookupAsync(SlipLineViewModel line)
    {
        if (line.IsBlank)
        {
            return;
        }

        var supplier = windowService.ShowDialog<SupplierMasterSearchDialog, SupplierMasterSearchDialogViewModel, Supplier>();
        if (supplier is null)
        {
            return;
        }

        line.ApplySupplier(
            supplier.SupplierCode,
            supplier.SupplierName,
            await costService.ResolveCostAsync(line.ProductCode, supplier.SupplierCode));
    }

    /// <summary>
    /// 仕入先欄の入力文字を確定する（Enter・保存前）。空欄は仕入先なし（標準原価）。確定済みと同じなら何もしない。
    /// </summary>
    /// <returns>見つからないときのメッセージ。確定できたら <c>null</c>。</returns>
    public async Task<string?> ConfirmAsync(SlipLineViewModel line)
    {
        if (line.IsBlank || line.IsSupplierConfirmed)
        {
            return null;
        }

        var code = line.SupplierCode.Trim();
        if (code.Length == 0)
        {
            line.ApplySupplier(null, null, await costService.ResolveCostAsync(line.ProductCode, null));
            return null;
        }

        var supplier = await supplierService.GetActiveByCodeAsync(code);
        if (supplier is null)
        {
            return $"仕入先コード「{code}」が見つかりません。";
        }

        line.ApplySupplier(
            supplier.SupplierCode,
            supplier.SupplierName,
            await costService.ResolveCostAsync(line.ProductCode, supplier.SupplierCode));
        return null;
    }

    private async Task<string?> FindNameAsync(string? supplierCode)
        => string.IsNullOrWhiteSpace(supplierCode)
            ? null
            : (await supplierService.GetByCodeAsync(supplierCode))?.SupplierName;
}
