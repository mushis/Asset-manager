using System.Globalization;
using System.Text.Json;
using Avalonia.Markup.Xaml;

namespace QuakeAssetManager;

/// <summary>
/// UI translations. The English text is the key; lang/&lt;code&gt;.json (next to the app) maps it to the translation.
/// A missing entry falls back to English, so a new string never shows up blank.
/// Folder names, file names, asset type names and the json configuration are never translated.
/// </summary>
public static class Loc
{
    public sealed record Language(string Code, string NativeName)
    {
        public override string ToString() => NativeName;
    }

    public static readonly IReadOnlyList<Language> Languages =
    [
        new("en", "English"),
        new("fr", "Français"),
        new("es", "Español"),
        new("pt", "Português"),
        new("sv", "Svenska"),
        new("fi", "Suomi"),
        new("ru", "Русский"),
    ];

    private static Dictionary<string, string> _strings = [];

    public static string Current { get; private set; } = "en";

    public static string Folder => Path.Combine(AppContext.BaseDirectory, "lang");

    public static void Set(string? code)
    {
        code = Languages.Any(l => l.Code == code) ? code! : "en";
        Current = code;
        _strings = [];
        if (code == "en") return;
        try
        {
            var path = Path.Combine(Folder, code + ".json");
            if (File.Exists(path))
                _strings = JsonSerializer.Deserialize(File.ReadAllText(path), LangJson.Default.DictionaryStringString) ?? [];
        }
        catch (Exception e) when (e is IOException or JsonException) { }
        try
        {
            var culture = CultureInfo.GetCultureInfo(code);
            CultureInfo.CurrentUICulture = culture;
        }
        catch (CultureNotFoundException) { }
    }

    /// <summary>Translate a UI text.</summary>
    public static string T(string english) =>
        _strings.TryGetValue(english, out var t) && !string.IsNullOrEmpty(t) ? t : english;

    /// <summary>Translate a text with {0}, {1}... placeholders, then fill them in.</summary>
    public static string F(string english, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(english), args);

    /// <summary>"1 file" / "{0} files" with the right form.</summary>
    public static string Files(int n)
    {
        if (n == 1) return Loc.T("1 file");
        // Russian has a separate form for 2-4 (and 22-24, ...), except 12-14.
        if (Current == "ru" && n % 10 is >= 2 and <= 4 && n % 100 is < 12 or > 14)
            return Loc.F("{0} files#few", n);
        if (Current == "ru" && n % 10 == 1 && n % 100 != 11)
            return Loc.F("{0} files#one", n);
        return Loc.F("{0} files", n);
    }
}

/// <summary>JSON code generated at build time for lang/*.json (safe to trim).</summary>
[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class LangJson : System.Text.Json.Serialization.JsonSerializerContext;

/// <summary>XAML: Text="{local:T 'Show invalid'}"</summary>
public sealed class TExtension(string text) : MarkupExtension
{
    public string Text { get; set; } = text;

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Text);
}
