using System.Diagnostics;
using System.IO;
using System.Windows;
using Prism.Commands;
using Prism.Mvvm;

namespace bmcs_app.ViewModels;

public class MainWindowViewModel : BindableBase
{
    public DelegateCommand<string> LaunchCommand { get; }

    public MainWindowViewModel()
    {
        LaunchCommand = new DelegateCommand<string>(Launch);
    }

    private static void Launch(string exeName)
    {
        var dir  = AppDomain.CurrentDomain.BaseDirectory;
        var path = Path.Combine(dir, exeName);

        if (!File.Exists(path))
        {
            MessageBox.Show($"{exeName} が見つかりません。\n\n{path}",
                            "起動エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
