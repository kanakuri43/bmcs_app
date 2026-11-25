using System.Globalization;
using bmcs_app.Application.Common;
using bmcs_app.Domain.Entities;
using bmcs_app.Infrastructure;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace bmcs_app.Application.Master;

/// <summary>プレビュー行の判定状態。</summary>
public enum PriceImportStatus
{
    /// <summary>有効な登録を上書きしない（未登録・無効化済みへの追加／復活だけ）。</summary>
    New,
    /// <summary>有効な単価または掛率の登録を、Excelの値で上書きする。</summary>
    Update,
    /// <summary>取込不可（<see cref="PriceImportRow.Reason"/> に理由）。</summary>
    Error,
}

/// <summary>
/// Excelの1行。<see cref="UnitPrice"/>・<see cref="Rate"/> は空欄（＝変更しない）か、解析できなければ null。
/// 名称はDBとの突き合わせで見つかったときだけ入る。
/// </summary>
public sealed record PriceImportRow(
    int RowNumber,
    string CustomerCode,
    string ProductCode,
    decimal? UnitPrice,
    int? Rate,
    string? CustomerName,
    string? ProductName,
    PriceImportStatus Status,
    string? Reason);

/// <summary>読込結果。<see cref="FileError"/> が非nullならファイル全体が取込不可。</summary>
public sealed record PriceImportReadResult(string? FileError, IReadOnlyList<PriceImportRow> Rows);

/// <summary>
/// 得意先／商品／単価・掛率マスタのExcel取込（docs/design_document.md 41章）。1行目の見出し名で列を特定し、
/// 値のある単価・掛率を全件1トランザクションで追加・上書きする（空欄は変更しない。Excelからの無効化はしない）。
/// </summary>
public class CustomerProductPriceExcelImportService(
    BmcsDbContext dbContext,
    ICurrentEmployeeContext currentEmployeeContext,
    ILogger<CustomerProductPriceExcelImportService> logger)
{
    /// <summary>テンプレートは2〜4行目が説明・入力例のため、データは5行目から読む。</summary>
    private const int FirstDataRow = 5;

    private static readonly string[] RequiredHeaders = ["得意先コード", "商品コード", "単価", "掛率"];

    /// <summary>Excelを読み、行ごとに検証して新規／更新／エラーを判定する。</summary>
    public async Task<PriceImportReadResult> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        // Excelで開いたままでも読めるよう、共有読み取りで開く。
        var parsed = await Task.Run(() =>
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Parse(stream);
        }, cancellationToken);
        return parsed.FileError is not null
            ? parsed
            : new PriceImportReadResult(null, await ValidateAgainstDbAsync(parsed.Rows, cancellationToken));
    }

    /// <summary>ブックを解析する（DBを見ない）。見出しが揃った最初のシートを対象にする。</summary>
    public static PriceImportReadResult Parse(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        return ExcelImportReader.FindSheet(workbook, RequiredHeaders) is var (sheet, columns)
            ? new PriceImportReadResult(null, ParseRows(sheet, columns))
            : new PriceImportReadResult(ExcelImportReader.MissingHeadersMessage(RequiredHeaders), []);
    }

    private static List<PriceImportRow> ParseRows(IXLWorksheet sheet, Dictionary<string, int> columns)
    {
        var rows = new List<PriceImportRow>();
        var seen = new HashSet<(string, string)>();
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        for (var r = FirstDataRow; r <= lastRow; r++)
        {
            var customerCode = ExcelImportReader.Text(sheet, columns, r, "得意先コード");
            var productCode = ExcelImportReader.Text(sheet, columns, r, "商品コード");
            var price = ExcelImportReader.Text(sheet, columns, r, "単価");
            var rate = ExcelImportReader.Text(sheet, columns, r, "掛率");
            if (new[] { customerCode, productCode, price, rate }.All(s => s.Length == 0))
            {
                continue; // 書式・数式だけ残った空行
            }

            var priceValue = price.Length == 0 ? null : ParsePrice(price);
            int? rateValue = rate.Length == 0 ? null
                : int.TryParse(rate, NumberStyles.None, CultureInfo.InvariantCulture, out var i)
                    && i is >= CustomerProductPriceService.MinRate and <= CustomerProductPriceService.MaxRate ? i : null;

            string? error =
                customerCode.Length == 0 ? "得意先コードが空です。"
                : customerCode.Length > 10 ? "得意先コードが10文字を超えています。"
                : productCode.Length == 0 ? "商品コードが空です。"
                : productCode.Length > 20 ? "商品コードが20文字を超えています。"
                : !seen.Add((customerCode, productCode)) ? "得意先コードと商品コードの組み合わせがファイル内で重複しています。"
                : price.Length == 0 && rate.Length == 0 ? "単価と掛率が両方空です（どちらかを入力してください）。"
                : price.Length > 0 && priceValue is null ? $"単価は 0 以上 {CustomerProductPriceService.MaxUnitPrice:N4} 以下の数値（小数は4桁まで）にしてください。"
                : rate.Length > 0 && rateValue is null ? $"掛率は {CustomerProductPriceService.MinRate} 〜 {CustomerProductPriceService.MaxRate} の整数にしてください。"
                : null;

            rows.Add(new PriceImportRow(r, customerCode, productCode, priceValue, rateValue, null, null,
                error is null ? PriceImportStatus.New : PriceImportStatus.Error, error));
        }

        return rows;
    }

    /// <summary>0以上・上限以下で、小数4桁以内の数値。丸めて取り込まず、範囲外・桁超過・数値でなければ null。</summary>
    private static decimal? ParsePrice(string text)
        => decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d)
            && d >= 0m && d <= CustomerProductPriceService.MaxUnitPrice && Math.Round(d, 4) == d ? d : null;

    /// <summary>
    /// DBとの突き合わせ（得意先・商品の有効性、有効な登録の有無）。解析でエラーでない行だけを見て、
    /// 新規／更新を決め、新たにエラーになった行は理由つきで返す。プレビューと取込実行の両方から呼ぶ。
    /// </summary>
    private async Task<List<PriceImportRow>> ValidateAgainstDbAsync(IReadOnlyList<PriceImportRow> rows, CancellationToken cancellationToken)
    {
        var targets = rows.Where(r => r.Status != PriceImportStatus.Error).ToList();
        var customers = await LoadCustomerNamesAsync(targets, cancellationToken);
        var products = await LoadProductNamesAsync(targets, cancellationToken);

        var existing = new Dictionary<(string, string), (CustomerProductPrice? Price, CustomerProductRate? Rate)>();
        foreach (var group in targets.Where(r => customers.ContainsKey(r.CustomerCode)).GroupBy(r => r.CustomerCode))
        {
            var codes = group.Select(r => r.ProductCode).ToList();
            var prices = await dbContext.CustomerProductPrices.AsNoTracking()
                .Where(p => p.CustomerCode == group.Key && codes.Contains(p.ProductCode)).ToDictionaryAsync(p => p.ProductCode, cancellationToken);
            var rates = await dbContext.CustomerProductRates.AsNoTracking()
                .Where(p => p.CustomerCode == group.Key && codes.Contains(p.ProductCode)).ToDictionaryAsync(p => p.ProductCode, cancellationToken);
            foreach (var code in codes)
            {
                existing[(group.Key, code)] = (prices.GetValueOrDefault(code), rates.GetValueOrDefault(code));
            }
        }

        return rows.Select(r =>
        {
            if (r.Status == PriceImportStatus.Error)
            {
                return r;
            }

            if (!customers.TryGetValue(r.CustomerCode, out var customerName))
            {
                return r with { Status = PriceImportStatus.Error, Reason = $"得意先コード「{r.CustomerCode}」は登録されていないか、無効です。" };
            }

            if (!products.TryGetValue(r.ProductCode, out var productName))
            {
                return r with { Status = PriceImportStatus.Error, Reason = $"商品コード「{r.ProductCode}」は登録されていないか、無効です。" };
            }

            var (price, rate) = existing[(r.CustomerCode, r.ProductCode)];
            var overwrites = (r.UnitPrice is not null && price is { IsDeleted: false }) || (r.Rate is not null && rate is { IsDeleted: false });
            return r with
            {
                CustomerName = customerName,
                ProductName = productName,
                Status = overwrites ? PriceImportStatus.Update : PriceImportStatus.New,
            };
        }).ToList();
    }

    private async Task<Dictionary<string, string>> LoadCustomerNamesAsync(List<PriceImportRow> targets, CancellationToken cancellationToken)
    {
        var codes = targets.Select(r => r.CustomerCode).Distinct().ToList();
        return await dbContext.Customers.AsNoTracking()
            .Where(c => codes.Contains(c.CustomerCode) && !c.IsDeleted)
            .ToDictionaryAsync(c => c.CustomerCode, c => c.CustomerName, cancellationToken);
    }

    private async Task<Dictionary<string, string>> LoadProductNamesAsync(List<PriceImportRow> targets, CancellationToken cancellationToken)
    {
        var codes = targets.Select(r => r.ProductCode).Distinct().ToList();
        return await dbContext.Products.AsNoTracking()
            .Where(p => codes.Contains(p.ProductCode) && !p.IsDeleted)
            .ToDictionaryAsync(p => p.ProductCode, p => p.ProductName, cancellationToken);
    }

    /// <summary>
    /// エラー以外の行を取り込む。実行直前にDBを見直し、プレビュー後に新たなエラーが出た場合は何も登録せず例外にする。
    /// 値のある単価・掛率だけを、行が無ければ追加、あれば上書き（無効化済みは有効に戻す）する。空欄は触らない。
    /// 1回の SaveChanges で保存する（1件でも失敗すれば何も反映しない）。Excelが正のため RowVersion の照合はしない。
    /// </summary>
    public async Task<(int Inserted, int Updated)> ImportAsync(IReadOnlyList<PriceImportRow> rows, CancellationToken cancellationToken = default)
    {
        var targets = rows.Where(r => r.Status != PriceImportStatus.Error).ToList();
        var recheck = await ValidateAgainstDbAsync(targets, cancellationToken);
        if (recheck.FirstOrDefault(r => r.Status == PriceImportStatus.Error) is { } bad)
        {
            throw new CustomerProductPriceValidationException($"{bad.RowNumber}行目: {bad.Reason}（読込後にデータが変わりました。読み込み直してください）");
        }

        var employeeCode = currentEmployeeContext.EmployeeCode;
        var now = DateTime.Now;
        try
        {
            foreach (var group in recheck.GroupBy(r => r.CustomerCode))
            {
                var codes = group.Select(r => r.ProductCode).ToList();
                var prices = await dbContext.CustomerProductPrices
                    .Where(p => p.CustomerCode == group.Key && codes.Contains(p.ProductCode)).ToDictionaryAsync(p => p.ProductCode, cancellationToken);
                var rates = await dbContext.CustomerProductRates
                    .Where(p => p.CustomerCode == group.Key && codes.Contains(p.ProductCode)).ToDictionaryAsync(p => p.ProductCode, cancellationToken);

                foreach (var row in group)
                {
                    if (row.UnitPrice is { } unitPrice)
                    {
                        if (prices.TryGetValue(row.ProductCode, out var price))
                        {
                            price.UnitPrice = unitPrice;
                            price.IsDeleted = false;
                            price.UpdatedBy = employeeCode;
                            price.UpdatedAt = now;
                        }
                        else
                        {
                            dbContext.CustomerProductPrices.Add(new CustomerProductPrice
                            {
                                CustomerCode = row.CustomerCode,
                                ProductCode = row.ProductCode,
                                UnitPrice = unitPrice,
                                CreatedBy = employeeCode,
                                CreatedAt = now,
                                UpdatedBy = employeeCode,
                                UpdatedAt = now,
                            });
                        }
                    }

                    if (row.Rate is { } rateValue)
                    {
                        if (rates.TryGetValue(row.ProductCode, out var rate))
                        {
                            rate.CatalogPriceRate = rateValue;
                            rate.IsDeleted = false;
                            rate.UpdatedBy = employeeCode;
                            rate.UpdatedAt = now;
                        }
                        else
                        {
                            dbContext.CustomerProductRates.Add(new CustomerProductRate
                            {
                                CustomerCode = row.CustomerCode,
                                ProductCode = row.ProductCode,
                                CatalogPriceRate = rateValue,
                                CreatedBy = employeeCode,
                                CreatedAt = now,
                                UpdatedBy = employeeCode,
                                UpdatedAt = now,
                            });
                        }
                    }
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new CustomerProductPriceConcurrencyException("他のユーザーが更新しました。読み込み直してください。", ex);
        }
        finally
        {
            // 失敗した追加・変更を追跡に残すと、再実行時に二重に登録しようとする。
            dbContext.ChangeTracker.Clear();
        }

        var updated = recheck.Count(r => r.Status == PriceImportStatus.Update);
        logger.LogInformation("単価・掛率をExcelから取り込みました。新規={Inserted} 更新={Updated}", recheck.Count - updated, updated);
        return (recheck.Count - updated, updated);
    }
}
