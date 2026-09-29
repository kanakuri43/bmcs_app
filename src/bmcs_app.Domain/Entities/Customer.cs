using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Entities;

/// <summary>得意先マスタ（customer）。</summary>
public class Customer : AuditableEntity
{
    public required string CustomerCode { get; set; }

    public required string CustomerName { get; set; }

    public string? CustomerNameKana { get; set; }

    public string? PostalCode { get; set; }

    public string? Address1 { get; set; }

    public string? Address2 { get; set; }

    public string? PhoneNumber { get; set; }

    public string? FaxNumber { get; set; }

    /// <summary>得意先側の担当者名。自社の営業担当は <see cref="SalesEmployeeCode"/>。</summary>
    public string? ContactPersonName { get; set; }

    /// <summary>自社の営業担当社員コード。月次締めの担当者別集計キー。</summary>
    public string? SalesEmployeeCode { get; set; }

    /// <summary>請求書に印字する振込先口座1（bank_accounts）。0〜2件、空欄可。</summary>
    public string? BankAccountCode1 { get; set; }

    /// <summary>請求書に印字する振込先口座2（bank_accounts）。0〜2件、空欄可。</summary>
    public string? BankAccountCode2 { get; set; }

    /// <summary>0＝都度・明細／1〜31＝締め日／99＝末日締め。登録後は変更不可。</summary>
    public required byte ClosingDay { get; set; }

    /// <summary>登録後は変更不可。<see cref="ClosingDay"/> との組み合わせを DB の CHECK 制約が守る。</summary>
    public required TaxUnit TaxUnit { get; set; }

    public required RoundingType RoundingType { get; set; }

    public required bool PrintRepresentativeFlag { get; set; }

    /// <summary>
    /// 請求得意先コード。自分自身のコードと一致すれば単独で請求（請求集約先）、
    /// 異なれば売上がこのコードの得意先（請求集約先）に集約される（請求集約元）。
    /// 確定済み請求（billings）に取り込まれた売上が1件でもある得意先は変更不可
    /// （docs/database-schema.md 1-1節）。
    /// </summary>
    public required string BillingCustomerCode { get; set; }

    /// <summary>この得意先が請求集約先（または単独）かどうか。<c>BillingCustomerCode == CustomerCode</c> と同値。
    /// DB側の計算列 <c>is_billing_root</c> と対応するが、EFにはマップしない（アプリ側で同じ判定ができるため）。</summary>
    public bool IsBillingRoot => BillingCustomerCode == CustomerCode;
}
