using Avalonia.Controls;
using Avalonia.Markup.Xaml;

// One class per theme file, so a theme is created with compiled XAML (safe to trim) instead of loaded by name.
namespace QuakeAssetManager.ThemeFiles;

public partial class Rust : ResourceDictionary
{
    public Rust() => AvaloniaXamlLoader.Load(this);
}

public partial class Slate : ResourceDictionary
{
    public Slate() => AvaloniaXamlLoader.Load(this);
}

public partial class Parchment : ResourceDictionary
{
    public Parchment() => AvaloniaXamlLoader.Load(this);
}
