namespace bmcs_app.Domain.Entities;

/// <summary>
/// コピー機マスタ（copier_machines）。コピー機売上CSVの「機番」から得意先を特定する変換マスタ。
/// 1得意先に複数機番を紐づけられる。
/// </summary>
public class CopierMachine : AuditableEntity
{
    public required string MachineNo { get; set; }

    public required string CustomerCode { get; set; }

    public string? MachineModel { get; set; }

    public string? Remarks { get; set; }
}
