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

    /// <summary>0＝都度・明細／1〜31＝締め日／99＝末日締め。登録後は変更不可。</summary>
    public required byte ClosingDay { get; set; }

    /// <summary>登録後は変更不可。<see cref="ClosingDay"/> との組み合わせを DB の CHECK 制約が守る。</summary>
    public required TaxUnit TaxUnit { get; set; }

    public required RoundingType RoundingType { get; set; }

    public required bool PrintRepresentativeFlag { get; set; }
}
