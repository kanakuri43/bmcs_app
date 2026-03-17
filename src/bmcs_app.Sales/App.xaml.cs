using System.Windows;

namespace bmcs_app.Sales;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var win = new Views.SalesMainView();
        win.Show();
    }
}
