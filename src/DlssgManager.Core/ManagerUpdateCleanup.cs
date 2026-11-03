using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DlssgManager.Core;

internal static class ManagerUpdateCleanup
{
    internal const string ActivityFileName = "update-active.lock";

    internal static void Prune(string cacheRoot, string currentVersion)
    {
        if (!ManagerUpdateService.TryVersion(currentVersion, out var current)) return;
        try
        {
            if (Path.GetFileName(cacheRoot) != "manager-updates" || !Directory.Exists(cacheRoot)) return;
            ManagerUpdateService.RejectReparsePath(cacheRoot);
            var dataDirectory = Path.GetDirectoryName(cacheRoot)!;
            var completed = new List<(string Path, DateTime Finished)>();
            foreach (var directory in Directory.EnumerateDirectories(cacheRoot))
            {
                try
                {
                    if (!IsOperation(cacheRoot, directory)) continue;
                    ManagerUpdateService.RejectReparsePath(directory);
                    var resultPath = Path.Combine(directory, "update-result.json");
                    var result = ManagerUpdateWorker.ReadResult(resultPath, dataDirectory);
                    if (result is { Success: true, Code: "manager_update_completed" }
                        && ManagerUpdateService.TryVersion(result.ToVersion, out var to) && to <= current)
                        completed.Add((directory, File.GetLastWriteTimeUtc(resultPath)));
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
            }
            if (completed.Count < 2) return;
            var activePaths = RunningManagerPaths();
            if (activePaths is null) return; // An inaccessible manager process might own any operation.
            foreach (var item in completed.OrderByDescending(c => c.Finished).ThenByDescending(c => c.Path, StringComparer.Ordinal).Skip(1))
            {
                try
                {
                    if (!IsOperation(cacheRoot, item.Path) || activePaths.Any(path => ManagerUpdateService.IsWithin(item.Path, path))
                        || !SafeOwnedTree(item.Path)) continue;
                    var activityPath = Path.Combine(item.Path, ActivityFileName);
                    ManagerUpdateService.RejectReparsePath(activityPath);
                    // A live helper holds this file with FileShare.None. Our own delete sharing permits cleanup.
                    using var activity = new FileStream(activityPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Delete);
                    if (!SafeOwnedTree(item.Path)) continue;
                    // Revalidate absolute boundaries immediately before recursive deletion.
                    if (IsOperation(cacheRoot, item.Path)) Directory.Delete(Path.GetFullPath(item.Path), true);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException) { }
    }

    private static bool IsOperation(string cacheRoot, string directory) =>
        ManagerUpdateService.SamePath(Path.GetDirectoryName(Path.GetFullPath(directory))!, cacheRoot)
        && Regex.IsMatch(Path.GetFileName(directory), @"\Aupdate-[0-9a-f]{32}\z", RegexOptions.CultureInvariant);

    private static IReadOnlyList<string>? RunningManagerPaths()
    {
        var paths = new List<string>();
        if (Environment.ProcessPath is { } ownPath) paths.Add(ownPath);
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ManagerUpdateService.ExeName)))
        {
            using (process)
            {
                try
                {
                    if (!process.HasExited && process.MainModule?.FileName is { } path) paths.Add(path);
                }
                catch (InvalidOperationException) when (process.HasExited) { }
                catch (System.ComponentModel.Win32Exception) { return null; }
            }
        }
        return paths;
    }

    private static bool SafeOwnedTree(string operation)
    {
        ManagerUpdateService.RejectReparsePath(operation);
        var pending = new Stack<string>(); pending.Push(operation); var count = 0;
        while (pending.TryPop(out var directory))
        {
            ManagerUpdateService.RejectReparsePath(directory);
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (++count > 128 || !ManagerUpdateService.IsWithin(operation, entry)) return false;
                ManagerUpdateService.RejectReparsePath(entry);
                var parts = Path.GetRelativePath(operation, entry).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var isDirectory = (File.GetAttributes(entry) & FileAttributes.Directory) != 0;
                if (isDirectory)
                {
                    if (parts.Length != 1 || parts[0] is not ("candidate" or "worker" or "backup")) return false;
                    pending.Push(entry);
                }
                else if (parts.Length == 1)
                {
                    if (parts[0] is not ("update-plan.json" or "update-result.json" or ActivityFileName
                        or ManagerUpdateService.PortableAssetName or ManagerUpdateService.InstallerAssetName)) return false;
                }
                else if (parts.Length != 2 || parts[0] == "worker" && parts[1] != ManagerUpdateService.ExeName
                    || parts[0] is "candidate" or "backup" && !ManagerUpdateService.OwnedFiles.Contains(parts[1], StringComparer.Ordinal)
                        && !(parts[0] == "candidate" && parts[1] == ManagerUpdateService.InstallerAssetName)) return false;
            }
        }
        return true;
    }
}
