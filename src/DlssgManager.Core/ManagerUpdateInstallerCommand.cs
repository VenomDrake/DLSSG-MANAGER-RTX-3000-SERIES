using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DlssgManager.Core;

internal static class ManagerUpdateInstallerCommand
{
    internal const string LogFileName = "manager-install.log";

    internal static ProcessStartInfo CreateStartInfo(ManagerUpdatePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows Installer requires Windows.");
        if (plan.Mode != ManagerUpdateMode.Installed) throw new InvalidDataException("This update does not use Windows Installer.");
        var data = NormalizePath(plan.DataDirectory);
        var operation = NormalizePath(plan.OperationDirectory);
        var candidate = NormalizePath(plan.CandidateDirectory);
        var target = NormalizePath(plan.TargetExePath);
        if (!ManagerUpdateService.SamePath(Path.GetDirectoryName(operation)!, Path.Combine(data, "manager-updates"))
            || !Regex.IsMatch(Path.GetFileName(operation), @"\Aupdate-[0-9a-f]{32}\z", RegexOptions.CultureInvariant)
            || !ManagerUpdateService.SamePath(candidate, Path.Combine(operation, "candidate"))
            || ManagerUpdateService.IsWithin(operation, target))
            throw new InvalidDataException("The installer update paths are inconsistent.");
        ManagerUpdateService.ValidateTargetPath(target);
        var installDirectory = NormalizePath(Path.GetDirectoryName(target)!);
        var installer = NormalizePath(Path.Combine(candidate, ManagerUpdateService.InstallerAssetName));
        var log = NormalizePath(Path.Combine(operation, LogFileName));
        if (!Directory.Exists(operation) || !File.Exists(installer) || !ManagerUpdateService.IsWithin(operation, log)
            || !ManagerUpdateService.SamePath(Path.GetDirectoryName(log)!, operation))
            throw new InvalidDataException("The installer or its operation directory is missing.");
        foreach (var path in new[] { data, operation, candidate, installDirectory, installer, log }) ManagerUpdateService.RejectReparsePath(path);
        if (File.Exists(log) || Directory.Exists(log)) throw new IOException("The installer log already exists. Prepare a fresh update before retrying.");

        // Windows Installer parses PROPERTY="value with spaces" itself; quoting the whole
        // "PROPERTY=value with spaces" argument (as ArgumentList does) is not equivalent.
        // https://learn.microsoft.com/windows/win32/msi/command-line-options
        var arguments = "/i " + Quote(installer) + " /passive /norestart APPFOLDER=" + Quote(installDirectory) + " /L*v " + Quote(log);
        var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "msiexec.exe");
        if (arguments.Length + executable.Length + 4 >= 32767) throw new InvalidDataException("The installer command is too long.");
        return new ProcessStartInfo(executable)
        {
            Arguments = arguments, UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = operation
        };
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Any(c => c == '"' || char.IsControl(c)) || !Path.IsPathFullyQualified(path))
            throw new InvalidDataException("An installer path contains invalid characters or is not absolute.");
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (full.Any(c => c == '"' || char.IsControl(c))) throw new InvalidDataException("An installer path contains invalid characters.");
        return full;
    }

    // Paths are validated first, and all three values end in a filename or a non-root directory,
    // so no terminal backslash can interact with the closing quote.
    private static string Quote(string value) => "\"" + value + "\"";
}
