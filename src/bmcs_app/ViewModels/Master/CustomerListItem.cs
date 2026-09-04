namespace bmcs_app.ViewModels.Master;

/// <summary>得意先一覧の表示用行。表示文字列の組み立ては Presentation 層の責務。</summary>
public sealed record CustomerListItem(
    string CustomerCode,
    string CustomerName,
    string? CustomerNameKana,
    string ClosingDayDisplay,
    string TaxUnitDisplay);
