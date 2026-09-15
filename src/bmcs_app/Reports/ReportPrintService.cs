using System.Printing;
using System.Windows.Controls;
using System.Windows.Documents;
using bmcs_app.Application.Common;
using Microsoft.Extensions.Logging;

namespace bmcs_app.Reports;

/// <summary>帳票種別。<see cref="PrinterSettings"/>（TODO.md 2-6）の3項目に一致させる。</summary>
public enum ReportKind
{
    DeliveryNote,
    Invoice,
    DetailInvoice,
}

/// <summary>印刷・PDF出力の結果。呼び出し元（ViewModel）が <c>StatusMessage</c> に表示する。</summary>
public readonly record struct ReportPrintResult(bool Success, string? Message);

/// <summary>
/// 帳票の印刷・PDF出力（TODO.md 10-3、M-10確定方式）。<see cref="PrinterSettingsService"/>
/// （Application、Singleton）と同じ Singleton で登録する（DbContext に依存しないため）。
/// 設定済みプリンタへダイアログなしで直接送信し、失敗時のみ <see cref="PrintDialog"/> に
/// フォールバックする（docs/report-spec.md 1章）。エラー表示（MessageBox 等）は行わず、
/// 結果を <see cref="ReportPrintResult"/> で返して呼び出し元に委ねる。
/// </summary>
public class ReportPrintService(PrinterSettingsService printerSettingsService, ILogger<ReportPrintService> logger)
{
    private const string PrintToPdfQueueName = "Microsoft Print to PDF";

    /// <summary>
    /// 設定済みプリンタへ直接印刷する。未設定、または送信に失敗した場合のみ
    /// 印刷ダイアログを表示してユーザーにプリンタを選ばせる
    /// （「ダイアログを出さない」方針の唯一の例外。docs/report-spec.md 1章）。
    /// </summary>
    public ReportPrintResult Print(FixedDocument document, ReportKind kind, string jobName)
    {
        var printerName = ResolvePrinterName(kind);

        if (!string.IsNullOrWhiteSpace(printerName))
        {
            try
            {
                var dialog = new PrintDialog { PrintQueue = new PrintQueue(new LocalPrintServer(), printerName) };
                ApplyA4Portrait(dialog, document);
                dialog.PrintDocument(document.DocumentPaginator, jobName);
                return new ReportPrintResult(true, $"「{printerName}」へ印刷しました。");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "設定済みプリンタ「{PrinterName}」への送信に失敗しました。印刷ダイアログにフォールバックします。", printerName);
            }
        }

        var fallback = new PrintDialog();
        if (fallback.ShowDialog() != true)
        {
            return new ReportPrintResult(false, "印刷をキャンセルしました。");
        }

        ApplyA4Portrait(fallback, document);
        fallback.PrintDocument(document.DocumentPaginator, jobName);
        return new ReportPrintResult(true, $"「{fallback.PrintQueue.FullName}」へ印刷しました。");
    }

    /// <summary>
    /// 「Microsoft Print to PDF」へ印刷する。保存先の選択はこの仮想プリンタが表示する
    /// OS 標準の保存ダイアログに任せる（アプリ側でファイルパスを扱わない。docs/report-spec.md 1章）。
    /// ユーザーが OS の保存ダイアログでキャンセルしたかどうかはアプリ側から検知できないため、
    /// 戻り値の成功はあくまで「ジョブを送信できたか」を意味する（発行記録のカウントには使わない）。
    /// </summary>
    public ReportPrintResult PrintToPdf(FixedDocument document, string jobName)
    {
        var server = new LocalPrintServer();
        var pdfQueue = server
            .GetPrintQueues([EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections])
            .FirstOrDefault(q => q.FullName == PrintToPdfQueueName);

        if (pdfQueue is null)
        {
            return new ReportPrintResult(false, $"「{PrintToPdfQueueName}」が見つかりません。Windowsの機能から追加してください。");
        }

        var dialog = new PrintDialog { PrintQueue = pdfQueue };
        ApplyA4Portrait(dialog, document);
        dialog.PrintDocument(document.DocumentPaginator, jobName);
        return new ReportPrintResult(true, "PDF保存ダイアログを表示しました。");
    }

    /// <summary>
    /// プリンタの既定設定（Letter 等）に依存せず A4・縦向きで出力する。
    /// 未設定のままだと、既定用紙が Letter の端末で縮小・欠けが起きる。
    /// </summary>
    private static void ApplyA4Portrait(PrintDialog dialog, FixedDocument document)
    {
        dialog.PrintTicket.PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4);
        dialog.PrintTicket.PageOrientation = PageOrientation.Portrait;
        document.DocumentPaginator.PageSize = new System.Windows.Size(
            ReportDocumentBuilder.A4Width, ReportDocumentBuilder.A4Height);
    }

    private string? ResolvePrinterName(ReportKind kind)
    {
        var settings = printerSettingsService.Load();
        return kind switch
        {
            ReportKind.DeliveryNote => settings.DeliverySlipPrinter,
            ReportKind.Invoice => settings.InvoicePrinter,
            ReportKind.DetailInvoice => settings.LineInvoicePrinter,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未対応の帳票種別です。"),
        };
    }
}
