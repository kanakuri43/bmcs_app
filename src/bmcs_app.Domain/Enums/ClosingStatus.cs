namespace bmcs_app.Domain.Enums;

/// <summary>
/// 月次締めの状態（monthly_closing.closing_status）。
/// 「未締め」はレコード不在で表すため、この enum に値を持たない。
/// </summary>
public enum ClosingStatus : byte
{
    Confirmed = 1,
    Released = 2,
}
