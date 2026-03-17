using System.Windows;

namespace bmcs_app.Closing;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var win = new Views.ClosingMainView();
        win.Show();
    }
}
