using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;

namespace DlssgManager.Desktop;

public sealed record LanguageChoice(string Code, string Name)
{
    public override string ToString() => Name;
}

public static class Localization
{
    public static IReadOnlyList<LanguageChoice> Languages { get; } =
    [new("en", "English"), new("it", "Italiano"), new("zh-Hans", "简体中文"), new("hi", "हिन्दी"),
     new("es", "Español"), new("fr", "Français"), new("ar", "العربية"), new("bn", "বাংলা"),
     new("pt", "Português"), new("ru", "Русский"), new("ur", "اردو")];
    public static string Current { get; private set; } = "en";
    private static Dictionary<string, string> _english = [];
    private static Dictionary<string, string> _active = [];
    private static string _settingsPath = "";
    public static event Action? Changed;

    public static void Initialize(string dataDirectory)
    {
        _english = Load("en");
        _settingsPath = Path.Combine(dataDirectory, "language.txt");
        string code;
        try { code = File.Exists(_settingsPath) ? File.ReadAllText(_settingsPath).Trim() : DetectLanguage(); }
        catch { code = DetectLanguage(); }
        Change(code, false);
    }
    private static string DetectLanguage()
    {
        var culture = CultureInfo.CurrentUICulture;
        if (culture.TwoLetterISOLanguageName == "zh") return "zh-Hans";
        return Languages.Any(l => l.Code == culture.TwoLetterISOLanguageName) ? culture.TwoLetterISOLanguageName : "en";
    }
    public static void Change(string code, bool save = true)
    {
        Current = Languages.Any(l => l.Code == code) ? code : "en";
        _active = Load(Current);
        foreach (var pair in _english) Application.Current.Resources[pair.Key] = _active.GetValueOrDefault(pair.Key, pair.Value);
        if (save) { try { File.WriteAllText(_settingsPath, Current); } catch { } }
        Changed?.Invoke();
    }
    public static string Get(string key) => _active.GetValueOrDefault(key, _english.GetValueOrDefault(key, key));
    public static FlowDirection Direction => Current is "ar" or "ur" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    public static Dictionary<string, string> Load(string code)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith($"Locales.{code}.json", StringComparison.Ordinal));
        if (name is null) return [];
        using var stream = assembly.GetManifestResourceStream(name)!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    }
}
