namespace bmcs_app.Domain.Entities;

/// <summary>
/// 採番（slip_number_sequence）。伝票種別ごとに1行を永続保持する。
/// 採番は UPDATE の行ロックで直列化するため、RowVersion（楽観的排他）は持たない
/// （TrackedEntity を継承）。伝票登録と同一トランザクション内で採番すること
/// （docs/architecture.md 6章）。採番は生SQLのUPDATEで行うため、このエンティティを
/// EF Coreで追跡してはならない（参照は必ず AsNoTracking()）。
/// </summary>
public class SlipNumberSequence : TrackedEntity
{
    /// <summary>伝票種別。order_slip / sales_slip / receipt_slip / detail_receipt / billing / detail_invoice。</summary>
    public required string SequenceKey { get; set; }

    /// <summary>現在の採番値。次番は CurrentValue + 1。</summary>
    public required long CurrentValue { get; set; }
}
