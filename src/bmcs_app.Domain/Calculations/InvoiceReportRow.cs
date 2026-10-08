namespace bmcs_app.Domain.Calculations;

/// <summary>
/// 請求書明細の表示行の種類（親子請求）。
/// </summary>
public enum InvoiceReportRowKind
{
    /// <summary>通常の売上明細行。</summary>
    Line,

    /// <summary>請求集約グループ内の得意先境界を示す見出し行。</summary>
    GroupHeader,

    /// <summary>その得意先の明細の合計を示す小計行。</summary>
    GroupSubtotal,
}

/// <summary>
/// <see cref="InvoiceReportRowBuilder.Build"/>が返す表示行1件。<see cref="Kind"/>によって
/// 有効なプロパティが変わる（<see cref="Line"/>なら<see cref="SourceLineIndex"/>、
/// <see cref="GroupHeader"/>／<see cref="GroupSubtotal"/>なら<see cref="Label"/>。
/// <see cref="GroupSubtotal"/>のみ<see cref="SubtotalAmount"/>も設定される）。
/// </summary>
public sealed record InvoiceReportRow
{
    public required InvoiceReportRowKind Kind { get; init; }

    /// <summary><see cref="InvoiceReportRowKind.Line"/>のときだけ有効。元の明細配列内の位置。</summary>
    public int? SourceLineIndex { get; init; }

    /// <summary><see cref="InvoiceReportRowKind.GroupHeader"/>／<see cref="GroupSubtotal"/>の表示文言。</summary>
    public string? Label { get; init; }

    /// <summary><see cref="InvoiceReportRowKind.GroupSubtotal"/>のときだけ有効。その得意先の明細金額の合計。</summary>
    public decimal? SubtotalAmount { get; init; }
}
