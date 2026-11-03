using System.IO;
using System.Windows;

namespace DlssgManager.Desktop;

public partial class App : Application
{
    public static string DataDirectory { get; private set; } = "";
    public static string[] Arguments { get; private set; } = [];
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Arguments = e.Args;
        var index = Array.IndexOf(e.Args, "--data-dir");
        DataDirectory = index >= 0 && index + 1 < e.Args.Length ? Path.GetFullPath(e.Args[index + 1])
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DLSSGModManager");
        Directory.CreateDirectory(DataDirectory);
        Localization.Initialize(DataDirectory);
        DispatcherUnhandledException += (_, args) =>
        {
            try { File.AppendAllText(Path.Combine(DataDirectory, "application.log"), $"{DateTimeOffset.UtcNow:o} {args.Exception}\n"); } catch { }
            MessageBox.Show(Localization.Get("unexpected_error") + "\n\n" + args.Exception.Message,
                "DLSSG Mod Manager", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        MainWindow = new MainWindow();
        MainWindow.Show();
    }
}
