namespace bmcs_app.Application.Common;

/// <summary>
/// 起動時パラメータ（ショートカット引数）から現在操作している社員コードを解決する。
/// 1つ目の引数を社員コードとして扱う。引数が渡されない場合（IDEからの起動等）は
/// 開発用に seed データの `EMP001` を既定値として使う。
/// 社員コードの実在確認・権限レベルの取得は、実際に使う側（例: メインメニュー画面）が
/// <c>EmployeeService</c> を通じて行う。ここでは文字列を保持するだけで DB アクセスは行わない
/// （<see cref="ICurrentEmployeeContext"/> は監査列の書き込みにも使われる軽量な参照のため）。
/// </summary>
public class StartupArgsCurrentEmployeeContext : ICurrentEmployeeContext
{
    private const string DefaultEmployeeCode = "0";

    public StartupArgsCurrentEmployeeContext(string[] startupArgs)
    {
        EmployeeCode = startupArgs.Length > 0 && !string.IsNullOrWhiteSpace(startupArgs[0])
            ? startupArgs[0]
            : DefaultEmployeeCode;
    }

    public string EmployeeCode { get; }
}
