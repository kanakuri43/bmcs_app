namespace bmcs_app.Domain.Enums;

/// <summary>得意先元帳の1行の区分。売上・返品・値引の内訳は <see cref="SlipType"/> が別に持つ。</summary>
public enum LedgerEntryKind : byte
{
    /// <summary>前月繰越。期間の直前日に置く仮想行。</summary>
    OpeningBalance = 1,

    /// <summary>売上・返品・値引（内訳は <see cref="SlipType"/>）。</summary>
    Sales = 2,

    /// <summary>消費税（請求確定分・伝票単位・未締め仮計算のいずれか）。</summary>
    ConsumptionTax = 3,

    /// <summary>入金。</summary>
    Receipt = 4,
}
