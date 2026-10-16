# Quake Asset Manager

A desktop app for Windows and Linux that manages QuakeWorld (ezQuake) replacement assets: model skins, item boxes,
HUD pictures, the frag tracker, particles and more. These assets aren't configurable in the client. Each one needs
a specific file name in a specific folder, and switching between versions means juggling files by hand.

The app does the juggling:
- It keeps a copy of every version in an **Assets** folder.
- It shows you which file the game uses now.
- It swaps a file, or a whole set of files, into your Quake folder with one click.

## Download

Get the latest release from the [Releases](https://github.com/mushis/Asset-manager/releases) page.

- **Windows:** `QuakeAssets-<version>-win-x64.zip`. Unzip it anywhere, e.g. `C:\QuakeAssets`, and run `QuakeAssetManager.exe`.
- **Linux:** `QuakeAssets-<version>-linux-x64.tar.gz`. Extract it and run `QuakeAssets/QuakeAssetManager`. A `.desktop` file and an icon are included.

You don't need to install .NET; the packages include everything. The app is portable: its settings live in
`config/` next to the executable, so the whole app is one folder.

## Features

- **Asset types:** Models, BModels, HUD, Tracker and Particles are shown by default. Crosshairs, Skyboxes, Gfx,
  Charsets, Levelshots, Skins and Progs can be switched on in Settings.
- **For each type:**
  - **Active:** what the game loads now.
  - **Local:** your Assets folder, with the file that's in the game marked "in game".
  - **Online:** browse and download from [gfx.quakeworld.nu](https://gfx.quakeworld.nu).
- **Thumbnails and previews:** PNG, JPG, TGA and PCX, and Quake palette images (`.mdl` skins, `.lmp`, WAD lumps).
- **Drag and drop:** zip, pk3, pak, rar, 7z and tar.gz archives, folders and images. Each file goes to the right
  asset type. You're asked when it's unclear, and you see a summary with **Revert** before anything is added.
- **Archives tab:** browse the PAK and PK3 files in your Quake folder, and copy files from the `qw` ones to Assets.
- **Test:** starts your client on a chosen map so you can check your changes.
- **Languages:** English, French, Spanish, Portuguese, Swedish, Finnish and Russian.
- **Configurable:** asset types, their folders, extensions and valid file names are JSON files you can edit
  (see below).

## Configuration

These files are in `config/` next to the app. If that folder can't be written to, they go in
`%AppData%\QuakeAssetManager` on Windows or `~/.config/QuakeAssetManager` on Linux.

| File | What it holds |
|---|---|
| `settings.json` | Quake folder, Assets folder, client, map to open, language |
| `asset-types.json` | One entry per asset type: shown or hidden, game folder, Assets folder name, extensions, name sources, how loose files are recognised, archive folders, online lists |
| `names/<type>.json` | Extra valid file names (`names`) and patterns (`patterns`) for each type |
| `app.json` | The online site's name and address |

Restart the app after editing these files. New asset types can be added to `asset-types.json`.

The rules for which file names the game loads come from the ezQuake source code. They're documented in
[docs/qw-folder-names.md](docs/qw-folder-names.md).

## Rules the app follows

- **The Assets folder always keeps a copy.** Every scan copies new loose files from the game folders into a dated
  folder (e.g. `16 Oct 2026`), skipping content that's already there. Subfolders of the game folders are offered
  in a dialog instead.
- **No loose files in `assets/<type>/`:** files always go into a subfolder.
- **Lower case:** files copied into the game are lower case, since Linux is case sensitive.
- **Archives are never changed:** PAK and PK3 files are only read. Files copied from them go to
  `assets/<type>/<archive name>/`.
- **Delete is undoable:** it sends items to the Recycle Bin (Windows) or the trash (Linux).

## Building from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet run --project src/QuakeAssetManager
dotnet test
python tools/release/make-release.py 0.4.0
```

The last command builds both release packages into `release/<version>/`.

For testing, `QAM_SETTINGS=<file>` points the app at a different settings file. In Debug builds,
`QAM_SNAPSHOT=<folder>` walks through the screens, saves a PNG of each and exits.

**Layout:**
- `src/QuakeAssetManager.Core`: everything that isn't UI.
  - asset types and configuration
  - valid names
  - importing and routing files to the right folder
  - PAK/PK3/WAD/MDL/BSP readers
  - image decoders
- `src/QuakeAssetManager.Online`: the online catalog, behind `IOnlineCatalog`. All of gfx.quakeworld.nu's HTML reading is in `GfxPageParser`.
- `src/QuakeAssetManager`: the Avalonia UI.
- `tests/QuakeAssetManager.Tests`: unit tests. Some also read a local nQuake install, read-only, and are skipped when it isn't there.
- `tools/i18n`: translation tools (see its README).
- `tools/release`: the release packaging script.

## Credits

- Made with love by mushi. Vibe coded with Claude.
- The icon is from [gfx.quakeworld.nu](https://gfx.quakeworld.nu/files/650.jpg).
