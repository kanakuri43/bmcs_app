using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Calculations;

/// <summary>
/// 消費税計算の共通ロジック（TODO.md 5-1）。売上入力・返品値引・請求締め・明細請求書発行の
/// 4箇所から使う想定のため、端数処理の規則をここに集約する。
///
/// 得意先の税区分（<see cref="TaxUnit"/>）ごとに保存すべき値の個数が異なる
/// （請求単位＝請求ごとに1値、伝票単位＝伝票ごとに1値、内税明細単位＝明細行ごとにN値）ため、
/// <see cref="TaxUnit"/> で分岐する単一の入口は用意せず、用途ごとに明示的なメソッドを公開する。
///
/// インボイス制度の原則は「税率ごとに区分した消費税額は1枚の請求書につき税率ごとに1回」の
/// 端数処理だが、伝票単位・内税明細単位の得意先は構造上これを満たせない
/// （<c>docs/design_document.md</c> 2章で税理士確認待ちの【要確認】として記録済み）。
/// TODO.md の暫定決定 C-4b「得意先マスタの税区分どおりに計算する」に従い、
/// 伝票単位は伝票ごと、内税明細単位は明細行ごとに端数処理する
/// （<see cref="CalculateExternalTaxPerSlip"/> / <see cref="CalculateInternalTaxPerLine"/>）。
/// 将来方針が変わった場合は、抽象化を挟まずこのクラスと対応するテストを直接修正する。
/// </summary>
public static class ConsumptionTaxCalculator
{
    // ---- 外税（TaxUnit.Invoice / TaxUnit.Slip） ----

    /// <summary>
    /// 1グループ（請求単位＝請求期間全体／伝票単位＝1伝票）について、
    /// (<see cref="TaxCategory"/>, 税率) ごとに1回だけ端数処理した内訳を返す。
    /// 戻り値は TaxCategory, TaxRate の昇順で安定ソートされる。
    /// </summary>
    public static IReadOnlyList<TaxRateBucket> CalculateExternalTaxBuckets(
        IEnumerable<TaxLine> lines, RoundingType roundingType)
    {
        return lines
            .GroupBy(l => (l.TaxCategory, l.TaxRate))
            .Select(g =>
            {
                var taxable = g.Sum(l => l.Amount);
                var tax = g.Key.TaxCategory == TaxCategory.TaxExempt
                    ? 0m
                    : TaxRounding.RoundToYen(taxable * g.Key.TaxRate / 100m, roundingType);
                return new TaxRateBucket(g.Key.TaxCategory, g.Key.TaxRate, taxable, tax);
            })
            .OrderBy(b => b.TaxCategory)
            .ThenBy(b => b.TaxRate)
            .ToList();
    }

    /// <summary>上記を請求データ／明細請求書の固定5カラム形に畳んだもの。</summary>
    public static TaxSummary CalculateExternalTax(IEnumerable<TaxLine> lines, RoundingType roundingType)
        => ToSummary(CalculateExternalTaxBuckets(lines, roundingType));

    /// <summary>
    /// 伝票単位の得意先の消費税額（<c>sales_tax_unit_slip.slip_tax_amount</c>）。
    /// 同一伝票の全行に同じ値を書き込む。SUM してはいけない
    /// （<c>docs/database-schema.md</c> 2.8節）。
    /// </summary>
    public static decimal CalculateSlipTaxAmount(IEnumerable<TaxLine> slipLines, RoundingType roundingType)
        => CalculateExternalTax(slipLines, roundingType).TaxAmount;

    /// <summary>
    /// 伝票単位の得意先の請求締め用。伝票ごとに確定した税額を積み上げる
    /// （請求全体で1回だけ丸め直すのではない。暫定 C-4b）。
    /// </summary>
    public static TaxSummary CalculateExternalTaxPerSlip(
        IEnumerable<IEnumerable<TaxLine>> slips, RoundingType roundingType)
        => slips
            .Select(slip => CalculateExternalTax(slip, roundingType))
            .Aggregate(TaxSummary.Zero, (acc, next) => acc + next);

    // ---- 内税（TaxUnit.Line） ----

    /// <summary>
    /// 内税明細1行の消費税額（<c>sales_tax_unit_line.tax_amount</c>）。
    /// <see cref="TaxLine.Amount"/> は税込金額。非課税は税率を見ずに0を返す。
    /// </summary>
    public static decimal CalculateInternalTaxAmount(TaxLine line, RoundingType roundingType)
    {
        if (line.TaxCategory == TaxCategory.TaxExempt)
        {
            return 0m;
        }

        var rawTax = line.Amount * line.TaxRate / (100m + line.TaxRate);
        return TaxRounding.RoundToYen(rawTax, roundingType);
    }

    /// <summary>内税明細1行の内訳（税抜対価額と税額）。</summary>
    public static TaxRateBucket CalculateInternalTaxBucket(TaxLine line, RoundingType roundingType)
    {
        var tax = CalculateInternalTaxAmount(line, roundingType);
        return new TaxRateBucket(line.TaxCategory, line.TaxRate, line.Amount - tax, tax);
    }

    /// <summary>
    /// 明細請求書用。明細行ごとに確定した税額を積み上げる（暫定 C-4b）。
    /// </summary>
    public static TaxSummary CalculateInternalTaxPerLine(IEnumerable<TaxLine> lines, RoundingType roundingType)
        => ToSummary(lines.Select(l => CalculateInternalTaxBucket(l, roundingType)));

    // ---- 帳票印字用（TODO.md 10-5） ----

    /// <summary>
    /// 確定済みの固定5カラム（<see cref="TaxSummary"/>）に、明細行から拝借した適用税率(%)を
    /// 付与して印字用の内訳（<see cref="TaxRateBucket"/>）へ組み立て直す。
    /// <c>billing</c>／<c>detail_invoice</c>は税種別区分ごとの確定金額のみを保持し、
    /// 税率(%)そのものは持たない。一方、明細を構成する<c>sales</c>行は税単位によらず必ず
    /// <c>tax_rate</c>をスナップショットとして持つため、金額は確定値をそのまま使い、
    /// 税率ラベルだけを該当する税種別区分を持つ明細行から拝借する。こうすることで、
    /// 伝票単位（伝票ごとに端数処理）と請求全体の再集計との二重丸めによる金額不一致を避けつつ、
    /// 適格請求書の法定記載事項である税率(%)を表示できる（TODO.md 10-5設計判断）。
    /// 対価額・税額がともに0の区分（非課税は対価額のみ）は出力しない
    /// （<see cref="CalculateExternalTaxBuckets"/>と同じ「0円の区分は載せない」扱い）。
    /// </summary>
    public static IReadOnlyList<TaxRateBucket> ResolveConfirmedBuckets(TaxSummary summary, IEnumerable<TaxLine> lines)
    {
        var rateByCategory = lines
            .GroupBy(l => l.TaxCategory)
            .ToDictionary(g => g.Key, g => g.First().TaxRate);

        var buckets = new List<TaxRateBucket>();

        if (summary.StandardRateTaxableAmount != 0m || summary.StandardRateTaxAmount != 0m)
        {
            rateByCategory.TryGetValue(TaxCategory.Standard, out var rate);
            buckets.Add(new TaxRateBucket(
                TaxCategory.Standard, rate, summary.StandardRateTaxableAmount, summary.StandardRateTaxAmount));
        }

        if (summary.ReducedRateTaxableAmount != 0m || summary.ReducedRateTaxAmount != 0m)
        {
            rateByCategory.TryGetValue(TaxCategory.Reduced, out var rate);
            buckets.Add(new TaxRateBucket(
                TaxCategory.Reduced, rate, summary.ReducedRateTaxableAmount, summary.ReducedRateTaxAmount));
        }

        if (summary.TaxExemptAmount != 0m)
        {
            buckets.Add(new TaxRateBucket(TaxCategory.TaxExempt, 0m, summary.TaxExemptAmount, 0m));
        }

        return buckets;
    }

    // ---- 共通 ----

    /// <summary>
    /// 明細金額 = 数量 × 単価。数量(decimal 13,3)×単価(decimal 15,4)は最大7桁の小数になるが
    /// <c>amount</c> カラムは decimal(15,2) のため、得意先の端数区分で1円単位に丸めてから保存する
    /// （丸めずに保存すると SQL Server 側で端数区分を無視した四捨五入が起きる）。
    /// </summary>
    public static decimal CalculateLineAmount(decimal quantity, decimal unitPrice, RoundingType roundingType)
        => TaxRounding.RoundToYen(quantity * unitPrice, roundingType);

    /// <summary>
    /// 確定済み内訳を固定5カラム形に畳む。未知の <see cref="TaxCategory"/> は例外にする
    /// （DB側は3区分の固定カラムしか持たないため、区分を落とすと金額が静かに消える）。
    /// </summary>
    public static TaxSummary ToSummary(IEnumerable<TaxRateBucket> buckets)
    {
        var summary = TaxSummary.Zero;

        foreach (var bucket in buckets)
        {
            summary = bucket.TaxCategory switch
            {
                TaxCategory.Standard => summary with
                {
                    StandardRateTaxableAmount = summary.StandardRateTaxableAmount + bucket.TaxableAmount,
                    StandardRateTaxAmount = summary.StandardRateTaxAmount + bucket.TaxAmount,
                },
                TaxCategory.Reduced => summary with
                {
                    ReducedRateTaxableAmount = summary.ReducedRateTaxableAmount + bucket.TaxableAmount,
                    ReducedRateTaxAmount = summary.ReducedRateTaxAmount + bucket.TaxAmount,
                },
                TaxCategory.TaxExempt => summary with
                {
                    TaxExemptAmount = summary.TaxExemptAmount + bucket.TaxableAmount,
                },
                _ => throw new ArgumentOutOfRangeException(
                    nameof(buckets), bucket.TaxCategory, "未対応の税種別区分です。"),
            };
        }

        return summary;
    }
}
