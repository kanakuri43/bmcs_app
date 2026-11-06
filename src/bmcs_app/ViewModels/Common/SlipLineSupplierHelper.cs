using bmcs_app.Application.Master;

namespace bmcs_app.ViewModels.Common;

/// <summary>
/// 伝票明細行の仕入先・原価の解決。受注入力・売上入力で共用する。
/// 商品検索モーダルを通る経路は検索の時点で仕入先と原価が決まるので、ここが扱うのは
/// 検索モーダルを通らない経路（商品コードの直接入力＝主仕入先、保存済み明細の読込）だけ。
/// </summary>
public sealed class SlipLineSupplierHelper(
    SupplierService supplierService,
    ProductSupplierCostService costService)
{
    /// <summary>商品コードの直接入力の仕入先と原価。商品の主仕入先とその原価（有効な原価行がなければ標準原価）。</summary>
    public async Task<(string? SupplierCode, string? SupplierName, decimal CostPrice)> ResolveDefaultAsync(string productCode)
    {
        var (supplierCode, costPrice) = await costService.ResolveDefaultAsync(productCode);
        return (supplierCode, await FindNameAsync(supplierCode), costPrice);
    }

    /// <summary>保存済みの明細（受注から売上化・複写・訂正の読込）の仕入先と原価をそのまま行へ設定する。原価は引き直さない。</summary>
    public async Task RestoreAsync(SlipLineViewModel line, string? supplierCode, decimal costPrice)
        => line.ApplySupplier(supplierCode, await FindNameAsync(supplierCode), costPrice);

    // 無効化済みの仕入先も名称を引く（訂正で読み込んだ明細の表示用）。
    private async Task<string?> FindNameAsync(string? supplierCode)
        => string.IsNullOrWhiteSpace(supplierCode)
            ? null
            : (await supplierService.GetByCodeAsync(supplierCode))?.SupplierName;
}
