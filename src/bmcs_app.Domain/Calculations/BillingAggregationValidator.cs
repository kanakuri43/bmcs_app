using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Calculations;

/// <summary>
/// 親子請求（請求集約）のリンク可否判定。
/// 得意先マスタに登録・更新しようとしている得意先（<paramref name="candidate"/>）の
/// <see cref="Customer.BillingCustomerCode"/> が有効かどうかを、指し先の得意先
/// （<paramref name="billingCustomer"/>、DBから未取得または存在しない場合は <c>null</c>）
/// と突き合わせて判定する。
/// DB側（<c>FK_customers_billing_customer</c>／<c>CK_customers_billing_customer_tax_unit</c>、
/// <c>scripts/020_add_billing_customer_code.sql</c>）が最後の防波堤として同じ制約を強制するが、
/// アプリ層でも判定することでユーザーに分かりやすい理由を返す（<see cref="SalesEditLockEvaluator"/>
/// と同じ「DBアクセスを持たない純粋関数」の方針）。
/// </summary>
public static class BillingAggregationValidator
{
    /// <summary>
    /// 拒否理由を返す。<c>null</c> なら有効なリンク。
    /// </summary>
    public static string? Validate(Customer candidate, Customer? billingCustomer)
    {
        if (candidate.BillingCustomerCode == candidate.CustomerCode)
        {
            // 自分自身を指す＝単独で請求（従来どおり）。常に許可。
            return null;
        }

        if (candidate.TaxUnit == TaxUnit.Line)
        {
            return "都度得意先（内税明細単位）は請求得意先コードに自分以外の得意先を指定できません。";
        }

        if (billingCustomer is null)
        {
            return "指定した請求得意先コードの得意先が見つかりません。";
        }

        if (!billingCustomer.IsBillingRoot)
        {
            return $"請求得意先コード「{billingCustomer.CustomerCode}」は自身も他の得意先の請求得意先コードを指定しているため、請求集約先にできません（請求集約は2段階までです）。";
        }

        if (billingCustomer.ClosingDay != candidate.ClosingDay)
        {
            return "請求得意先コードの締め日が自分の締め日と一致しません。";
        }

        if (billingCustomer.TaxUnit != candidate.TaxUnit)
        {
            return "請求得意先コードの税区分が自分の税区分と一致しません。";
        }

        if (billingCustomer.RoundingType != candidate.RoundingType)
        {
            return "請求得意先コードの端数区分が自分の端数区分と一致しません。";
        }

        return null;
    }
}
