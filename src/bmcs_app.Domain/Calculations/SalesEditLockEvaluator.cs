using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Calculations;

/// <summary>
/// 売上伝票の編集可否（C-6・2026-09-10確定、2026-09-14 Phase 6-5レビューで4条件に改訂）の判定。
/// ①請求締め（<see cref="Sales.BillingNumber"/> が確定済み <c>billing</c> を指す）
/// ①'明細請求書発行済（都度得意先の売上明細行が<c>detail_invoice_sales_line</c>に連携している）
/// ②月次締め（対象年月の <c>monthly_closing</c> が確定済み）
/// ③入金済み（<see cref="Sales.SettlementStatus"/> が消込完了）
/// のいずれかに該当する行が1件でもあれば伝票全体を編集不可とする（`docs/product-spec.md` 共通業務ルール5）。
/// ①'は当初「ロック条件に含めない」としていたが、発行済みの明細請求書ヘッダー（確定金額のスナップショット）
/// が実データと乖離する不具合をPhase 6-5レビューで発見し追加した（`docs/design_document.md` 13章）。
/// DBアクセス（<c>monthly_closing</c>・<c>detail_invoice_sales_line</c>の照会）は呼び出し元
/// （<c>SalesEditLockService</c>）の責務とし、本クラスは純粋関数として単体テスト可能にする。
/// </summary>
public static class SalesEditLockEvaluator
{
    /// <summary>
    /// <paramref name="lines"/> は同一伝票の明細行（1件以上）。<paramref name="monthlyClosingConfirmed"/> は
    /// 当該得意先・当該年月の <c>monthly_closing</c> が確定済みかどうか、
    /// <paramref name="detailInvoiceLinked"/> はいずれかの明細行が<c>detail_invoice_sales_line</c>に
    /// 連携済みかどうか（いずれも呼び出し元が事前に照会する）。
    /// </summary>
    public static SalesEditLock Evaluate(
        IReadOnlyList<Sales> lines, bool monthlyClosingConfirmed, bool detailInvoiceLinked)
    {
        if (lines.Any(l => l.BillingNumber is not null))
        {
            return new SalesEditLock(true, "請求締め済みのため編集できません。");
        }

        if (detailInvoiceLinked)
        {
            return new SalesEditLock(true, "明細請求書発行済みのため編集できません。明細請求書を取消してから訂正してください。");
        }

        if (monthlyClosingConfirmed)
        {
            return new SalesEditLock(true, "月次締め済みのため編集できません。");
        }

        if (lines.Any(l => l.SettlementStatus == SettlementStatus.FullySettled))
        {
            return new SalesEditLock(true, "入金済み（消込完了）のため編集できません。");
        }

        return SalesEditLock.Unlocked;
    }
}

/// <summary>編集可否の判定結果。<see cref="Reason"/> はロック中のみ非null（ViewModel はこれを表示するだけ）。</summary>
public readonly record struct SalesEditLock(bool IsLocked, string? Reason)
{
    public static SalesEditLock Unlocked { get; } = new(false, null);
}
