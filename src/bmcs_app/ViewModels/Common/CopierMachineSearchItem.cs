using bmcs_app.Domain.Entities;

namespace bmcs_app.ViewModels.Common;

/// <summary>コピー機マスタ検索モーダルの一覧行の表示用。</summary>
public sealed record CopierMachineSearchItem(string MachineNo, string CustomerCode, string Remarks)
{
    public static CopierMachineSearchItem FromEntity(CopierMachine machine) => new(
        machine.MachineNo,
        machine.CustomerCode,
        machine.Remarks ?? string.Empty);
}
