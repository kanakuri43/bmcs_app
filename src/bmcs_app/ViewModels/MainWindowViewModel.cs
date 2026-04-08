using System.Diagnostics;
using System.IO;
using System.Windows;
using bmcs_app.Views;
using Prism.Commands;
using Prism.Mvvm;

namespace bmcs_app.ViewModels;

public class MainWindowViewModel : BindableBase
{
    public DelegateCommand<string> LaunchCommand              { get; }
    public DelegateCommand OpenPrinterSettingsCommand { get; }
    public DelegateCommand OpenServerSettingsCommand { get; }

    public MainWindowViewModel()
    {
        LaunchCommand              = new DelegateCommand<string>(Launch);
        OpenPrinterSettingsCommand = new DelegateCommand(OpenPrinterSettings);
        OpenServerSettingsCommand  = new DelegateCommand(OpenServerSettings);
    }

    private static void OpenPrinterSettings()
    {
        var vm  = new PrinterSettingsViewModel();
        var win = new PrinterSettingsWindow
        {
            DataContext = vm,
            Owner       = Application.Current.MainWindow,
        };
        win.ShowDialog();
    }

    private static void OpenServerSettings()
    {
        var vm = new ServerSettingsViewModel();
        var win = new ServerSettingsWindow
        {
            DataContext = vm,
            Owner = Application.Current.MainWindow,
        };
        win.ShowDialog();
    }

    private static void Launch(string exeNameWithArgs)
    {
        var parts   = exeNameWithArgs.Split(' ', 2);
        var exeName = parts[0];
        var args    = parts.Length > 1 ? parts[1] : "";

        var dir  = AppDomain.CurrentDomain.BaseDirectory;
        var path = Path.Combine(dir, exeName);

        if (!File.Exists(path))
        {
            MessageBox.Show($"{exeName} が見つかりません。\n\n{path}",
                            "起動エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Process.Start(new ProcessStartInfo(path)
        {
            UseShellExecute = true,
            Arguments       = args,
        });
    }
}
