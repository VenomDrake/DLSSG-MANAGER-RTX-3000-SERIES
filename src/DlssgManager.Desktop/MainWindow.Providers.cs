using System.IO;
using System.Windows;
using System.Windows.Controls;
using DlssgManager.Core;

namespace DlssgManager.Desktop;

public partial class MainWindow
{
    private static string ProviderName(ModProvider provider) => Localization.Get(provider == ModProvider.Anr ? "provider_anr" : "provider_sm86");

    private void InitializeProvider()
    {
        try { _provider = File.ReadAllText(Path.Combine(App.DataDirectory, "provider.txt")).Trim() == "Anr" ? ModProvider.Anr : ModProvider.Sm86; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        var argument = Array.IndexOf(App.Arguments, "--provider");
        if (argument >= 0 && argument + 1 < App.Arguments.Length)
            _provider = App.Arguments[argument + 1].Equals("anr", StringComparison.OrdinalIgnoreCase) ? ModProvider.Anr : ModProvider.Sm86;
    }

    private void RefreshProviderUi()
    {
        if (ProviderDescription is null) return;
        var anr = _provider == ModProvider.Anr;
        ProviderDescription.Text = Localization.Get(anr ? "provider_anr_description" : "provider_sm86_description");
        if (_record is not null)
            ProviderDescription.Text += "\n" + string.Format(Localization.Get("provider_installed"), ProviderName(_record.Provider));
        AnrOptionsPanel.Visibility = anr ? Visibility.Visible : Visibility.Collapsed;
        AnrSettingsHint.Visibility = anr ? Visibility.Visible : Visibility.Collapsed;
        AnrGameHelp.Visibility = anr ? Visibility.Visible : Visibility.Collapsed;
        VerificationScopeText.Text = Localization.Get(anr ? "anr_log_scope" : "log_scope");
        Sm86OptionsHelp.Visibility = anr ? Visibility.Collapsed : Visibility.Visible;
        ProxyPicker.ToolTip = Localization.Get(anr ? "anr_options_hint" : "proxy_help");
        foreach (var control in new Control[] { OptimizationPicker, PresetPicker, SrPicker, FlowPicker, HudPicker, LogPicker })
        {
            control.IsEnabled = !anr;
            if (control.Parent is FrameworkElement panel) panel.Visibility = anr ? Visibility.Collapsed : Visibility.Visible;
        }
        TimingCheck.Visibility = anr ? Visibility.Collapsed : Visibility.Visible;
        ProxyPicker.IsEnabled = !anr || _anrVariant != "addon";
        if (anr && _anrVariant == "addon")
        {
            var proxy = _record?.ExternalProxy;
            if (proxy is null && _scan?.RenderingDirectory is { } directory)
                proxy = new[] { "dxgi.dll", "winmm.dll", "version.dll" }.FirstOrDefault(p => File.Exists(Path.Combine(directory, p)));
            if (proxy is not null) Select(ProxyPicker, proxy);
        }
    }

    private async void Provider_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_changingChoices || _operation is not null || ProviderPicker.SelectedItem is not Choice selected) return;
        var provider = selected.Value == "Anr" ? ModProvider.Anr : ModProvider.Sm86;
        if (provider == _provider) return;
        _provider = provider;
        _package = null; _updateState = null; _anrVariant = "full";
        ResetGameAnalysis(false);
        RebuildChoices();
        Select(ProxyPicker, provider == ModProvider.Anr ? "dxgi.dll" : "version.dll");
        try { File.WriteAllText(Path.Combine(App.DataDirectory, "provider.txt"), provider.ToString()); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { SaveError(error); }
        await RunAsync(async token =>
        {
            await UpdatePackageAsync(token);
            await InspectHardwareAsync(token);
            if (!string.IsNullOrWhiteSpace(FolderPath.Text)) await AnalyzeAsync(token);
        });
    }
}
