using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Numbering;

/// <summary>
/// 伝票番号の文字列表現（TODO.md 4-1）。DB非依存の純粋ロジックのみを持つ
/// （docs/architecture.md 2章「DBに依存しない単体テストの対象はDomainに集める」）。
/// 採番そのもの（<c>slip_number_sequences</c> への同時実行制御付きINCREMENT）は
/// Infrastructure/Application 層が担当し、ここでは関与しない。
/// </summary>
public static class SlipNumberFormatter
{
    /// <summary>
    /// 接頭辞なし・8桁ゼロ埋め10進（例: <c>00000001</c>）。2026-09-08 ユーザー確認済み。
    /// 伝票番号は varchar 列であり、一覧・元帳での並び替えが文字列ソートで行われる
    /// （<see cref="bmcs_app.Application.Common.ProductHistoryQueryService"/> 等）ため、
    /// 文字列ソート＝数値ソートになるようゼロ埋めする。8桁を超えたら自然に桁が伸びる
    /// （varchar(20) の範囲内であり、1億件到達は現実的でないため上限チェックは置かない）。
    /// </summary>
    public static string Format(long sequenceValue) => sequenceValue.ToString("D8");

    /// <summary>
    /// <see cref="SlipNumberKind"/> から <c>slip_number_sequences.sequence_key</c> の
    /// 文字列リテラルを解決する。DBのキー文字列をここ1箇所に閉じ込める。
    /// </summary>
    public static string ToSequenceKey(SlipNumberKind kind) => kind switch
    {
        SlipNumberKind.OrderSlip => "order_slip",
        SlipNumberKind.SalesSlip => "sales_slip",
        SlipNumberKind.ReceiptSlip => "receipt_slip",
        SlipNumberKind.DetailReceipt => "detail_receipt",
        SlipNumberKind.Billing => "billing",
        SlipNumberKind.DetailInvoice => "detail_invoice",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知の採番系列です。"),
    };
}
