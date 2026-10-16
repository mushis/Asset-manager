# Next version: planned changes

Status: built as version 0.2 (16 Oct 2026). Also added in 0.2: Tracker and Particles asset types, and an About dialog.

## 0. Rename "WAD" to "HUD" in the app

- **Shown as "HUD":** the tab, headers, dialogs, the drop summary and help text.
- **Still `wad` on disk:** `qw/textures/wad` and `assets/wad` keep their names, because the game reads them from there. The code keeps the `AssetKind.Wad` name.
- **Tab tooltip:** "HUD pictures (status bar, numbers, icons): qw\textures\wad".

## 1. Modified date

**Lists** (Active, Assets, Archives):
- A "Modified" column in a smaller font (11px, muted).
- Relative wording, so the column stays narrow. The tooltip on the cell shows the full date and time.

  | When it was modified | Shows |
  |---|---|
  | Today | the time, e.g. `14:32` |
  | Yesterday | `Yesterday` |
  | 2–7 days ago | `Last week` |
  | 8–31 days ago | `Last month` |
  | 32–365 days ago | `Last year` |
  | Longer ago | the year, e.g. `2023` |

- **Folders** show the newest date of the files inside them, using the same wording.
- **Archives:** pk3 entries use the date stored in the zip. PAK files don't store dates, so their entries show nothing.

**Preview pane:** a new row under the size line with the full date: `Modified 16 Oct 2026 14:32`.

## 2. Help mode text in bold

- When help mode is on (the **?** button), the help bubbles use semi-bold text.
- The normal hover tooltips stay regular weight.

## 3. Origin label in the Active list (bug)

**Cause:** the label is worked out by matching file content. Files that were already in the game before the app existed were copied to `16 Oct 2026` by the first scan, so the content match finds that copy. The label then claims the file came from that folder.

**Fix:**
- The app keeps a list of the files it put into the game. It's written by "Use this file" and "Use these", and records for each file:
  - its name
  - its content hash
  - the Assets folder it came from
- The list is stored in `assets/.qam/placed.json`. That keeps it with the Assets folder, and the "no files directly in `assets/<type>/`" rule isn't broken.

## 4. Colours for row labels

Each label keeps its own colour. The colours work in the light and dark themes.

| Label | Where | Colour |
|---|---|---|
| `in game` | Assets: this exact file is the one in the game | green |
| `from <folder>` | Active: the app placed it from that Assets folder | blue (accent) |
| `original` | Active: was in the game before the app; tooltip says where its backup is | neutral grey |
| `changed outside the app` | Active: the app placed it, then the file changed | amber |
| `invalid` (and the file name) | anywhere | red |
| `not loaded` (subfolders in Active) | Active | red |

## 5. Thumbnails

**Why they were missing:** the mockup used coloured squares. v1 only drew a generic icon, because decoding hundreds of images up front would slow the lists down.

**Plan:**
- Each row shows a 32×32 thumbnail: in the Active list, the Assets tree and the Archives list.
- Thumbnails are decoded in the background only for rows on screen. This covers PNG, JPG, TGA, PCX and Quake palette files.
- Invalid files get a thumbnail too, when they're images. Files that aren't images keep a file-type icon.

**Cache: memory only, no files on disk.**
- Thumbnails live in memory while the app runs, keyed by path and modified time.
- The cache is capped at about 2,000 thumbnails, roughly 8 MB. The oldest are dropped first.
- **Why no disk cache:** the textures are small (a few KB to a few hundred KB), so decoding one takes about a millisecond. Writing cache files would cost about as much as it saves, and would leave files to clean up when textures change.
- If it ever turns out slow (folders with thousands of large TGAs), a single cache file per Assets folder can be added later. It wouldn't be one file per image.

## 6. Actions in the preview pane

The preview pane gets buttons for the selected item, matching the right-click menu:

| Selected item | Buttons |
|---|---|
| Active, invalid file or folder | Move to assets…, Delete |
| Active, valid file | Open folder |
| Assets file (valid) | Use this file, Rename…, Delete |
| Assets file (invalid) | Rename…, Move to folder…, Delete |
| Assets pack folder | Use these, Rename…, Delete |
| Archives entry (qw only) | Extract to assets |
| Online item | Download to assets, Open page in browser |

## 7. Rename for every Assets file

- "Rename…" is added for valid files too. Today it's only offered for invalid files.
- Renaming a file to an invalid name shows a warning first, since that file will no longer be usable.

## 8. Drag and drop

You can drop files on any tab. While you drag, an overlay says "Drop to add to Assets".

**Archives** (`.zip`, `.rar`, `.7z`, `.tar`, `.tar.gz`, `.tgz`):
- They're extracted to a temporary folder, then each file is sorted into an asset type (see the rules below).
- Files go to `assets/<type>/<archive name without extension>/`. If that folder already exists, the name gets a suffix.
- Reading `.zip` and `.tar.gz` is built into .NET. `.rar` and `.7z` need the SharpCompress library (MIT licence).
- Archives inside the dropped archive (`.pk3` and `.zip`) are opened too, one level deep. Downloads from the website often contain a `.pk3`.

**Folders:** files go to `assets/<type>/<folder name>/`, keeping the folder's structure inside the asset type.

**Individual files:** files go to a dated folder, `assets/<type>/16 Oct 2026`, reused for the whole day.

**How each file is sorted, first match wins:**
1. **Path:** the file's path contains `textures/models/`, `textures/bmodels/` or `textures/wad/`. This works for `qw/textures/...` and pk3 layouts.
2. **Name:** the file name is valid for exactly one asset type. For example, `armor_0.png` is a model texture, `med100.png` is a bmodel texture and `ibar.png` is a HUD picture.
3. **Folder hint:** if a name is valid for more than one type, a parent folder named `models`, `bmodels`, `wad`, `hud` or `gfx` decides it.
4. **Recognised but not managed:** files that belong to another `qw` folder are listed by that type, so you know what was in the archive. Examples: `progs/*.mdl`, `skins/*.pcx`, `sound/**/*.wav`, `env/*` skyboxes, `textures/charsets/*`. The rules come from `docs/qw-folder-names.md`.
5. **Ignored:** unsupported extensions, names no folder accepts, readme files.

As in every other import, files whose content is already in Assets are skipped.

**Staged import with Revert:**
- Files are first copied to `assets/.qam/staging/<import id>/`. Nothing appears in the Assets folders until you confirm.
- The folder watcher ignores `assets/.qam`.

**Summary dialog:**
```
Added from pak_dm_[models].zip
  Models     42 files → assets\models\pak_dm_[models]
  BModels     0
  HUD         0
  Already in Assets       3 (skipped)
  Not managed by the app  8  (progs: 6, sound: 2)
  Ignored                 3  [Show list ▾]   readme.txt, …
                                         [Revert]   [OK]
```
- **OK** (or Enter) moves the staged files into their Assets folders and closes the dialog.
- **Revert** throws the staged files away. Nothing is added to Assets. It has no key binding, on purpose.
- **Esc does nothing** in this dialog. You have to choose OK or Revert.
- The window has no close (X) button, for the same reason.
- If the app quits while the dialog is open, the next start finds the leftover staging folder and asks to keep it or throw it away.
- The same dialog is used for dropped folders, dropped files, Online downloads, and when every file was ignored.

## 9. Online browsing (gfx.quakeworld.nu): a tab next to Assets

### Where it sits

- **No top-level "Online" tab.** Each asset type (Models, BModels, HUD) gets two tabs over its middle column: **Assets** and **Online**.
- The Online tab is already filtered to that asset type:

  | App tab | Site lists shown (merged, newest first) |
  |---|---|
  | Models | `/browse/models/` (all subcategories) and, hardcoded, `/browse/textures/armor/`, `/browse/textures/backpack/`, `/browse/textures/weapon/`, `/browse/textures/sets/` |
  | HUD | `/browse/hud/` (all subcategories: face and armor, icons, numbers, sets, wads, weapon) |
  | BModels | *No list chosen yet; to be decided later.* The site has no bmodels category; "simple bmodels" sits under Models. Until URLs are added to `online-sources.json`, the tab says "No online source for BModels yet". |

- **Layout:**
  - The middle column becomes a grid of thumbnails with names, with page buttons and a subcategory filter.
  - The preview pane on the right shows the selected item: big screenshot, description, date, download count and size.
  - Buttons: **Download to assets** and **Open page in browser**.
- **Download to assets** saves the zip to a temporary folder and sends it through the drag-and-drop import (section 8): same sorting, same summary dialog, same Revert. The folder name is the zip's own name.
- Items you've already downloaded show a ✓. Their ids are stored in `assets/.qam/online.json`.

### Built as a replaceable module

- **Separate project:** `QuakeAssetManager.Online`, with an interface the app talks to:
  ```csharp
  public interface IOnlineCatalog
  {
      string Name { get; }                                   // "gfx.quakeworld.nu"
      Task<CatalogPage> BrowseAsync(AssetKind type, int page, string? subcategory, CancellationToken ct);
      Task<CatalogItemDetails> GetDetailsAsync(string itemId, CancellationToken ct);
      Task<DownloadedFile> DownloadAsync(string itemId, string targetFolder, IProgress<long>? progress, CancellationToken ct);
  }
  ```
- **Reading the site:** `GfxQuakeWorldCatalog` implements it. All the HTML reading sits in one class (`GfxPageParser`), with tests that run against saved copies of the site's pages. If the site changes, only that class and its saved test pages change.
- **URLs in a settings file:** the list URLs for each asset type live in `online-sources.json`, next to the app. Adding a site list later is a file edit, not a code change. The four `/browse/textures/...` URLs above are the starting contents.
- **Another site later:** that would be a second `IOnlineCatalog` class. The UI doesn't change.

### When the site can't be reached

- Opening the Online tab with no connection, a timeout, or a page the parser doesn't recognise shows a message in the tab instead of the grid:
  > **gfx.quakeworld.nu can't be reached right now.** Check your internet connection and try again. [Retry] [Open site in browser]
- **Unrecognised page:** if the page loads but has no items the parser recognises, the message says the site may have changed and the app needs an update.
- A failed download shows the same kind of message in a dialog. Nothing is added to Assets.

### Being polite to the site

- Pages and thumbnails are only fetched when you open them.
- One request at a time, with a 15-second timeout.
- Requests send a User-Agent that names the app.
- Online list pages and thumbnails are cached for a day in one folder, `%LocalAppData%\QuakeAssetManager\online-cache` (`~/.cache/QuakeAssetManager` on Linux). That's separate from the local thumbnails in section 5.

## 10. File names the game expects in each `qw` folder

See `docs/qw-folder-names.md`, researched from the id Software Quake source and the ezQuake source. It's used in two places:
- **Sorting dropped files (section 8):** files that belong to folders the app doesn't manage yet are named as such, not just "ignored".
- **Groundwork for future asset types:** crosshairs, skyboxes, charsets, skins, … Each new type needs its folder, its name rule and its preview.

**Key findings** (details and source references in the note):
- **Kinds of name rule:**
  - **Fixed lists:** HUD/wad pictures, menu pictures, and the stock models and sounds.
  - **Built from files on disk:** levelshots, lits, per-map texture folders.
  - **Open-ended, chosen by a setting in the game:** crosshairs, skyboxes, charsets and skins. Only the extension can be checked; for skyboxes, also that all 6 faces are there.
- **`gfx/`** is more than console backgrounds. It also holds menu pictures, HUD group pictures, the cursor, and a fallback for every HUD picture.
- **Skyboxes:** they load from `env/` and `gfx/env/`, but the sky's `_wind.cfg` is only read from `gfx/env/`. So `qw/env/doom2016_vortex_wind.cfg` is never used (checked in `r_brushmodel_sky.c:308`).
- **Folders the client reads that weren't in your list:** `locs/`, `maps/lits/`, `gfx/env/`, and the `textures/` subfolders `sprites`, `particles`, `scrollbars`, `tracker` and `scoreboard`.
- **Never loaded, so the app could flag them:**
  - `_luma` files for water, lava and slime textures (`#…`)
  - several `gfx/*.jpg` and `*.tga` files in `nquake.pk3`
  - `maps/*.txt`
  - a second file with the same name but a later extension. The game takes the first it finds in the order tga, png, jpg, pcx, so a `.png` next to a `.tga` of the same name is ignored.

## Order of work

1. Origin label fix, label colours, modified date and the HUD rename (small; they fix wrong or confusing information)
2. Thumbnails
3. Preview-pane actions and Rename
4. Bold help text
5. Import pipeline (sorting, staging, Revert, summary dialog) and drag and drop
6. Online module and the Online tab, which reuse the import pipeline from step 5

## Decisions (16 Oct 2026)

1. Modified-date wording: as in the table in section 1.
2. Dropped single files: a dated folder.
3. Esc in the drop summary dialog: does nothing.
4. Online for BModels: not decided. It can be added later in `online-sources.json`.
