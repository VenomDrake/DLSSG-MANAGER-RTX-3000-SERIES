using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DlssgManager.Core;
using Microsoft.Win32;

namespace DlssgManager.Desktop;

public partial class MainWindow : Window
{
    private readonly ReleaseService _releases = new(Path.Combine(App.DataDirectory, "cache"));
    private readonly InstallService _installer = new();
    private readonly GameScanner _scanner = new();
    private readonly HardwareService _hardwareService = new();
    private readonly SteamLibraryService _steamService = new();
    private readonly DispatcherTimer _logTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private ReleasePackage? _package;
    private UpdateState? _updateState;
    private HardwareInfo? _hardware;
    private GameScan? _scan;
    private InstallationRecord? _record;
    private LogVerification? _verification;
    private IReadOnlyList<SteamGame> _steamGames = [];
    private CancellationTokenSource? _operation;
    private bool _changingChoices;
    private string _statusKey = "idle";
    private string _statusDetail = "";
    private string? _selectedExe;
    private bool _readOnly => App.Arguments.Contains("--read-only");

    private sealed record Choice(string Value, string Label) { public override string ToString() => Label; }
    private sealed record CheckRow(string Title, string Detail, string Status, string Symbol, Brush Color);

    public MainWindow()
    {
        InitializeComponent();
        if (_readOnly) Title += " · Preview";
        _changingChoices = true;
        LanguagePicker.ItemsSource = Localization.Languages;
        LanguagePicker.SelectedItem = Localization.Languages.First(l => l.Code == Localization.Current);
        _changingChoices = false;
        RebuildChoices();
        FlowDirection = Localization.Direction;
        FolderPath.FlowDirection = FlowDirection.LeftToRight;
        Localization.Changed += LanguageChanged;
        _logTimer.Tick += (_, _) => ReadLog(false);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await RunAsync(async token =>
        {
            await UpdatePackageAsync(token);
            await InspectHardwareAsync(token);
            await DiscoverSteamAsync(token);
            var index = Array.IndexOf(App.Arguments, "--game");
            if (index >= 0 && index + 1 < App.Arguments.Length)
            {
                FolderPath.Text = App.Arguments[index + 1];
                await AnalyzeAsync(token);
            }
            else SetStatus("idle");
        });
    }

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (_operation is not null) return;
        _operation = new CancellationTokenSource();
        OperationBar.Visibility = Visibility.Visible;
        OperationBar.IsIndeterminate = true;
        CancelButton.Visibility = Visibility.Visible;
        RefreshButtons();
        try { await action(_operation.Token); }
        catch (OperationCanceledException) { SetStatus("canceled"); }
        catch (Exception error)
        {
            SetStatus("operation_failed", error.Message);
            SaveError(error);
            MessageBox.Show(this, Localization.Get("operation_failed") + "\n\n" + error.Message,
                Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _operation.Dispose();
            _operation = null;
            OperationBar.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Collapsed;
            RefreshButtons();
        }
    }

    private IProgress<OperationProgress> Progress() => new Progress<OperationProgress>(p =>
    {
        SetStatus(p.Stage, p.Detail);
        OperationBar.IsIndeterminate = p.Percent is null;
        if (p.Percent is not null) OperationBar.Value = Math.Clamp(p.Percent.Value, 0, 100);
    });

    private async Task UpdatePackageAsync(CancellationToken token)
    {
        var result = await _releases.EnsureLatestAsync(Progress(), token);
        _package = result.Package;
        _updateState = result.State;
        RefreshSummaries();
        SetStatus(result.State switch
        {
            UpdateState.UpToDate => "up_to_date", UpdateState.Downloaded => "download_complete",
            UpdateState.OfflineCache => "offline_cache", _ => "update_failed"
        }, result.Detail);
        if (_scan is not null) await AnalyzeAsync(token);
        RenderChecks();
    }

    private async Task InspectHardwareAsync(CancellationToken token)
    {
        SetStatus("hardware_checking");
        _hardware = await _hardwareService.InspectAsync(token);
        RefreshSummaries();
        RenderChecks();
        SetStatus("idle");
    }

    private async Task DiscoverSteamAsync(CancellationToken token)
    {
        _steamGames = await _steamService.DiscoverAsync(token);
        _changingChoices = true;
        SteamPicker.ItemsSource = _steamGames;
        _changingChoices = false;
        SteamPicker.ToolTip = Localization.Get(_steamGames.Count == 0 ? "no_steam_games" : "steam_library");
    }

    private async Task AnalyzeAsync(CancellationToken token)
    {
        SetStatus("analyzing");
        _logTimer.Stop();
        _verification = null;
        _scan = await _scanner.ScanAsync(FolderPath.Text.Trim().Trim('"'), _package, _selectedExe, token);
        _record = null;
        try { _record = _scan.RenderingDirectory is null ? null : await _installer.ReadRecordAsync(_scan.RenderingDirectory, token); }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException)
        { _record = null; SaveError(error); }
        _changingChoices = true;
        ExePicker.ItemsSource = _scan.Executables;
        ExePicker.SelectedItem = _scan.Executables.FirstOrDefault(x => string.Equals(x.Path, _scan.RenderingExe, StringComparison.OrdinalIgnoreCase));
        _changingChoices = false;
        if (_record is not null) ApplyOptions(_record.Options);
        RefreshSummaries();
        RenderChecks();
        ReadLog(false);
        SetStatus(PrechecksBlocked() ? "checks_blocked" : _verification?.Evaluated == true ? "verified" : "prechecks_only");
    }

    private void RefreshSummaries()
    {
        PackageVersion.Text = _package is null ? "—" : "v" + _package.Release.Version;
        var updateKey = _updateState switch
        {
            UpdateState.UpToDate => "up_to_date", UpdateState.Downloaded => "download_complete",
            UpdateState.OfflineCache => "offline_cache", UpdateState.Failed => "update_failed", _ => "checking_update"
        };
        PackageStatus.Text = Localization.Get(updateKey);
        if (_package is { SupportedLayout: false }) PackageStatus.Text += "\n" + Localization.Get("unsupported_package");
        GpuName.Text = _hardware?.Gpus.FirstOrDefault(g => g.Eligible)?.Name ?? _hardware?.Gpus.FirstOrDefault()?.Name ?? "—";
        HardwareStatus.Text = _hardware is null ? Localization.Get("hardware_pending")
            : string.Join(" · ", _hardware.Gpus.Select(g => $"{g.Name} / {g.DriverVersion}"));
        if (_scan is not null)
        {
            var state = _scan.ModState switch { ModState.Managed => "managed_install", ModState.RecognizedManual => "manual_install", _ => "not_installed" };
            GameSummary.Text = Localization.Get(state) + (_scan.InstalledVersion is null ? "" : " · v" + _scan.InstalledVersion)
                + (_scan.RenderingExe is null ? "" : "\n" + _scan.RenderingExe);
            if (IsGameCurrent())
                GameSummary.Text += "\n" + Localization.Get("up_to_date");
        }
        RefreshButtons();
    }

    private void RenderChecks()
    {
        var checks = (_package?.Checks ?? []).Concat(_hardware?.Checks ?? []).Concat(_scan?.Checks ?? []).ToList();
        if (_verification?.HagsActive == true)
            checks = checks.Select(c => c.Code == "hags_runtime" ? c with { Level = CheckLevel.Pass } : c).ToList();
        CheckList.ItemsSource = checks.Select(c => new CheckRow(Localization.Get(c.Code), c.Detail,
            Localization.Get(c.Level.ToString().ToLowerInvariant()), c.Level switch
            { CheckLevel.Pass => "✓", CheckLevel.Blocked => "×", CheckLevel.Warning => "!", _ => "·" },
            new SolidColorBrush((Color)ColorConverter.ConvertFromString(c.Level switch
            { CheckLevel.Pass => "#70E5BB", CheckLevel.Blocked => "#FF8795", CheckLevel.Warning => "#FFC479", _ => "#93B7DD" })))).ToList();
    }

    private void RefreshButtons()
    {
        if (UpdateButton is null) return;
        var idle = _operation is null;
        foreach (var button in new[] { UpdateButton, HardwareButton, FolderButton, ExeButton, AnalyzeButton, SteamButton }) button.IsEnabled = idle;
        FolderPath.IsEnabled = idle;
        SteamPicker.IsEnabled = idle;
        ExePicker.IsEnabled = idle;
        OptionsGrid.IsEnabled = idle;
        var blockedHardware = _hardware is null || _hardware.Checks.Any(c => c.Level == CheckLevel.Blocked);
        InstallButton.IsEnabled = idle && !_readOnly && !blockedHardware && _package is { SupportedLayout: true } && _scan?.CanInstall == true;
        InstallButton.Content = Localization.Get(_scan?.ModState switch
        {
            ModState.Managed or ModState.RecognizedManual => IsGameCurrent() ? "apply_options" : "update_game",
            _ => "install"
        });
        RestoreButton.IsEnabled = idle && !_readOnly && _record is not null;
        DisableButton.IsEnabled = idle && !_readOnly && _record?.State == "Installed";
        LaunchButton.IsEnabled = idle && _scan?.RenderingExe is not null;
        ReadLogButton.IsEnabled = idle && _scan?.RenderingDirectory is not null;
    }

    private bool IsGameCurrent()
    {
        if (_package is null || _scan is null || _updateState is not (UpdateState.UpToDate or UpdateState.Downloaded)) return false;
        if (_scan.ModState == ModState.RecognizedManual) return _scan.InstalledVersion == _package.Release.Version;
        return _record is not null && _record.ReleaseId == _package.Release.ReleaseId && _record.AssetId == _package.Release.AssetId
            && string.Equals(_record.PackageSha256, _package.Release.Sha256, StringComparison.OrdinalIgnoreCase);
    }
    private bool PrechecksBlocked() => _scan?.CanInstall != true || _package is not { SupportedLayout: true }
        || _hardware is null || _hardware.Checks.Any(c => c.Level == CheckLevel.Blocked);

    private static string Value(ComboBox combo, string fallback) => (combo.SelectedItem as Choice)?.Value ?? fallback;
    private void Fill(ComboBox combo, IEnumerable<Choice> choices, string fallback)
    {
        var current = Value(combo, fallback);
        var items = choices.ToList();
        combo.ItemsSource = items;
        combo.SelectedItem = items.FirstOrDefault(c => c.Value == current) ?? items.FirstOrDefault(c => c.Value == fallback) ?? items.FirstOrDefault();
    }
    private void RebuildChoices()
    {
        _changingChoices = true;
        Fill(EnginePicker, [new("DLSS", "NVIDIA DLSS-G"), new("XeSS", "Intel XeSS-FG")], "DLSS");
        Fill(OptimizationPicker, Enumerable.Range(0, 4).Select(i => new Choice(i.ToString(), Localization.Get("tier" + i))), "1");
        Fill(PresetPicker, new[] { "A", "B", "C" }.Select(v => new Choice(v, v)), "A");
        Fill(SrPicker, new[] { "Off", "M-INT8", "L-INT8", "M-FP16", "L-FP16" }.Select(v => new Choice(v, v == "Off" ? Localization.Get("sr_off") : v)), "Off");
        Fill(FlowPicker, new[] { "Coarse", "Middle", "Fine", "CoarseAt4K" }.Select(v => new Choice(v, v)), "Middle");
        Fill(HudPicker, [new("Auto", Localization.Get("hud_auto")), new("1", Localization.Get("hud_strict")), new("0", Localization.Get("hud_never"))], "Auto");
        Fill(ProxyPicker, new[] { "version.dll", "winmm.dll", "dbghelp.dll", "dinput8.dll", "dxgi.dll", "d3d12.dll" }.Select(v => new Choice(v, v)), "version.dll");
        Fill(LogPicker, new[] { "log_off", "log_level_errors", "log_normal", "log_trace" }.Select((v, i) => new Choice(i.ToString(), Localization.Get(v))), "2");
        RefreshMultipliers();
        _changingChoices = false;
    }
    private void RefreshMultipliers() => Fill(MultiplierPicker,
        Enumerable.Range(2, Value(EnginePicker, "DLSS") == "XeSS" ? 3 : 5).Select(i => new Choice(i.ToString(), i + "×")), "2");
    private ModOptions GetOptions() => new()
    {
        Engine = Value(EnginePicker, "DLSS"), Multiplier = int.Parse(Value(MultiplierPicker, "2")),
        Optimization = int.Parse(Value(OptimizationPicker, "1")), Preset = Value(PresetPicker, "A"),
        SuperResolution = Value(SrPicker, "Off"), FlowResolution = Value(FlowPicker, "Middle"),
        UseHudless = Value(HudPicker, "Auto"), LoggingLevel = int.Parse(Value(LogPicker, "2")),
        FrameTiming = TimingCheck.IsChecked == true, Proxy = Value(ProxyPicker, "version.dll")
    };
    private static void Select(ComboBox combo, string value) => combo.SelectedItem = combo.Items.Cast<Choice>().FirstOrDefault(c => c.Value == value) ?? combo.SelectedItem;
    private void ApplyOptions(ModOptions options)
    {
        _changingChoices = true;
        Select(EnginePicker, options.Engine); RefreshMultipliers(); Select(MultiplierPicker, options.Multiplier.ToString());
        Select(OptimizationPicker, options.Optimization.ToString()); Select(PresetPicker, options.Preset);
        Select(SrPicker, options.SuperResolution); Select(FlowPicker, options.FlowResolution); Select(HudPicker, options.UseHudless);
        Select(LogPicker, options.LoggingLevel.ToString()); Select(ProxyPicker, options.Proxy); TimingCheck.IsChecked = options.FrameTiming;
        _changingChoices = false;
    }
    private void LanguageChanged()
    {
        FlowDirection = Localization.Direction;
        FolderPath.FlowDirection = FlowDirection.LeftToRight;
        RebuildChoices(); RefreshSummaries(); RenderChecks(); SetStatus(_statusKey, _statusDetail);
        if (_scan is not null) ReadLog(false);
    }
    private void SetStatus(string key, string detail = "")
    {
        _statusKey = key; _statusDetail = detail;
        StatusText.Text = Localization.Get(key) + (string.IsNullOrWhiteSpace(detail) ? "" : " · " + detail);
    }
    private void SaveError(Exception error)
    {
        try { File.AppendAllText(Path.Combine(App.DataDirectory, "application.log"), $"{DateTimeOffset.UtcNow:o} {error}\n"); } catch { }
    }

    private async void Update_Click(object sender, RoutedEventArgs e) => await RunAsync(UpdatePackageAsync);
    private async void Hardware_Click(object sender, RoutedEventArgs e) => await RunAsync(InspectHardwareAsync);
    private async void Analyze_Click(object sender, RoutedEventArgs e) { _selectedExe = null; await RunAsync(AnalyzeAsync); }
    private async void SteamRefresh_Click(object sender, RoutedEventArgs e) => await RunAsync(DiscoverSteamAsync);
    private async void Folder_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { _selectedExe = null; await RunAsync(AnalyzeAsync); } }
    private void Language_Changed(object sender, SelectionChangedEventArgs e) { if (!_changingChoices && LanguagePicker.SelectedItem is LanguageChoice l) Localization.Change(l.Code); }
    private void Engine_Changed(object sender, SelectionChangedEventArgs e) { if (!_changingChoices) RefreshMultipliers(); }
    private async void Exe_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_changingChoices || _operation is not null || ExePicker.SelectedItem is not ExeCandidate exe) return;
        _selectedExe = exe.Path; await RunAsync(AnalyzeAsync);
    }
    private async void Steam_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_changingChoices || _operation is not null || SteamPicker.SelectedItem is not SteamGame game) return;
        FolderPath.Text = game.Directory; _selectedExe = null; await RunAsync(AnalyzeAsync);
    }
    private async void Folder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = Localization.Get("choose_folder"), Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        FolderPath.Text = dialog.FolderName; _selectedExe = null; await RunAsync(AnalyzeAsync);
    }
    private async void Exe_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = Localization.Get("choose_exe"), Filter = "Windows executable (*.exe)|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        FolderPath.Text = dialog.FileName; _selectedExe = dialog.FileName; await RunAsync(AnalyzeAsync);
    }
    private async void Install_Click(object sender, RoutedEventArgs e) => await RunAsync(async token =>
    {
        if (_readOnly || _package is null || _scan is null) return;
        var options = GetOptions();
        await InspectHardwareAsync(token);
        await AnalyzeAsync(token);
        if (_hardware!.Checks.Any(c => c.Level == CheckLevel.Blocked) || !_scan!.CanInstall) { SetStatus("checks_blocked"); return; }
        if (MessageBox.Show(this, Localization.Get("confirm_install") + $"\n\n{_scan.RenderingDirectory}\nv{_package.Release.Version} · {options.Engine} · {options.Multiplier}× · {options.Proxy}",
            Title, MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        await _installer.InstallAsync(_scan, _package, options, Progress(), token);
        await AnalyzeAsync(token); SetStatus("install_done");
    });
    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (_scan?.RenderingDirectory is not { } directory || _readOnly) return;
        if (MessageBox.Show(this, Localization.Get("confirm_restore") + "\n\n" + directory, Title, MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        await RunAsync(async token => { await _installer.RestoreAsync(directory, Progress(), token); await AnalyzeAsync(token); SetStatus("restore_done"); });
    }
    private async void Disable_Click(object sender, RoutedEventArgs e)
    {
        if (_scan?.RenderingDirectory is not { } directory || _readOnly) return;
        if (MessageBox.Show(this, Localization.Get("confirm_disable"), Title, MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        await RunAsync(async token => { await _installer.DisableAsync(directory, token); await AnalyzeAsync(token); SetStatus("disable_done"); });
    }
    private void Launch_Click(object sender, RoutedEventArgs e)
    {
        if (_scan?.RenderingExe is null) return;
        try
        {
            var steam = _steamGames.FirstOrDefault(g => _scan.RenderingExe.StartsWith(Path.TrimEndingDirectorySeparator(g.Directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            Process.Start(steam is null ? new ProcessStartInfo(_scan.RenderingExe) { UseShellExecute = true, WorkingDirectory = _scan.RenderingDirectory }
                : new ProcessStartInfo("steam://rungameid/" + steam.AppId) { UseShellExecute = true });
            _logTimer.Start(); SetStatus("launch_note");
        }
        catch (Exception error) { SetStatus("operation_failed", error.Message); SaveError(error); }
    }
    private void ReadLog_Click(object sender, RoutedEventArgs e) { ReadLog(true); _logTimer.Start(); }
    private void ReadLog(bool showTab)
    {
        if (_scan?.RenderingDirectory is null) return;
        var path = Path.Combine(_scan.RenderingDirectory, "dlssg_sm86", "logs", "dlssg_sm86.log");
        _verification = LogReader.Read(path, Path.GetFileName(_scan.RenderingExe));
        VerificationStatus.Text = Localization.Get(_verification.Summary);
        VerificationStatus.Foreground = _verification.Evaluated ? (Brush)FindResource("Accent") : (Brush)FindResource("Text");
        VerificationDetails.Text = string.Join(" · ", new[] { _verification.Gpu, _verification.ProcessId is null ? null : "PID " + _verification.ProcessId }.Where(s => s is not null));
        VerificationErrors.Text = _verification.Errors.Count == 0 ? Localization.Get("no_errors") : string.Join("\n", _verification.Errors.TakeLast(8));
        RenderChecks();
        if (showTab) MainTabs.SelectedIndex = 1;
    }
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { FileName = "DLSSG-diagnostic-report.json", Filter = "JSON (*.json)|*.json", DefaultExt = ".json" };
        if (dialog.ShowDialog(this) != true) return;
        string Redact(string text)
        {
            if (!string.IsNullOrWhiteSpace(_scan?.Root)) text = text.Replace(_scan.Root, "<GAME>", StringComparison.OrdinalIgnoreCase);
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(profile)) text = text.Replace(profile, "<USER>", StringComparison.OrdinalIgnoreCase);
            return text;
        }
        try
        {
            var report = new
            {
                ManagerVersion = "0.1.0", GeneratedAt = DateTimeOffset.UtcNow,
                Package = _package?.Release.Version, PackageSha256 = _package?.Release.Sha256,
                UpdateState = _updateState?.ToString(), Gpus = _hardware?.Gpus,
                Checks = (_hardware?.Checks ?? []).Concat(_scan?.Checks ?? []).Select(c => new { c.Code, Level = c.Level.ToString(), Detail = Redact(c.Detail) }),
                Game = _scan is null ? null : Path.GetFileName(_scan.Root), RuntimeVersion = _scan?.RuntimeVersion,
                Verification = _verification is null ? null : new { _verification.ModLoaded, _verification.FrameGenerationCreated, _verification.Evaluated, _verification.HagsActive, Errors = _verification.Errors.Select(Redact) }
            };
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            SetStatus("report_saved");
        }
        catch (Exception error) { SetStatus("operation_failed", error.Message); SaveError(error); }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => _operation?.Cancel();
    private static void Open(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    private void Upstream_Click(object sender, RoutedEventArgs e) => Open(ReleaseService.RepositoryUrl);
    private void Driver_Click(object sender, RoutedEventArgs e) => Open("https://www.nvidia.com/Download/index.aspx");
    private void Graphics_Click(object sender, RoutedEventArgs e) { Open("ms-settings:display-advancedgraphics"); SetStatus("hags_help"); }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_operation is not null) { e.Cancel = true; _operation.Cancel(); SetStatus("close_busy"); return; }
        _logTimer.Stop(); Localization.Changed -= LanguageChanged; _releases.Dispose();
    }
}
