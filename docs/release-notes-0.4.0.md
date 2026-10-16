# Quake Asset Manager 0.4.0

First public release. Windows and Linux, x64, self-contained (no .NET install needed).

## Install

- **Windows:** unzip `QuakeAssets-0.4.0-win-x64.zip` anywhere, e.g. `C:\QuakeAssets`, and run `QuakeAssetManager.exe`.
- **Linux:** extract `QuakeAssets-0.4.0-linux-x64.tar.gz` and run `QuakeAssets/QuakeAssetManager`.

The app is portable: settings and configuration are kept in `config/` next to the executable. Settings from
earlier versions are copied there on the first start.

## What's new in 0.4

**Files go to the right folder:**
- **Archives:** a file inside a pak/pk3/zip goes to the asset type of the folder it sits in. Folders the app can't
  place are asked about, with the type pre-selected when the file names point to exactly one.
- **Loose files:** a file you drop is recognised by its folder, or by its name for types with fixed name lists.
  Anything else is asked about. Files in folders the app doesn't manage (map textures, sounds, maps, radars…) are
  listed and left out.
- **Hidden types:** files of hidden asset types are copied too.
- **Renamed alternatives:** a file in a type's folder with a name the game doesn't load (e.g. `particles/bps.png`)
  is copied and marked invalid, so you can rename it to use it.

**Asset types:**
- **Gfx:** replaces Conbacks and covers all of `qw/gfx`.
- **Progs:** accepts any model name, and the skins next to `.md3` models.
- **Levelshots:** accepts maps that aren't installed.
- **HUD:** accepts every `gfx.wad` picture name.
- **Crosshairs:** accepts `crosshair.txt`.
- **Removed:** Maps, Lits and Locs.

**Scanning your Quake folder:** subfolders found in the game folders (alternative sets such as `original/`) are
offered in a dialog, with **Copy** or **Copy and delete**.

**Active list:**
- **Delete** for every file, with an option to also delete its copies in Assets.
- No more "original" / "from" labels. The Local list marks the file that's "in game".

**Other changes:**
- Asset type tabs wrap onto more rows when they don't fit.
- New app icon.
- Plural fixes in all languages.

All UI texts are available in English, French, Spanish, Portuguese, Swedish, Finnish and Russian.
