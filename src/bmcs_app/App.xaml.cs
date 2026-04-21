using System.Windows;
using bmcs_app.Views;

namespace bmcs_app;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var level = ParsePermissionLevel(e.Args);
        var vm    = new ViewModels.MainWindowViewModel(level);
        var win   = new MainWindow { DataContext = vm };
        win.Show();
    }

    private static PermissionLevel ParsePermissionLevel(string[] args)
    {
        foreach (var arg in args)
        {
            // --level=1 形式
            if (arg.StartsWith("--level=", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(arg[8..], out var n) && Enum.IsDefined(typeof(PermissionLevel), n))
                    return (PermissionLevel)n;
            }
            // 単体数値 "1" "2" "3"
            else if (int.TryParse(arg, out var n) && Enum.IsDefined(typeof(PermissionLevel), n))
            {
                return (PermissionLevel)n;
            }
        }
        return PermissionLevel.Full;
    }
}
