# Version 0.5: planned changes

Status: built on 19 Oct 2026 (see `docs/release-notes-0.5.0.md`). Decisions from 19 Oct 2026 are recorded below.

## 1. Settings

- **Online tab on or off:** a "Show the Online tab" option (on by default), saved in `settings.json`. When it's off, the Local/Online switch is hidden and the Assets column shows Local only.
- **Asset types tab:** the buttons under the list wrap onto another row when they don't fit the width.
- **New "Tools" tab:** see section 9.

## 2. Header and look

- **Archives button:** looks different from the asset type tabs, so it doesn't read as one of them. For example, it gets an archive icon, an outline style and a small gap before it. The final look follows the style you pick in section 10.
- **Colours and style:** the user picks a theme in Settings (section 10).

## 3. Active list (the game folder)

**Show invalid:**
- On by default.
- Affects only the Active list. The Local list always shows every file.

**Types where any name is valid** (decided)
- For types where the client picks the file by a setting, nothing is marked invalid: Gfx, Charsets, Levelshots, Skins and Crosshairs.
- Skyboxes keep checking the `rt/bk/lf/ft/up/dn` ending, since a face without one is never loaded.
- For those types, tooltips don't say "used by the game", because the app can't know which file the client's setting points to. They say "Available to the game (the client setting decides which file is used)".

**Right-click menu and preview buttons:**

| Selection | Actions |
|---|---|
| One valid file | Rename…, Show alternatives, Open folder, Delete |
| One invalid file | Rename…, Mark as valid, Move to assets…, Delete |
| Several files | Move to assets…, Mark as valid (if any are invalid), Delete. No Rename, no preview |

- **Rename…:** available for every file. The new name is lower-cased, with a warning if the game won't load it.
- **Move to assets…:** the file keeps its name by default. A "Rename file to" checkbox turns on the name box, which is pre-filled with the suggested name, as now.
- **Show alternatives:** fills the new filter box above the Assets list (section 4) with the file name without its extension. The Assets list then shows every version of that file across all option folders.

## 4. Assets list (Local)

**Filter box in the header:**
- It has a "Filter…" placeholder and an × button that clears it.
- It filters as you type. It shows files, in every subfolder of the current asset type, whose names *start with* the text, and expands the folders that match.
- It's cleared when you change tab or asset type.

**Header buttons:**
- "New folder" becomes an icon (a folder with a ✱) with a tooltip.
- The Local tab's tooltip shows the full path of the Assets folder for this type.

**Nested folders** (e.g. `assets/models/pack/extra/`):
- **Move to the root:** moves the folder up to `assets/models/extra`, with a suffix if that name is taken.
- **Use these:** works for nested folders too. Their valid files are copied into the game folder as usual, ignoring the nesting.

**Invalid files:**
- **Copy to game folder:** copies the file into the Active folder with its name unchanged, e.g. to rename it there.
- **Mark as valid:** see section 5.

**Selecting several files:**
- Available actions: Use these files, Move to folder…, Mark as valid, Delete.
- Rename and the preview are off while several files are selected.

## 5. Mark as valid

- Any invalid file, in either list, gets **Mark as valid**.
- It adds the name, without extension, to `names/<type>.json` → `names`, saves the file and reloads the rules. The file then shows as valid everywhere.
- It also works on several selected files at once.
- To take a name off the list later, edit the json file.

## 6. Archives tab

**Any archive can be copied from.** PAK and PK3 files in every game folder (`id1`, `qw`, `ezquake`, mods) can be copied to Assets, not only those in `qw`.

**Any file can be copied.** Every file in an archive can now be copied, wherever it sits:
- **Type folders** (e.g. `textures/wad/`): the file goes to that type, in `assets/<type>/<archive name>/`.
- **Folders the app doesn't manage** (`sound/`, `maps/`, map textures…): the file goes to `assets/other/<same path>`, e.g. `assets/other/sound/weapons/coilgun.wav` (decided). `assets/other` doesn't appear as a tab, but the files are kept.
  - **Drag and drop:** sends these files to `assets/other` the same way.
  - **Name clashes** (decided): if a file with that path is already there, the imported one gets a number suffix, so nothing is replaced. If `assets/other/textures/abc.png` exists, the next import of `textures/abc.png` becomes `abc (2).png`, then `abc (3).png`, and so on (the first free number).
  - A file with exactly the same content as one already there is skipped, as everywhere else in the app.
- **Unclear folders:** the "Where should these files go?" dialog asks, as now. It also asks for the **target subfolder**, suggesting the archive's name (e.g. `nquake`), and "Other" is one of the choices.

## 7. Charsets: no font files (decided)

- `.ttf` and `.otf` are **not** added to Charsets.
- **Why:** ezQuake and unezQuake load TrueType fonts from the Windows Fonts folder (`font_facepath` / `fontload`), never from `textures/charsets`. A font file there would show as valid but would never be used.

## 8. Multiple selection

- **Both lists** (Active and Local) allow selecting several items with Ctrl+click and Shift+click.
- **Right-click menus** show only the actions that work on several files: no Rename, and no thumbnail preview. The preview pane then shows "N files selected".

## 9. Settings > Tools

**Convert to lower case**
- **What it's for:** on Linux the game only finds files whose names match exactly, and it asks for lower-case names. This tool finds files with upper-case letters in their name or extension.
- **Where it looks:** every Active folder of the shown asset types, and the Assets folder.
- **The dialog:**
  - One checkbox per file, plus a master checkbox.
  - Columns: current name → new name, and the folder.
  - **Convert** renames the ticked files.
- **Name clashes:** if the lower-case name already exists, the file is skipped and reported.

**Find duplicates** (decided: same path in different places)
- **What it's for:** the same file can exist in several places, and only one is used, which is confusing. This tool lists them, for example `id1/textures/abc.png` and `qw/textures/abc.png`.
- **What counts as a duplicate:** files with the same path inside a game folder (`id1`, `qw`, `ezquake`, mods) or inside a pak/pk3. These are the copies that hide each other in the game.
- **Where it looks:** the whole Quake folder, including inside `.pak` and `.pk3` files.
- **The dialog:**
  - Groups by path, with every location and size, and a mark when the contents are identical.
  - An **Export…** button saves a readable `.txt` report, one block per duplicated name with its locations indented.

## 10. Themes (decided: let the user choose)

- **Settings > General > Theme** offers **Rust** (dark brown, orange accent, square corners, the default and closest to today), **Slate** (blue-grey, teal accent, rounded corners) and **Parchment** (light, deep red accent).
- The choice is saved in `settings.json` and applied immediately.
- Each theme is one resource file of colours and corner radii, so adding a theme later is a new file.

## 11. Tooltips

Every tooltip and help bubble is reviewed for the new features, including the filter box, Show alternatives, Mark as valid, multiple selection, Tools, the Online switch and the new icon button. All new texts are translated into the 7 languages.

## 12. Release folder layout

- **Problem:** today the unpacked folder holds `QuakeAssetManager.exe` among about 200 runtime files.
- **Plan: single-file build.** Most of those files go inside the executable, so the folder becomes:
  ```
  QuakeAssets/
    QuakeAssetManager.exe     (or QuakeAssetManager on Linux)
    README.md
    lang/                      translations
    Assets/icon.png
    config/  cache/            created on first run
  ```
- **Native libraries:** the graphics libraries (Skia, HarfBuzz) are packed into the exe as well, and unpacked to a temp folder on first start.

**Exe size** (measured on the 0.4 code, win-x64):

| Build | Exe |
|---|---|
| Single file | 98 MB |
| + compression | 47 MB |
| + compression + trimming (unused .NET code removed) | 23 MB |

- **Where the size goes:** the .NET runtime (72 MB unpacked), the Skia/HarfBuzz graphics libraries (17 MB), Avalonia (9 MB) and WPF/WinForms/XML parts that come with the runtime (8 MB). The app itself is about 1 MB.
- **Compression:** no risk. The first start takes a little longer while the exe unpacks into memory.
- **Trimming:** removes .NET code the app never calls. The app's JSON reading uses reflection, which trimming can break, so 0.5 switches it to generated code (System.Text.Json source generation). A quick test of a trimmed 0.4 build started, read the config and showed the main window. Every screen still has to be checked, since some list bindings also use reflection.
- **Plan:** compression and trimming, if every screen passes the check. Otherwise compression only (47 MB).

## 13. unezQuake: Sprays and Emoticons added (decided)

Full report: `docs/unezquake-assets.md`. unezQuake keeps every ezQuake asset path and name rule, and adds two kinds of asset folder:

| Candidate | Folder | Name rule | Notes |
|---|---|---|---|
| **Sprays** | `qw/sprays/` (cvar `cl_spray_image_path`) | any name, chosen with `cl_spray_image` (default `spray`) | png/jpg/tga/pcx, at most 128×128. All images in the folder are loaded at start-up. Fits like Crosshairs. |
| **Emoticons** | `qw/textures/emoticons/` | fixed names, listed in `emoticons.json` | Off by default (`cl_emoticons 0`). `emoticons.pk3` ships 62 PNGs. A loose image replaces the one in the pk3. |

Both become asset types, hidden by default like the other extra types:
- **Sprays:**
  - Folder: `qw/sprays`.
  - Names: any image name.
  - Extensions: png, jpg, jpeg, tga, pcx.
  - Recognised by its folder.
  - Rule: a warning in the preview when an image is larger than 128×128.
- **Emoticons:**
  - Folder: `qw/textures/emoticons`.
  - Names: the names listed in `emoticons.json`. The names in nQuake's `emoticons.pk3` are the default list in `names/emoticons.json`.
  - Extensions: png and the other image formats.
  - Recognised by name.

Nothing else is new. A few new sound paths, the new menu using levelshots, and a background map setting aren't worth managing.

**TrueType fonts:** ezQuake and unezQuake load `.ttf` files from the **Windows Fonts folder** (`font_facepath` / `fontload`), not from the Quake folders. So `.ttf`/`.otf` in `textures/charsets` (section 7) would be stored but never loaded by the client. See the question below.

## 14. Cvar hints in the preview (decided: hint only)

Full report: `docs/cvars-and-assets.md`.

- **What it shows:** a line in the preview pane, under the file details, naming the client setting that decides whether the selected file is used. For example:
  - `simple_armor_1.png`: "Used when gl_simpleitems is 1".
  - `crosshairs/crosshair.txt`: "Used when crosshair is 1".
  - A crosshair image: "Chosen with crosshairimage".
  - A file in `textures/tracker`: "Shown when cl_useimagesinfraglog is 1".
  - A `_luma` file: "Drawn when gl_lumatextures is 1".
  - Files that need no setting (e.g. `.md3` replacements) show no line.
- **What it doesn't do:** the app doesn't read the client's config files, so it never says whether the setting is on now.
- **Where the rules live:** a `Hints` list per type in `asset-types.json`. Each hint has a name pattern, the cvar, and the value or "chosen with". The defaults come from the report.
- **Translation:** the sentences ("Used when {0} is {1}", "Chosen with {0}") are translated into the 7 languages. The cvar names and values are not translated.

## Open questions

None. Everything is decided (19 Oct 2026). Your empty list item was a stray line.
