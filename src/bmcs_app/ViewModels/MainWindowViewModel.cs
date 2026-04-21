using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using bmcs_app.Infrastructure;
using bmcs_app.Views;
using Prism.Commands;
using Prism.Mvvm;

namespace bmcs_app.ViewModels;

public class MainWindowViewModel : BindableBase
{
    public DelegateCommand<string> LaunchCommand                  { get; }
    public DelegateCommand         OpenPrinterSettingsCommand     { get; }
    public DelegateCommand         OpenCompanyInfoSettingsCommand { get; }

    public bool   IsSettingsEnabled { get; }
    public string ServerAddress     { get; }

    private readonly HashSet<string>? _allowedExes;

    public MainWindowViewModel(PermissionLevel level = PermissionLevel.Full)
    {
        _allowedExes       = PermissionPolicy.GetAllowedExes(level);
        IsSettingsEnabled  = PermissionPolicy.IsSettingsEnabled(level);
        ServerAddress      = $"{ResolveServerAddress()}";

        LaunchCommand                  = new DelegateCommand<string>(Launch, CanLaunch);
        OpenPrinterSettingsCommand     = new DelegateCommand(OpenPrinterSettings,     () => IsSettingsEnabled);
        OpenCompanyInfoSettingsCommand = new DelegateCommand(OpenCompanyInfoSettings, () => IsSettingsEnabled);
    }

    private static string ResolveServerAddress()
    {
        try
        {
            var cs     = AppConfig.ConnectionString;
            string? server = null, database = null;
            foreach (var part in cs.Split(';'))
            {
                var kv = part.Split('=', 2);
                if (kv.Length != 2) continue;
                var key = kv[0].Trim();
                if (key.Equals("Server",           StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("Data Source",       StringComparison.OrdinalIgnoreCase))
                    server = kv[1].Trim();
                else if (key.Equals("Database",    StringComparison.OrdinalIgnoreCase) ||
                         key.Equals("Initial Catalog", StringComparison.OrdinalIgnoreCase))
                    database = kv[1].Trim();
            }
            return (server, database) switch
            {
                (not null, not null) => $"{database}@{server}",
                (not null, null)     => server,
                _                    => "未設定",
            };
        }
        catch
        {
            return "未設定";
        }
    }

    private bool CanLaunch(string? exeNameWithArgs)
    {
        if (_allowedExes is null || exeNameWithArgs is null) return true;
        var exeName = exeNameWithArgs.Split(' ', 2)[0];
        return _allowedExes.Contains(exeName);
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

    private static void OpenCompanyInfoSettings()
    {
        var vm  = new CompanyInfoSettingsViewModel();
        var win = new CompanyInfoSettingsWindow
        {
            DataContext = vm,
            Owner       = Application.Current.MainWindow,
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
