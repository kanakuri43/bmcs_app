namespace bmcs_app.Domain.Entities;

/// <summary>
/// 自社情報マスタ（company_info）。1レコード運用（<see cref="CompanyInfoId"/> は常に 1）。
/// 振込口座は持たない（bank_account を参照）。
/// </summary>
public class CompanyInfo : AuditableEntity
{
    public required byte CompanyInfoId { get; set; }

    public required string CompanyName { get; set; }

    /// <summary>適格請求書発行事業者の登録番号（T＋13桁）。法定記載事項。</summary>
    public required string InvoiceRegistrationNumber { get; set; }

    public string? PostalCode { get; set; }

    public string? Address1 { get; set; }

    public string? Address2 { get; set; }

    public string? PhoneNumber { get; set; }

    public string? FaxNumber { get; set; }

    public string? RepresentativeName { get; set; }
}
