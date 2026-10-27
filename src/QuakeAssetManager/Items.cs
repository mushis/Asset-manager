using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using QuakeAssetManager.Core;
using QuakeAssetManager.Core.Archives;
using QuakeAssetManager.Core.Formats;
using QuakeAssetManager.Online;

namespace QuakeAssetManager;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>A row that shows a thumbnail, loaded in the background.</summary>
public abstract class ThumbItem : Observable
{
    private Bitmap? _thumbnail;

    public Bitmap? Thumbnail { get => _thumbnail; set { Set(ref _thumbnail, value); Raise(nameof(HasThumbnail)); } }
    public bool HasThumbnail => _thumbnail is not null;

    /// <summary>File on disk to make the thumbnail from, or null when there's nothing to show.</summary>
    public virtual string? ThumbnailPath => null;

    /// <summary>Bytes to make the thumbnail from (archive entries), when there's no file on disk.</summary>
    public virtual Func<(string Name, byte[] Data, string? Extension, byte LumpType)>? ThumbnailSource => null;
}

/// <summary>A row in the Active list: a file or a subfolder of qw/textures/&lt;type&gt;.</summary>
/// <param name="AnyName">The type accepts any name, so a client setting decides which file is used (Gfx, Crosshairs...).</param>
public sealed class ActiveItem(ActiveEntry entry, bool anyName = false) : ThumbItem
{
    public ActiveEntry Entry { get; } = entry;
    public string Name => Entry.IsFolder ? Entry.Name + Path.DirectorySeparatorChar : Entry.Name;
    public string Icon => Entry.IsFolder ? "📁" : Entry.IsValid ? "🖼" : "⚠";
    public bool IsInvalid => !Entry.IsValid;
    public override string? ThumbnailPath => Entry.IsFolder ? null : Entry.FullPath;

    /// <summary>Only folders and invalid names get a label (0.4: no origin labels in the Active list).</summary>
    public string Badge => Entry.IsFolder ? $"{Loc.Files(Entry.FileCount)} · {Loc.T("not loaded")}"
        : !Entry.IsValid ? Loc.T("invalid") : "";

    public bool BadgeIsBad => Entry.IsFolder || !Entry.IsValid;

    public string ModifiedText => RelativeDate.Format(Entry.Modified);
    public string ModifiedTip => RelativeDate.Full(Entry.Modified);

    public string Tip => Entry.IsFolder
        ? Loc.T("Subfolders aren't loaded by the game. Right-click to rename it, move it to Local assets or delete it.")
        : !Entry.IsValid ? Loc.T("The game never loads this name. Right-click to rename it, mark it as valid, move it to Local assets or delete it.")
        : (anyName ? Loc.T("Available to the game (the client setting decides which file is used).") : Loc.T("Used by the game.")) + " " +
          (Entry.Backup is null ? Loc.T("Not in Local assets yet.") : Loc.F("A copy is in Local assets folder \"{0}\".", Entry.Backup));
}

/// <summary>A node in the Assets tree: a pack folder, a nested folder or a file.</summary>
public sealed class AssetNode : ThumbItem
{
    private bool _isExpanded;

    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public bool IsFolder { get; init; }
    /// <summary>A folder directly under assets/&lt;type&gt;/: the unit for "Use these".</summary>
    public bool IsPack { get; init; }
    public bool IsValid { get; init; }
    public bool IsActive { get; init; }
    public DateTime Modified { get; init; }
    public bool IsInvalid => !IsValid;
    public ObservableCollection<AssetNode> Children { get; } = [];
    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }
    public override string? ThumbnailPath => IsFolder ? null : FullPath;

    /// <summary>Files directly in this folder (shown in its label even while a filter hides some).</summary>
    public int FileCount { get; init; }
    /// <summary>Full path of the file or folder, shown in the tooltip.</summary>
    public string Location { get; init; } = "";
    /// <summary>Other files in Local assets with the same contents; null when there are none.</summary>
    public IReadOnlyList<string>? DuplicateOf { get; init; }
    public bool IsDuplicate => DuplicateOf is { Count: > 0 };

    public string Icon => IsFolder ? "📁" : IsValid ? "🖼" : "⚠";
    public string Badge => IsFolder
        ? (IsPack ? Loc.Files(FileCount) : Loc.Files(FileCount) + " · " + Loc.T("nested"))
        : IsActive ? Loc.T("in game") + (IsDuplicate ? " · " + Loc.T("duplicate") : "")
        : !IsValid ? Loc.T("invalid")
        : IsDuplicate ? Loc.T("duplicate") : "";
    public bool BadgeIsInGame => !IsFolder && IsActive;
    public bool BadgeIsDuplicate => !IsFolder && !IsActive && IsValid && IsDuplicate;
    public bool BadgeIsBad => IsInvalid;
    public string ModifiedText => RelativeDate.Format(Modified);
    public string ModifiedTip => RelativeDate.Full(Modified);
    public string Tip => (IsFolder
        ? (IsPack ? Loc.T("A folder of alternatives. Right-click to use all its files, rename it or delete it.")
                  : Loc.T("A folder inside a folder of alternatives. Right-click to use its files or move it to the root."))
        : IsValid ? Loc.T("Right-click and choose \"Use this file\" to copy it into the game.")
        : Loc.T("The game never loads this name. Right-click to rename it, mark it as valid, copy it to the game folder or delete it."))
        + "\n" + Location;
}

public sealed class ArchiveTreeNode
{
    public required string Name { get; init; }
    public string? ArchivePath { get; init; }
    public ObservableCollection<ArchiveTreeNode> Children { get; } = [];
    public string Icon => ArchivePath is null ? "📁" : "🗜";
    /// <summary>0.5: every archive can be copied from, so there's nothing to label.</summary>
    public string Badge => "";
}

/// <summary>A file inside an archive. WAD files inside archives are expanded into their lumps.</summary>
public sealed class ArchiveItem : ThumbItem
{
    public required GameArchive Archive { get; init; }
    public required ArchiveEntry Entry { get; init; }
    public WadLump? Lump { get; init; }
    public WadFile? Wad { get; init; }
    /// <summary>Where the file would go (filled in by the Archives view): an asset type, other, or asked.</summary>
    public string Destination { get; init; } = "";
    /// <summary>The asset type the file goes to (shown or hidden), from its folder in the archive.</summary>
    public AssetType? Target { get; init; }
    public string DisplayPath => Lump is null ? Entry.Path : $"{Entry.Path}/{Lump.Name}";
    public string Name => Lump?.Name ?? Entry.Name;
    public string SizeText => Previews.Size(Lump?.Size ?? Entry.Size);
    public AssetType? Type => Lump is null ? AssetType.FromArchivePath(Entry.Path) : null;
    public string Badge => Lump is not null ? Loc.T("WAD lump") : Destination;
    public string ModifiedText => Entry.Modified is { } m ? RelativeDate.Format(m) : "";
    public string ModifiedTip => Entry.Modified is { } m ? RelativeDate.Full(m) : Loc.T("PAK files don't store dates");

    public bool IsImage => Lump is not null || Core.Imaging.ImageDecoders.CanPreview(Entry.Extension) && Entry.Extension != ".mdl";

    public override Func<(string, byte[], string?, byte)>? ThumbnailSource => !IsImage ? null
        : Lump is not null && Wad is not null ? () => (Lump.Name, Wad.ReadLump(Lump), ".wadlump", Lump.Type)
        : () => (Entry.Name, Archive.ReadEntry(Entry), null, 0);
}

/// <summary>A card in the Online grid.</summary>
public sealed class OnlineItem(CatalogItem item, bool downloaded) : Observable
{
    private Bitmap? _thumbnail;
    private bool _downloaded = downloaded;

    public CatalogItem Item { get; } = item;
    public string Name => Item.Name;
    public string Category => Item.Category;
    public Bitmap? Thumbnail { get => _thumbnail; set => Set(ref _thumbnail, value); }
    public bool Downloaded { get => _downloaded; set => Set(ref _downloaded, value); }
}

public static class RelativeDate
{
    /// <summary>Today → "14:32", yesterday → "Yesterday", then "Last week", "Last month", "Last year", else the year.</summary>
    public static string Format(DateTime when, DateTime? now = null)
    {
        var today = (now ?? DateTime.Now).Date;
        var days = (today - when.Date).TotalDays;
        return days switch
        {
            <= 0 => when.ToString("HH:mm"),
            1 => Loc.T("Yesterday"),
            <= 7 => Loc.T("Last week"),
            <= 31 => Loc.T("Last month"),
            <= 365 => Loc.T("Last year"),
            _ => when.Year.ToString(),
        };
    }

    public static string Full(DateTime when) => when.ToString("dd MMM yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);
}
