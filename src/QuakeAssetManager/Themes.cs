using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace QuakeAssetManager;

/// <summary>
/// Colour themes (Settings &gt; General &gt; Theme). Each theme is one resource file in Themes/: colours and corner
/// radii, plus the Fluent palette it sets. Adding a theme is a new file and a line in <see cref="All"/>.
/// </summary>
public static class Themes
{
    /// <param name="Create">Builds the theme's resource file (Themes/&lt;Name&gt;.axaml).</param>
    public sealed record Theme(string Id, string Name, Func<ResourceDictionary> Create)
    {
        public override string ToString() => Loc.T(Name);
    }

    public static readonly IReadOnlyList<Theme> All =
    [
        new("rust", "Rust", () => new ThemeFiles.Rust()),
        new("slate", "Slate", () => new ThemeFiles.Slate()),
        new("parchment", "Parchment", () => new ThemeFiles.Parchment()),
    ];

    private static ResourceDictionary? _current;

    public static string CurrentId { get; private set; } = "rust";

    public static Theme Find(string? id) => All.FirstOrDefault(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? All[0];

    /// <summary>Switch the whole app to a theme, immediately.</summary>
    public static void Apply(string? id)
    {
        if (Application.Current is not { } app) return;
        var theme = Find(id);
        var dict = theme.Create();
        var merged = app.Resources.MergedDictionaries;
        if (_current is not null) merged.Remove(_current);
        merged.Add(dict);
        _current = dict;
        CurrentId = theme.Id;

        var variant = dict.TryGetValue("ThemeVariant", out var v) && v as string == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        if (app.Styles.OfType<FluentTheme>().FirstOrDefault() is { } fluent)
        {
            if (!fluent.Palettes.TryGetValue(variant, out var palette))
                fluent.Palettes[variant] = palette = new ColorPaletteResources();
            Color C(string key) => dict.TryGetValue("Palette." + key, out var c) && c is Color color ? color : default;
            palette.Accent = C("Accent");
            palette.RegionColor = C("RegionColor");
            palette.AltHigh = C("AltHigh");
            palette.AltMediumHigh = C("AltMediumHigh");
            palette.AltMedium = C("AltMedium");
            palette.AltMediumLow = C("AltMediumLow");
            palette.AltLow = C("AltLow");
            palette.BaseHigh = C("BaseHigh");
            palette.BaseMediumHigh = C("BaseMediumHigh");
            palette.BaseMedium = C("BaseMedium");
            palette.BaseMediumLow = C("BaseMediumLow");
            palette.BaseLow = C("BaseLow");
            palette.ChromeLow = C("ChromeLow");
            palette.ChromeMediumLow = C("ChromeMediumLow");
            palette.ChromeMedium = C("ChromeMedium");
            palette.ChromeHigh = C("ChromeHigh");
            palette.ChromeAltLow = C("ChromeAltLow");
            palette.ListLow = C("ListLow");
            palette.ListMedium = C("ListMedium");
        }
        app.RequestedThemeVariant = variant;
    }

    /// <summary>A brush from the current theme (for controls built in code).</summary>
    public static IBrush Brush(string key, string fallback = "#808080") =>
        Application.Current?.TryGetResource(key, Application.Current.ActualThemeVariant, out var value) == true && value is IBrush brush
            ? brush
            : new SolidColorBrush(Color.Parse(fallback));
}
