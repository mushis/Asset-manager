# Quake Asset Manager 0.5.0

Windows and Linux, x64, self-contained (no .NET install needed).

## Install

- **Windows:** unzip `QuakeAssets-0.5.0-win-x64.zip` anywhere, e.g. `C:\QuakeAssets`, and run `QuakeAssetManager.exe`.
- **Linux:** extract `QuakeAssets-0.5.0-linux-x64.tar.gz` and run `QuakeAssets/QuakeAssetManager`.

To update 0.4, unzip 0.5 into a new folder and copy your old `config/` folder into it (0.4's many runtime files
aren't needed any more). Your settings and asset types are kept. Built-in asset types get the 0.5 definitions; types you added yourself stay as they are.

**Smaller download:** the app is now a single executable of about 23 MB, down from about 200 files and 110 MB.
The folder holds just the executable, `README.md`, `lang/` and the icon. The first start after an update takes a
moment longer while the executable unpacks.

## What's new in 0.5

**New look:** a new QAM logo and app icon, and a welcome screen at start that explains what the app does (tick "Don't show this again" to skip it). Settings links to the project page on GitHub and to the wiki on QuakeWorld.nu.

**Themes:** Settings > General > Theme offers **Rust** (the default), **Slate** and **Parchment** (light). The
theme changes as you pick it.

**Active list (the game folder):**
- **Rename…** for every file. Names are lower-cased, with a warning if the game won't load the new name.
- **Show alternatives:** shows every version of the file in your Local assets.
- **Mark as valid:** adds the name to 
ames/<type>.json`, so it counts as valid everywhere.
- **Move to Local assets…** keeps the file name, unless you tick "Rename file to".
- **Show invalid** is on by default and only affects this list.

**Local assets list (the Local tab):**
- **Filter box:** shows the files whose names start with what you type, in every folder.
- **Nested folders** can be used ("Use these") and moved up to the root.
- **Invalid files:** Mark as valid, or Copy to game folder to rename them there.
- The Local tab's tooltip shows the full path of the Local assets folder. "New folder" is now an icon button.

**Selecting several files:** Ctrl+click and Shift+click work in both lists. You can then move, mark as valid, use
or delete them in one go.

**Client settings in the preview:** the preview names the setting that decides whether a file is used, e.g.
"Used when gl_simpleitems is 1" for `simple_*` items or "Chosen with crosshairimage". The rules are in
`asset-types.json` (`hints`). Background: `docs/cvars-and-assets.md`.

**Types where the client picks the file by name** (Gfx, Charsets, Levelshots, Skins, Crosshairs) no longer mark
any name invalid. Their tooltip says "Available to the game", since a client setting decides which one is used.

**Archives and dropped files:**
- Any file in any PAK/PK3, in every game folder, can be copied to Local assets.
- **assets/other:** files the app doesn't manage (sounds, maps, map textures…) are now kept in `assets/other`,
  with their game path, e.g. `assets/other/sound/weapons/coilgun.wav`. If that path is already taken by a
  different file, the new one gets a number: `abc (2).png`.
- **"Where should these files go?"** offers **Other**, and asks which Local assets folder to use (the archive's name to
  start with).

**Settings > Tools:**
- **Convert to lower case:** finds files with upper-case letters (Linux only loads exact names) and renames the
  ones you tick.
- **Find duplicates in the active Quake folder:** lists paths that exist in several places (loose files and
  inside pak/pk3), marks the copy the game uses and the identical ones, previews the copies side by side, and
  exports the list as a text file (to Downloads by default).
- **Find duplicates in the Local assets folders:** finds exact copies (same name, size and contents) in your
  Local assets. You choose which copy to keep, see the full list of files to delete, and confirm.

**Other changes:**
- **unezQuake:** two new asset types, **Sprays** (`qw/sprays`, with a warning for images over 128×128) and
  **Emoticons** (`qw/textures/emoticons`). Both are hidden by default.
- **Online tab:** can be switched off in Settings > General.
- The Archives button looks different from the asset type tabs.
- The Settings buttons wrap onto more rows when they don't fit.
- **Subfolders found at start:** the recommended choice is now **Move** (copy to Local assets, then send the
  originals to the Recycle Bin).
- "Options" are now called **alternatives**, and the Assets folder is called **Local assets** throughout.
- Help texts (?) and tooltips were reviewed throughout.

All UI texts are available in English, French, Spanish, Portuguese, Swedish, Finnish and Russian.
