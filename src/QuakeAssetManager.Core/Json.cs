using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using QuakeAssetManager.Core.Import;

namespace QuakeAssetManager.Core;

/// <summary>
/// JSON code generated at build time instead of reflection, so the trimmed single-file release can't lose
/// anything the serializer needs. Configuration files use camelCase; settings.json keeps its PascalCase names.
/// Both are written readably (no < escapes), since people edit them by hand.
/// </summary>
[JsonSerializable(typeof(TypesFileModel))]
[JsonSerializable(typeof(NamesConfig))]
[JsonSerializable(typeof(AppConfig))]
internal sealed partial class ConfigJson : JsonSerializerContext
{
    public static ConfigJson Files { get; } = new(new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });
}

[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(ImportSession))]
internal sealed partial class PlainJson : JsonSerializerContext
{
    public static PlainJson Files { get; } = new(new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });
}
