using System.Text.RegularExpressions;

namespace DlssgManager.Core;

/// <summary>ANR-specific prerequisites. These do not inherit the SM86 runtime, driver or HAGS recipe.</summary>
public static class AnrSupport
{
    public static bool IsSupportedGpu(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        const RegexOptions options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        return (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)
            && Regex.IsMatch(name, @"\b(?:RTX\s+(?:20|30|40|50)\d{2}|GTX\s+16\d{2})\b", options))
            // Match the reviewed discrete Radeon RX families, not generic Radeon/APU names.
            // Four model digits and the bounded suffix also reject RX 580, RX 8000 and RX 79000.
            || Regex.IsMatch(name, @"^\s*(?:AMD\s+)?Radeon(?:\s*\(TM\))?\s+RX\s+[5679][0-9]{3}(?:\s*(?:XTX|XT|GRE|M(?:\s*XT)?|S))?(?:\s+Graphics)?\s*$", options);
    }

    /// <param name="systemDirectory">Optional isolated directory for deterministic prerequisite tests.</param>
    public static HardwareInfo AdaptHardware(HardwareInfo hardware, string? systemDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(hardware);
        var gpus = hardware.Gpus.Select(g => g with { Eligible = IsSupportedGpu(g.Name) }).ToArray();
        var checks = hardware.Checks.Where(c => c.Code is not ("gpu_supported" or "driver_r580" or "hags_configured"
            or "hags_runtime" or "gpu_architecture_unverified" or "gpu_native_fg")).ToList();
        checks.Add(new("anr_gpu_supported", gpus.Any(g => g.Eligible) ? CheckLevel.Pass : CheckLevel.Blocked,
            string.Join(" / ", gpus.Select(g => g.Name))));
        checks.Add(new("anr_driver_requirements", CheckLevel.Warning,
            string.Join(" / ", gpus.Where(g => g.Eligible).Select(g => g.Name + " · " + g.DriverVersion))));
        checks.Add(new("anr_hags", CheckLevel.Info,
            hardware.HagsConfigured is true ? "Configured" : hardware.HagsConfigured is false ? "Not configured" : "Unknown"));
        var system = systemDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.System);
        var required = new[] { "MSVCP140.dll", "VCRUNTIME140.dll", "VCRUNTIME140_1.dll" };
        var missing = required.Where(name => !IsX64File(Path.Combine(system, name))).ToArray();
        checks.Add(new("anr_vc_runtime", missing.Length == 0 ? CheckLevel.Pass : CheckLevel.Blocked,
            missing.Length == 0 ? "Microsoft Visual C++ 2015–2022 x64" : string.Join(", ", missing)));
        checks.Add(new("anr_vulkan_loader", IsX64File(Path.Combine(system, "vulkan-1.dll")) ? CheckLevel.Pass : CheckLevel.Blocked));
        return hardware with { Gpus = gpus, Checks = checks };
    }

    public static string RequiredVariant(GameScan scan)
    {
        ArgumentNullException.ThrowIfNull(scan);
        return scan.Checks.Any(c => c.Code == "anr_addon" && c.Level == CheckLevel.Pass
            || c.Code == "anr_variant" && c.Detail == "addon") ? "addon" : "full";
    }

    private static bool IsX64File(string path)
    {
        try { return File.Exists(path) && DetectionPe.Read(path).X64; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or BadImageFormatException or ArgumentException)
        { return false; }
    }
}
