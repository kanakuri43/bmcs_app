namespace bmcs_app.Core.Models;

/// <summary>伝票検索ダイアログ用サマリ（一覧表示・選択用）</summary>
public class SlipSummary
{
    public string   SlipNo       { get; set; } = "";
    public DateOnly SlipDate     { get; set; }
    public string   CustomerName { get; set; } = "";
}
