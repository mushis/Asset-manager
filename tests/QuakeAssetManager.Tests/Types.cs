using QuakeAssetManager.Core;

namespace QuakeAssetManager.Tests;

/// <summary>The built-in asset types (tests don't load asset-types.json).</summary>
public static class Types
{
    public static AssetType Models => AssetType.Get("models");
    public static AssetType BModels => AssetType.Get("bmodels");
    public static AssetType Wad => AssetType.Get("wad");
    public static AssetType Tracker => AssetType.Get("tracker");
    public static AssetType Particles => AssetType.Get("particles");
}
