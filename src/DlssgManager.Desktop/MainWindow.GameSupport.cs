using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DlssgManager.Core;

namespace DlssgManager.Desktop;

public partial class MainWindow
{
    private bool _gameSupportAnalyzing;

    private void RefreshGameSupport()
    {
        if (GameSupportNotice is null) return;
        if (_scan is null && !_gameSupportAnalyzing)
        {
            GameSupportNotice.Visibility = Visibility.Collapsed;
            return;
        }
        var assessment = _scan is null ? null : GameSupportAssessment.Evaluate(_scan);
        var state = assessment?.State ?? GameSupportState.NeedsReview;
        var pcBlocked = state == GameSupportState.RequirementsDetected && PrechecksBlocked();
        if (pcBlocked) state = GameSupportState.InstallationBlocked;
        var confirmed = state == GameSupportState.RequirementsDetected && _verification?.Evaluated == true;
        var titleKey = _gameSupportAnalyzing ? "support_analyzing" : confirmed ? "verified" : state switch
        {
            GameSupportState.Unsupported => "support_unsupported",
            GameSupportState.InstallationBlocked => "support_install_blocked",
            GameSupportState.RequirementsDetected => "support_prechecks_passed",
            _ => "support_requires_action"
        };
        var (background, border, text, symbol) = state switch
        {
            GameSupportState.Unsupported => ("#321D29", "#854557", "#FFB1BD", "×"),
            GameSupportState.RequirementsDetected => ("#142B27", "#2B5F4D", "#70E5BB", "✓"),
            _ => ("#302719", "#725537", "#FFD49B", "!")
        };
        static Brush BrushFor(string value) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        GameSupportNotice.Background = BrushFor(background);
        GameSupportNotice.BorderBrush = BrushFor(border);
        GameSupportTitle.Foreground = BrushFor(text);
        GameSupportSymbol.Foreground = BrushFor(text);
        GameSupportSymbol.Text = _gameSupportAnalyzing ? "…" : symbol;
        GameSupportTitle.Text = Localization.Get(titleKey);
        GameSupportReason.Text = _gameSupportAnalyzing ? "" : confirmed ? Localization.Get(_provider == ModProvider.Anr ? "anr_log_scope" : "log_scope")
            : pcBlocked ? Localization.Get("support_reason_other") : string.Join("\n", assessment!.ReasonKeys.Select(Localization.Get));
        GameSupportReason.Visibility = string.IsNullOrWhiteSpace(GameSupportReason.Text) ? Visibility.Collapsed : Visibility.Visible;
        GameSupportNotice.Visibility = Visibility.Visible;
    }

    private void Folder_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (GameSummary is null || ExePicker is null) return;
        ResetGameAnalysis(true);
        SetStatus("select_game_first");
    }

    private void ResetGameAnalysis(bool clearSelection)
    {
        _logTimer.Stop();
        _scan = null;
        _record = null;
        _verification = null;
        if (clearSelection) _selectedExe = null;
        _changingChoices = true;
        ExePicker.ItemsSource = null;
        _changingChoices = false;
        VerificationStatus.Text = Localization.Get("not_verified");
        VerificationStatus.Foreground = (Brush)FindResource("Text");
        VerificationDetails.Text = "";
        VerificationMetricsText.Text = "";
        VerificationMetricsText.Visibility = Visibility.Collapsed;
        VerificationErrors.Text = "";
        RefreshSummaries();
        RenderChecks();
    }
}
