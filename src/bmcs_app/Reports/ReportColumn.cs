using System.Windows;

namespace bmcs_app.Reports;

/// <summary>明細テーブルの列揃え。</summary>
public enum ReportColumnAlign
{
    Left,
    Center,
    Right,
}

/// <summary>
/// 帳票の明細テーブルの列定義。<paramref name="Width"/> に0を指定した列は残余幅（最大1列まで）になる。
/// </summary>
public sealed record ReportColumn(string Header, double Width, ReportColumnAlign Align)
{
    public TextAlignment ToTextAlignment() => Align switch
    {
        ReportColumnAlign.Center => TextAlignment.Center,
        ReportColumnAlign.Right => TextAlignment.Right,
        _ => TextAlignment.Left,
    };
}
