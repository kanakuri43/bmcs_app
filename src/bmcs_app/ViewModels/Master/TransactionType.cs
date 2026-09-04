namespace bmcs_app.ViewModels.Master;

/// <summary>
/// 画面専用の派生概念。DB は customer.closing_day / tax_unit の2列のまま持つ
/// （docs/database-schema.md）。この enum はその2列の組み合わせを
/// 画面上「都度取引」「締め取引」の1択として扱うためだけのもので、DBには存在しない。
/// </summary>
public enum TransactionType
{
    /// <summary>都度取引。ClosingDay=0・TaxUnit=Line に固定。</summary>
    OneOff,

    /// <summary>締め取引。ClosingDay=1〜31・99・TaxUnit=Invoice/Slip から選択。</summary>
    Closing,
}
