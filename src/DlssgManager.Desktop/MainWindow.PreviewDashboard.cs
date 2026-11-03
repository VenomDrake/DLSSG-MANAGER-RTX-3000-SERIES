using System.IO;
using System.Windows;

namespace DlssgManager.Desktop;

public partial class MainWindow
{
    private void RefreshPreviewDashboard()
    {
        if (SelectedGameTitle is null || NextActionText is null) return;
        string? gameName = null;
        if (_scan is not null)
        {
            try
            {
                var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_scan.Root));
                gameName = _steamGames.FirstOrDefault(g => root.Equals(Path.TrimEndingDirectorySeparator(g.Directory), StringComparison.OrdinalIgnoreCase)
                    || root.StartsWith(Path.TrimEndingDirectorySeparator(g.Directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))?.Name
                    ?? Path.GetFileName(root);
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { }
        }
        SelectedGameTitle.Text = string.IsNullOrWhiteSpace(gameName) ? Localization.Get("ui_next_select") : gameName;
        var nextKey = _readOnly ? "ui_preview_disabled" : _operation is not null ? "busy"
            : _scan is null ? "ui_next_select" : PrechecksBlocked() ? "ui_next_blocked"
            : IsGameCurrent() ? "up_to_date" : "ui_next_ready";
        NextActionText.Text = Localization.Get(nextKey);
        InstallButton.ToolTip = _readOnly ? Localization.Get("ui_preview_description") : NextActionText.Text;
        LaunchButton.ToolTip = _readOnly ? Localization.Get("ui_preview_description") : Localization.Get("launch_note");
    }

    private void ViewChecks_Click(object sender, RoutedEventArgs e) => DiagnosticsTab.IsSelected = true;
    private void ViewOptions_Click(object sender, RoutedEventArgs e) => SettingsTab.IsSelected = true;
}
