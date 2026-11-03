using System.IO;
using System.Windows;
using DlssgManager.Core;

namespace DlssgManager.Desktop;

public partial class App : Application
{
    public static string DataDirectory { get; private set; } = "";
    public static string[] Arguments { get; private set; } = [];
    public static string Version => typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    public static ManagerUpdateResult? UpdateResult { get; private set; }
    protected override async void OnStartup(StartupEventArgs e)
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
        var updateIndex = Array.IndexOf(e.Args, "--apply-manager-update");
        if (updateIndex >= 0)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var hashIndex = Array.IndexOf(e.Args, "--plan-sha256");
            if (updateIndex + 1 < e.Args.Length && hashIndex >= 0 && hashIndex + 1 < e.Args.Length)
            {
                var result = await ManagerUpdateWorker.RunAsync(e.Args[updateIndex + 1], e.Args[hashIndex + 1]);
                if (!result.Success)
                {
                    try { File.AppendAllText(Path.Combine(DataDirectory, "application.log"), $"{DateTimeOffset.UtcNow:o} Manager update: {result.Code} {result.Detail}\n"); } catch { }
                    MessageBox.Show(Localization.Get("manager_update_failed"), "DLSSG Mod Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            Shutdown();
            return;
        }
        var resultIndex = Array.IndexOf(e.Args, "--manager-update-result");
        if (resultIndex >= 0 && resultIndex + 1 < e.Args.Length)
            UpdateResult = ManagerUpdateWorker.ReadResult(e.Args[resultIndex + 1], DataDirectory);
        MainWindow = new MainWindow();
        MainWindow.Show();
    }
}
