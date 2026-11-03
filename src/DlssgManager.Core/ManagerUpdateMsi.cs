using System.Runtime.InteropServices;
using System.Text;

namespace DlssgManager.Core;

internal static class ManagerUpdateMsi
{
    internal const string UpgradeCode = "B704AF01-12FC-43EF-AB32-A3859FB716D0";
    internal static void Validate(string path, string expectedVersion)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows Installer updates require Windows.");
        ManagerUpdateService.RejectReparsePath(path);
        var error = MsiOpenDatabase(path, IntPtr.Zero, out var database);
        if (error != 0) throw new InvalidDataException("The downloaded update is not a readable Windows Installer package.");
        try
        {
            if (Property(database, "ProductName") != "DLSSG Mod Manager"
                || !string.Equals(Property(database, "UpgradeCode")?.Trim('{', '}'), UpgradeCode, StringComparison.OrdinalIgnoreCase)
                || !ManagerUpdateService.TryVersion(Property(database, "ProductVersion"), out var packageVersion)
                || !ManagerUpdateService.TryVersion(expectedVersion, out var expected) || packageVersion != expected)
                throw new InvalidDataException("The installer identity or version does not match the manager update.");
            var allUsers = Property(database, "ALLUSERS");
            if (allUsers == "1") throw new InvalidDataException("A machine-wide installer cannot replace this per-user manager installation.");
        }
        finally { MsiCloseHandle(database); }
    }
    private static string? Property(uint database, string name)
    {
        uint view = 0, record = 0;
        try
        {
            if (MsiDatabaseOpenView(database, "SELECT `Value` FROM `Property` WHERE `Property`='" + name + "'", out view) != 0
                || MsiViewExecute(view, 0) != 0 || MsiViewFetch(view, out record) != 0) return null;
            uint length = 0;
            var result = MsiRecordGetString(record, 1, null, ref length);
            if (result is not (0 or 234) || length > 4096) return null;
            var text = new StringBuilder((int)length + 1); length++;
            return MsiRecordGetString(record, 1, text, ref length) == 0 ? text.ToString() : null;
        }
        finally { if (record != 0) MsiCloseHandle(record); if (view != 0) MsiCloseHandle(view); }
    }
    [DllImport("msi.dll", CharSet = CharSet.Unicode, EntryPoint = "MsiOpenDatabaseW")]
    private static extern uint MsiOpenDatabase(string path, IntPtr persist, out uint database);
    [DllImport("msi.dll", CharSet = CharSet.Unicode, EntryPoint = "MsiDatabaseOpenViewW")]
    private static extern uint MsiDatabaseOpenView(uint database, string query, out uint view);
    [DllImport("msi.dll")] private static extern uint MsiViewExecute(uint view, uint record);
    [DllImport("msi.dll")] private static extern uint MsiViewFetch(uint view, out uint record);
    [DllImport("msi.dll", CharSet = CharSet.Unicode, EntryPoint = "MsiRecordGetStringW")]
    private static extern uint MsiRecordGetString(uint record, uint field, StringBuilder? value, ref uint length);
    [DllImport("msi.dll")] private static extern uint MsiCloseHandle(uint handle);
}
