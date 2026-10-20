using bmcs_app.Application.Common;
using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace bmcs_app.Application.Master;

/// <summary>
/// 受注・売上入力の単価の初期値を解決する。得意先の単価マスタ・定価掛率マスタと税率マスタを1回ずつ読み、
/// 商品ごとの決定は <see cref="IUnitPriceCalculator"/> に任せる。商品検索モーダルは全商品分を一覧表示するため、
/// 読み込みを <see cref="CreateResolverAsync"/> の1回にまとめている。
/// </summary>
public class UnitPriceService(
    BmcsDbContext dbContext,
    TaxRateQueryService taxRateQueryService,
    IUnitPriceCalculator unitPriceCalculator)
{
    /// <param name="customer">対象得意先。未選択（null）のときはマスタを読まず、商品マスタの外税単価を返す。</param>
    /// <param name="slipDate">伝票日付。内税得意先の掛率計算で使う税率の適用日。</param>
    public async Task<UnitPriceResolver> CreateResolverAsync(
        Customer? customer, DateOnly slipDate, CancellationToken cancellationToken = default)
    {
        if (customer is null)
        {
            return new UnitPriceResolver(
                unitPriceCalculator, null, slipDate,
                new Dictionary<string, CustomerProductPrice>(), new Dictionary<string, CustomerProductRate>(), []);
        }

        var prices = await dbContext.CustomerProductPrices
            .AsNoTracking()
            .Where(p => p.CustomerCode == customer.CustomerCode && !p.IsDeleted)
            .ToDictionaryAsync(p => p.ProductCode, cancellationToken);
        var rates = await dbContext.CustomerProductRates
            .AsNoTracking()
            .Where(r => r.CustomerCode == customer.CustomerCode && !r.IsDeleted)
            .ToDictionaryAsync(r => r.ProductCode, cancellationToken);
        var taxRateMasters = await taxRateQueryService.GetAllAsync(cancellationToken);

        return new UnitPriceResolver(unitPriceCalculator, customer, slipDate, prices, rates, taxRateMasters);
    }
}

/// <summary><see cref="UnitPriceService.CreateResolverAsync"/> が返す、得意先・伝票日付を固定した単価の解決器。</summary>
public sealed class UnitPriceResolver(
    IUnitPriceCalculator unitPriceCalculator,
    Customer? customer,
    DateOnly slipDate,
    IReadOnlyDictionary<string, CustomerProductPrice> prices,
    IReadOnlyDictionary<string, CustomerProductRate> rates,
    IReadOnlyList<TaxRateMaster> taxRateMasters)
{
    public decimal Resolve(Product product)
    {
        prices.TryGetValue(product.ProductCode, out var price);
        rates.TryGetValue(product.ProductCode, out var rate);

        // 税率は内税得意先の掛率計算でしか使わないので、必要なときだけ解決する
        // （税率マスタが無い環境でも、それ以外の単価決定は例外にしない）。
        var taxRate = rate is not null && price is null && customer?.TaxUnit == TaxUnit.Line
            ? TaxRateResolver.ResolveRate(taxRateMasters, slipDate, product.TaxCategory)
            : 0m;

        return unitPriceCalculator.SelectUnitPrice(product, customer, price, rate, taxRate);
    }
}
