using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using DlssgManager.Core;

namespace DlssgManager.Desktop;

public partial class MainWindow
{
    private readonly ManagerUpdateService _managerUpdates = new(Path.Combine(App.DataDirectory, "manager-updates"));
    private readonly CancellationTokenSource _windowLifetime = new();
    private readonly DispatcherTimer _managerUpdateTimer = new() { Interval = TimeSpan.FromHours(6) };
    private ManagerUpdateInfo? _availableManagerUpdate;
    private string? _dismissedManagerVersion;
    private bool _managerCheckInProgress;
    private bool _managerPreparing;
    private bool _windowClosed;

    private void InitializeManagerUpdates()
    {
        _managerUpdateTimer.Tick += async (_, _) => await CheckManagerUpdatesAsync();
        SourceInitialized += (_, _) => ApplyDarkCaption();
        RefreshManagerUpdateUi();
    }

    private async Task CheckManagerUpdatesAsync()
    {
        if (_managerCheckInProgress || _windowClosed) return;
        _managerCheckInProgress = true;
        try
        {
            await Task.Run(() => _managerUpdates.PruneCompletedUpdates(App.Version), _windowLifetime.Token);
            var update = await _managerUpdates.CheckAsync(App.Version, _windowLifetime.Token);
            if (_windowClosed) return;
            _availableManagerUpdate = update;
            RefreshManagerUpdateUi();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { SaveError(error); }
        finally { _managerCheckInProgress = false; }
    }

    private void RefreshManagerUpdateUi()
    {
        if (ManagerVersionText is null) return;
        ManagerVersionText.Text = string.Format(Localization.Get("manager_version"), App.Version);
        var preview = _readOnly && App.Arguments.Contains("--preview-update");
        var visible = preview || _availableManagerUpdate is not null && _availableManagerUpdate.Version != _dismissedManagerVersion;
        ManagerUpdateNotice.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ManagerUpdateInstallButton.IsEnabled = !preview && !_readOnly && _operation is null && _availableManagerUpdate is not null;
        ManagerUpdateInstallButton.ToolTip = Localization.Get("manager_update_install_note");
        ManagerUpdateDismissButton.IsEnabled = !_managerPreparing;
        ManagerUpdateProgress.Visibility = _managerPreparing ? Visibility.Visible : Visibility.Collapsed;
        if (!_managerPreparing)
            ManagerUpdateText.Text = string.Format(Localization.Get("manager_update_available"), _availableManagerUpdate?.Version ?? "0.3.0");
    }

    private async void ManagerUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_readOnly || _availableManagerUpdate is null) return;
        if (_operation is not null) { SetStatus("manager_update_busy"); return; }
        ManagerUpdatePlan? plan = null;
        _managerPreparing = true;
        ManagerUpdateProgress.IsIndeterminate = true;
        try
        {
            await RunAsync(async token =>
            {
                var arguments = new List<string> { "--data-dir", App.DataDirectory };
                if (!string.IsNullOrWhiteSpace(FolderPath.Text))
                {
                    arguments.Add("--game");
                    arguments.Add(FolderPath.Text.Trim().Trim('"'));
                }
                var progress = new Progress<OperationProgress>(p =>
                {
                    if (_windowClosed) return;
                    ManagerUpdateText.Text = Localization.Get(p.Stage);
                    ManagerUpdateProgress.IsIndeterminate = p.Percent is null;
                    if (p.Percent is not null) ManagerUpdateProgress.Value = Math.Clamp(p.Percent.Value, 0, 100);
                    SetStatus(p.Stage);
                });
                var prepared = await _managerUpdates.PrepareAsync(_availableManagerUpdate, Environment.ProcessPath
                    ?? throw new InvalidOperationException("The manager executable cannot be located."),
                    App.DataDirectory, Environment.ProcessId, arguments, progress, token);
                token.ThrowIfCancellationRequested();
                plan = prepared;
            }, "manager_update_download_failed");
        }
        finally
        {
            _managerPreparing = false;
            RefreshManagerUpdateUi();
        }
        if (plan is null || _windowClosed) return;
        try
        {
            var start = new ProcessStartInfo(plan.WorkerExePath)
            {
                UseShellExecute = false, CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(plan.WorkerExePath)!
            };
            foreach (var arg in new[] { "--apply-manager-update", plan.PlanPath, "--plan-sha256", plan.PlanSha256, "--data-dir", App.DataDirectory })
                start.ArgumentList.Add(arg);
            using var worker = Process.Start(start) ?? throw new InvalidOperationException("The update worker could not be started.");
            Close();
        }
        catch (Exception error)
        {
            SaveError(error);
            SetStatus("manager_update_failed");
        }
    }

    private void ManagerUpdateDismiss_Click(object sender, RoutedEventArgs e)
    {
        if (_managerPreparing) return;
        _dismissedManagerVersion = _availableManagerUpdate?.Version;
        ManagerUpdateNotice.Visibility = Visibility.Collapsed;
    }

    private void ShowManagerUpdateResult()
    {
        if (App.UpdateResult is not { } result) return;
        if (!string.IsNullOrEmpty(result.Detail))
            SaveError(new InvalidOperationException($"Manager update {result.Code}: {result.Detail}"));
        SetStatus(result.RebootRequired ? "manager_update_restart_required"
            : result.Success ? "manager_update_complete" : "manager_update_failed", result.Success && !result.RebootRequired ? result.ToVersion : "");
    }

    private void StopManagerUpdates()
    {
        _windowClosed = true;
        _managerUpdateTimer.Stop();
        _windowLifetime.Cancel();
        _managerUpdates.Dispose();
        _windowLifetime.Dispose();
    }

    private void ApplyDarkCaption()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            var enabled = 1;
            _ = DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
            var color = 0x0018100B;
            _ = DwmSetWindowAttribute(handle, 35, ref color, sizeof(int));
            color = 0x00F9F5F1;
            _ = DwmSetWindowAttribute(handle, 36, ref color, sizeof(int));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
