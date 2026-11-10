using System.Globalization;
using bmcs_app.Application.Common;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace bmcs_app.Application.Master;

/// <summary>プレビュー行の判定状態。</summary>
public enum ProductImportStatus
{
    /// <summary>未登録の商品コード。新規登録する。</summary>
    New,
    /// <summary>登録済みの商品コード。名称・JAN・標準原価・定価を上書きする。</summary>
    Update,
    /// <summary>取込不可（<see cref="ProductImportRow.Reason"/> に理由）。</summary>
    Error,
}

/// <summary>Excelの1行。<see cref="Status"/> は <see cref="ProductExcelImportService.ReadAsync"/> で決まる。</summary>
public sealed record ProductImportRow(
    int RowNumber,
    string ProductCode,
    string ProductName,
    string? JanCode,
    decimal CostPrice,
    decimal CatalogPrice,
    ProductImportStatus Status,
    string? Reason);

/// <summary>読込結果。<see cref="FileError"/> が非nullならファイル全体が取込不可。</summary>
public sealed record ProductImportReadResult(string? FileError, IReadOnlyList<ProductImportRow> Rows);

/// <summary>
/// 商品マスタのExcel取込（docs/design_document.md 38章）。1行目の見出し名で列を特定し、
/// 全件を1回の SaveChanges（＝1トランザクション）で新規登録・上書き更新する。
/// </summary>
public class ProductExcelImportService(
    BmcsDbContext dbContext,
    ICurrentEmployeeContext currentEmployeeContext,
    ILogger<ProductExcelImportService> logger)
{
    private const string CodeHeader = "商品コード";
    private const string BrandHeader = "ブランド名";
    private const string NameHeader = "商品名";
    private const string ModelHeader = "品番";
    private const string JanHeader = "JANコード";
    private const string CatalogHeader = "表示定価";
    private const string CostHeader = "最新価格相当";
    private static readonly string[] RequiredHeaders = [CodeHeader, BrandHeader, NameHeader, ModelHeader, JanHeader, CatalogHeader, CostHeader];

    /// <summary>Excelを読み、行ごとに検証して新規／更新／エラーを判定する。</summary>
    public async Task<ProductImportReadResult> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        // Excelで開いたままでも読めるよう、共有読み取りで開く。
        var parsed = await Task.Run(() =>
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Parse(stream);
        }, cancellationToken);
        if (parsed.FileError is not null)
        {
            return parsed;
        }

        var codes = parsed.Rows.Where(r => r.Status != ProductImportStatus.Error).Select(r => r.ProductCode).ToList();
        var existing = await dbContext.Products
            .AsNoTracking()
            .Where(p => codes.Contains(p.ProductCode))
            .Select(p => new { p.ProductCode, p.IsDeleted })
            .ToDictionaryAsync(p => p.ProductCode, p => p.IsDeleted, cancellationToken);

        var rows = parsed.Rows.Select(r => r.Status == ProductImportStatus.Error ? r
            : existing.TryGetValue(r.ProductCode, out var deleted)
                ? r with { Status = ProductImportStatus.Update, Reason = deleted ? "無効化済みの商品（更新後も無効のまま）" : null }
                : r).ToList();
        return new ProductImportReadResult(null, rows);
    }

    /// <summary>
    /// ブックを解析する（DBを見ない）。エラーでない行は <see cref="ProductImportStatus.New"/> で返す。
    /// 見出しがすべて揃った最初のシートを対象にする。
    /// </summary>
    public static ProductImportReadResult Parse(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        foreach (var sheet in workbook.Worksheets)
        {
            var columns = sheet.Row(1).CellsUsed()
                .Select(c => (Header: Clean(c), c.Address.ColumnNumber))
                .Where(h => h.Header.Length > 0)
                .GroupBy(h => h.Header)
                .ToDictionary(g => g.Key, g => g.First().ColumnNumber);
            if (RequiredHeaders.All(columns.ContainsKey))
            {
                return new ProductImportReadResult(null, ParseRows(sheet, columns));
            }
        }

        return new ProductImportReadResult($"見出し（{string.Join("・", RequiredHeaders)}）が揃ったシートがありません。", []);
    }

    private static List<ProductImportRow> ParseRows(IXLWorksheet sheet, Dictionary<string, int> columns)
    {
        var rows = new List<ProductImportRow>();
        var seen = new HashSet<string>();
        foreach (var row in sheet.RowsUsed().Where(r => r.RowNumber() > 1))
        {
            string Text(string header) => Clean(row.Cell(columns[header]));
            var code = Text(CodeHeader);
            var name = string.Join(" ", new[] { Text(BrandHeader), Text(NameHeader), Text(ModelHeader) }.Where(s => s.Length > 0));
            var jan = Text(JanHeader);
            var cost = ParsePrice(row.Cell(columns[CostHeader]));
            var catalog = ParsePrice(row.Cell(columns[CatalogHeader]));
            if (code.Length == 0 && name.Length == 0)
            {
                continue; // 書式だけ残った空行
            }

            string? error =
                code.Length == 0 ? "商品コードが空です。"
                : code.Length > 20 ? "商品コードが20文字を超えています。"
                : !seen.Add(code) ? "商品コードがファイル内で重複しています。"
                : name.Length == 0 ? "商品名が空です。"
                : name.Length > 200 ? $"商品名が200文字を超えています（{name.Length}文字）。"
                : jan.Length > 13 ? "JANコードが13文字を超えています。"
                : cost is null ? $"{CostHeader}が0以上の数値ではありません。"
                : catalog is null ? $"{CatalogHeader}が0以上の数値ではありません。"
                : null;
            rows.Add(new ProductImportRow(
                row.RowNumber(), code, name, jan.Length == 0 ? null : jan, cost ?? 0, catalog ?? 0,
                error is null ? ProductImportStatus.New : ProductImportStatus.Error, error));
        }

        return rows;
    }

    /// <summary>セルの値を文字列にして前後の空白（全角含む）を除く。数値は指数表記にせず、文字列「NULL」は空とみなす。</summary>
    private static string Clean(IXLCell cell)
    {
        var value = cell.Value;
        var text = value.IsNumber ? ((decimal)value.GetNumber()).ToString("0.####", CultureInfo.InvariantCulture)
            : value.IsBlank ? string.Empty
            : value.IsText ? value.GetText()
            : value.ToString();
        text = text.Trim(' ', '　');
        return text.Equals("NULL", StringComparison.OrdinalIgnoreCase) ? string.Empty : text;
    }

    /// <summary>0以上の数値（decimal(15,4)に丸める）。空・数値でない・負なら null。</summary>
    private static decimal? ParsePrice(IXLCell cell)
    {
        decimal? price = cell.Value.IsNumber ? (decimal)cell.Value.GetNumber()
            : decimal.TryParse(Clean(cell), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d
            : null;
        return price >= 0 ? Math.Round(price.Value, 4) : null;
    }

    /// <summary>
    /// エラー以外の行を新規登録・上書き更新する。全件を1回の SaveChanges で保存する（1件でも失敗すれば何も反映しない）。
    /// 新規か更新かはプレビュー時点ではなく、ここで登録状況を見直して決める。
    /// </summary>
    public async Task<(int Inserted, int Updated)> ImportAsync(IReadOnlyList<ProductImportRow> rows, CancellationToken cancellationToken = default)
    {
        var targets = rows.Where(r => r.Status != ProductImportStatus.Error).ToList();
        var codes = targets.Select(r => r.ProductCode).ToList();
        var existing = await dbContext.Products
            .Where(p => codes.Contains(p.ProductCode))
            .ToDictionaryAsync(p => p.ProductCode, cancellationToken);

        var employeeCode = currentEmployeeContext.EmployeeCode;
        var now = DateTime.Now;
        var inserted = 0;
        foreach (var row in targets)
        {
            if (existing.TryGetValue(row.ProductCode, out var product))
            {
                product.ProductName = row.ProductName;
                product.JanCode = row.JanCode;
                product.StandardCostPrice = row.CostPrice;
                product.CatalogPrice = row.CatalogPrice;
                product.UpdatedBy = employeeCode;
                product.UpdatedAt = now;
                continue;
            }

            dbContext.Products.Add(new Product
            {
                ProductCode = row.ProductCode,
                ProductName = row.ProductName,
                JanCode = row.JanCode,
                StandardUnitPriceExclTax = 0,
                StandardUnitPriceInclTax = 0,
                StandardCostPrice = row.CostPrice,
                CatalogPrice = row.CatalogPrice,
                TaxCategory = TaxCategory.Standard,
                IsDeleted = false,
                CreatedBy = employeeCode,
                CreatedAt = now,
                UpdatedBy = employeeCode,
                UpdatedAt = now,
            });
            inserted++;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // 失敗した追加・変更を追跡に残すと、再実行時に二重に登録しようとする。
            dbContext.ChangeTracker.Clear();
            throw;
        }

        var updated = targets.Count - inserted;
        logger.LogInformation("商品マスタをExcelから取り込みました。新規={Inserted} 更新={Updated}", inserted, updated);
        return (inserted, updated);
    }
}
