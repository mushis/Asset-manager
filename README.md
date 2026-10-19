# Quake Asset Manager

A desktop app for Windows and Linux that manages QuakeWorld replacement assets: **model skins, item boxes,
HUD pictures, frag tracker, particles and more**. 

## Why
**Organize your own collection of assets that you have been downloading over the years (maybe even creating your own), on ONE folder, which is read by this app, and the app does the copying to your Quake installation(s), while keeping your archive safe.**

The app does the juggling:
- It keeps a copy of every version in an **Assets** folder. Drag and drop to the app, or manually copy your assets to the correct subfolders.
- It shows you which file the game uses now, and offers you to replace it without losing it.
- It swaps a file, or a whole set of files, into your Quake folder with one click.
- Downloads assets  from gfx.quakeworld.nu.
<table>
  <tr>
    <td width="50%">
      <img src="https://github.com/user-attachments/assets/02a2c8cb-6c30-48a1-9d85-e85100a6746c" alt="Screenshot 1" width="100%">
    </td>
    <td width="50%">
      <img src="https://github.com/user-attachments/assets/64451e99-7778-48ee-bf21-158da5a13560" alt="Screenshot 2" width="100%">
    </td>
  </tr>
  <tr>
    <td width="50%">
      <img src="https://github.com/user-attachments/assets/e40d215b-55a8-4c7c-8e83-d885f216a2b2" alt="Screenshot 3" width="100%">
    </td>
    <td width="50%">
      <img src="https://github.com/user-attachments/assets/2930a888-03b8-4fc9-8c67-e0a323f33c53" alt="Screenshot 3" width="100%">
">
    </td>
  </tr>
</table>

## Download

Get the latest release from the [Releases](https://github.com/mushis/Asset-manager/releases) page.

- **Windows:** `QuakeAssets-<version>-win-x64.zip`. Unzip it anywhere, e.g. `C:\QuakeAssets`, and run `QuakeAssetManager.exe`.
- **Linux:** `QuakeAssets-<version>-linux-x64.tar.gz`. Extract it and run `QuakeAssets/QuakeAssetManager`. A `.desktop` file and an icon are included.

You don't need to install .NET: the app is a single executable with everything included. It's portable: its
settings live in `config/` next to the executable, so the whole app is one folder.

## Features

- **Asset types:** Models, BModels, HUD, Tracker and Particles are shown by default. Crosshairs, Skyboxes, Gfx,
  Charsets, Levelshots, Skins, Progs, and unezQuake's Sprays and Emoticons can be switched on in Settings.
- **Configurable:** asset types, their folders, extensions, valid file names and setting hints are JSON files you
  can edit (see below).
- **For each type:**
  - **Active:** what the game loads now. Corresponds to the **currently selected game folder**. Rename, Show
    alternatives, Mark as valid, Move to Local assets, Delete.
  - **Local:** your Local assets folder, with the file that's in the game marked "in game", and a filter box.
    **This is your new archive of Quake assets.** Copy your stuff there.
  - **Online:** browse and download from [gfx.quakeworld.nu](https://gfx.quakeworld.nu). Can be switched off.
- **Several files at once:** Ctrl+click and Shift+click in both lists.
- **Thumbnails and previews:** PNG, JPG, TGA and PCX, and Quake palette images (`.mdl` skins, `.lmp`, WAD lumps).
  The preview names the client setting that decides whether a file is used (e.g. `gl_simpleitems`).
- **Drag and drop:** zip, pk3, pak, rar, 7z and tar.gz archives, folders and images. Each file goes to the right
  asset type. You're asked when it's unclear, and you see a summary with **Revert** before anything is added.
  Files the app doesn't manage (sounds, maps…) are kept in `assets/other` with their game path.
- **Archives tab:** browse the PAK and PK3 files in your Quake folder, and copy any file from them to Local assets.
- **Tools:** convert file names to lower case (for Linux), find duplicates in your Quake folder, and find exact
  copies in your Local assets.
- **Test:** starts your client on a chosen map so you can check your changes.
- **Themes:** Rust, Slate and Parchment.
- **Languages:** English, French, Spanish, Portuguese, Swedish, Finnish and Russian.


## Configuration

**Drag and drop to the app** or **copy your existing folders** containing assets to the Quake assets folder you selected. This is all you need to know.

The configuration files are in `config/` next to the app. **The app is fully configurable by json text!** If that folder can't be written to, they go in
`%AppData%\QuakeAssetManager` on Windows or `~/.config/QuakeAssetManager` on Linux.

| File | What it holds |
|---|---|
| `settings.json` | Quake folder, Local assets folder, client, map to open, language, theme, Online tab on or off |
| `asset-types.json` | One entry per asset type: shown or hidden, game folder, Local assets folder name, extensions, name sources, how loose files are recognised, archive folders, online lists, setting hints |
| `names/<type>.json` | Extra valid file names (`names`) and patterns (`patterns`) for each type |
| `app.json` | The online site's name and address |

Restart the app after editing these files. New asset types can be added to `asset-types.json`.

The rules for which file names the game loads come from the ezQuake source code. They're documented in
[docs/qw-folder-names.md](docs/qw-folder-names.md).

## Rules the app follows

- **The Local assets folder always keeps a copy.** Every scan copies new loose files from the game folders into a
  dated folder (e.g. `16 Oct 2026`), skipping content that's already there. Subfolders of the game folders are
  offered in a dialog instead.
- **No loose files in `assets/<type>/`:** files always go into a subfolder.
- **Lower case:** files copied into the game are lower case, since Linux is case sensitive.
- **Archives are never changed:** PAK and PK3 files are only read. Files copied from them go to
  `assets/<type>/<archive name>/`.
- **Nothing is replaced in `assets/other`:** a different file with a path that's already there gets a number,
  e.g. `abc (2).png`. Identical files are skipped.
- **Delete is undoable:** it sends items to the Recycle Bin (Windows) or the trash (Linux).

## Building from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet run --project src/QuakeAssetManager
dotnet test
python tools/release/make-release.py 0.5.0
```

The last command builds both release packages into `release/<version>/`: one compressed, trimmed executable each.

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
- The QAM logo and icon were made for the app in 0.5. The earlier icon was made by Samon, and downloaded from
  [gfx.quakeworld.nu](https://gfx.quakeworld.nu/files/650.jpg).
