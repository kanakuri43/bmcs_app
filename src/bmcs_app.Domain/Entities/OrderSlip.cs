using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Entities;

/// <summary>
/// 受注（order_slip）。明細行1テーブル構成（M-5）。主キーは (OrderSlipNumber, LineNumber)。
/// </summary>
public class OrderSlip : AuditableEntity
{
    public required string OrderSlipNumber { get; set; }

    public required short LineNumber { get; set; }

    public required DateOnly OrderDate { get; set; }

    public required string CustomerCode { get; set; }

    public required string CustomerName { get; set; }

    /// <summary>子得意先（学校のクラス・先生等）の指定。子得意先マスタは持たない（C-9）。</summary>
    public string? SubCustomerId { get; set; }

    public required string ProductCode { get; set; }

    public required string ProductName { get; set; }

    public string? Specification { get; set; }

    public string? UnitName { get; set; }

    public required decimal OrderQuantity { get; set; }

    public required decimal UnitPrice { get; set; }

    public required decimal Amount { get; set; }

    public required decimal CostPrice { get; set; }

    public required TaxCategory TaxCategory { get; set; }

    public required decimal TaxRate { get; set; }

    /// <summary>引当数量。在庫連携はスコープ外のため自動更新ロジックは持たない。</summary>
    public required decimal AllocatedQuantity { get; set; }

    public required OrderStatus OrderStatus { get; set; }

    /// <summary>売上化済数量。<see cref="OrderQuantity"/> との比較で <see cref="OrderStatus"/> を判定する。</summary>
    public required decimal SalesConfirmedQuantity { get; set; }

    /// <summary>伝票摘要。同一伝票の全行に同じ値が入る（伝票単位の値）。</summary>
    public string? SlipRemarks { get; set; }

    /// <summary>行摘要。</summary>
    public string? LineRemarks { get; set; }

    /// <summary>社内摘要。画面表示のみで帳票には印字しない。同一伝票の全行に同じ値が入る（伝票単位の値）。</summary>
    public string? InternalRemarks { get; set; }

    /// <summary>担当者（社員コード）。得意先の営業担当とは別に、その伝票自体の担当者。任意。同一伝票の全行に同じ値が入る（伝票単位の値）。</summary>
    public string? EmployeeCode { get; set; }
}
