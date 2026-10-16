# Version 0.4: planned changes

Status: planned, not started. Don't build or create a new version until asked.

## 1. Subfolders found in the game folders: choose which to import

**Today:** the scan copies every subfolder of an active folder into Assets automatically. For example, `qw/textures/models/original` goes to `assets/models/original`.

**New behaviour:**
- Loose files in each active folder are still copied automatically into a dated folder, as now.
- Subfolders inside each active folder are collected per asset type and shown in one dialog. They're usually alternative asset sets.

**The dialog:**
```
Folders with alternative assets were found
  Models  (qw\textures\models)
    [x] original        15 files   → assets\models\original
    [x] simple          13 files   → assets\models\simple
  HUD     (qw\textures\wad)
    [x] original        40 files   → assets\wad\original
  [ ] Don't ask again about the folders I leave unticked

        [Skip]   [Copy and delete]   [Copy]
```
- Each subfolder is listed with its file count and where it will go: `assets\<type>\<same name>`. The name gets a `(2)` suffix if that folder already exists.
- **Copy** copies the ticked folders and leaves the game folder as it is.
- **Copy and delete** copies the ticked folders, then moves the originals in the game folder to the Recycle Bin (trash on Linux). It asks for confirmation first, because it changes the Quake folder.
- **Skip** does nothing this time.
- **Keys:** Enter = Copy, Esc = Skip.
- **Duplicates:** files whose content is already in Assets are skipped, as in every other import, and the summary says how many.
- **When the dialog appears:** after the scan at start-up, but only when there are folders it hasn't asked about yet. Folders you've already imported or told it not to ask about again are remembered per Quake folder in `settings.json`.
- **Asking again later:** Settings gets a "Look for subfolders again" button.
- **Afterwards:** the import summary is shown, with OK and Revert.

## 2. Everything in one folder next to the app

**Why "Open configuration folder" failed:** I started the app from inside the Claude desktop app, which is a packaged Windows app. Windows redirects such apps' `AppData` writes into `C:\Users\…\AppData\Local\Packages\Claude_…\LocalCache\Roaming\QuakeAssetManager`. The app saw the folder, but File Explorer looks in the real `AppData`, where the folder doesn't exist. Started normally (double-click), the app would have used the real `AppData`. Keeping everything next to the exe avoids the problem entirely.

**New layout (portable).** Unzip anywhere, e.g. `C:\QuakeAssets`:
```
C:\QuakeAssets\
  QuakeAssetManager.exe (+ its .dll files)
  lang\                  translations
  config\
    settings.json        Quake folder, Assets folder, client, map, language
    asset-types.json
    app.json
    names\*.json
  cache\online\          website pages and thumbnails (was %LocalAppData%)
```

**Details:**
- **Config location:** the config folder is `config\` next to the exe. If that folder can't be written to (e.g. the app is in `C:\Program Files` or `/usr/bin`), the app falls back to `%AppData%\QuakeAssetManager` (Windows) or `~/.config/QuakeAssetManager` (Linux). Settings > Asset types shows the folder actually used.
- **Migration:** on the first start of 0.4, if `config\` is empty, the app copies the existing settings and config files into it. It takes them from the real `AppData` folder, or from the Claude package folder above if that's where they ended up. Your Quake folder, Assets folder and choices carry over.
- **Assets folder:** it stays wherever you chose it (now `C:\Games\nQuake\assets`). It isn't moved.
- **Release builds:** a self-contained folder that needs no .NET install:
  - Windows: `QuakeAssets-win-x64.zip`
  - Linux: `QuakeAssets-linux-x64.tar.gz`
- **Open configuration folder:** it opens `config\` with Explorer (`explorer.exe "<path>"`) or `xdg-open`, and shows an error message if that fails.

## 3. App icon

- **Source:** https://gfx.quakeworld.nu/files/650.jpg, as you asked. I'll download it when building 0.4.
- **Conversion:** crop it to a square, then make a Windows `.ico` with 16, 24, 32, 48, 64, 128 and 256 px sizes. Also a 256 px `.png` for Linux.
- **Where it's used:**
  - the exe icon (`ApplicationIcon` in the project)
  - the window and dialog title bars (`Window.Icon`)
  - the Linux release, which gets a `.desktop` file
- **Credit:** the About dialog credits the image source.

## 4. Recognising every file in nquake.pk3 and models.pk3

### Decisions (16 Oct 2026)
- **`qw/gfx`:** the type is renamed **Gfx** (id `gfx`, folder `assets/gfx`). It covers the console background, menu pictures, cursor and other 2D graphics. Existing `asset-types.json` and Assets folders are migrated from `conbacks`.
- **Sounds:** ignored. They're still listed as "not managed: sound".
- **Hidden types:** files of hidden types can still be copied.
  - The Archives right-click menu, drag and drop, and Online downloads all offer them.
  - They go to `assets/<type>/<archive name>`. The summary marks them "(hidden)".
  - After OK the app only switches tab if the type is shown. Otherwise the status bar says where the files went.
- **Scrollbars:** no type. Reported as "not managed: textures/scrollbars".

### Rule changes
- **Progs:**
  - Also accepts the images next to models: `.png`, `.tga`, `.jpg` and `.pcx` named after a model, e.g. `b_g_key.png` next to `b_g_key.md3`, or `end1.tga` next to `end1.mdl`.
  - Also accepts `.skin` files (`<model>_<n>.skin`), which md3 models use.
  - Model names come from the game data and from the `.mdl`/`.md3` files in the same archive or drop. So `v_spike.png` is accepted because `v_spike.md3` is beside it, even if no game folder has that model.
- **Levelshots:**
  - Accepts any name, not only maps that are installed. `aerowalk`, `ztndm3` and `povdmm4` aren't in your game data.
  - Recognised only by its folder (see section 10).
- **Gfx:** any image or `.lmp` name. The client's fixed menu names, `conback` and the cursor are listed in `names/gfx.json` for suggestions.
- **Config files:** `configs/*.cfg` are ignored without being counted as unrecognised. The summary shows them as "config files (not managed)".
- **Hidden-type message:** the Archives menu no longer says "No known asset type" for files of a hidden type. It offers "Copy to Assets → assets\progs\nquake (Progs, hidden)".

### Expected result

| Archive | Folder | Files | Goes to |
|---|---|---|---|
| nquake.pk3 | `gfx/` | 43 | Gfx (hidden) |
| | `lits/` | 10 | not managed (Lits type removed, see section 9) |
| | `progs/` | 28 (16 .mdl, 6 .md3, 6 .png) | Progs (hidden) |
| | `textures/levelshots/` | 7 | Levelshots (hidden) |
| | `textures/tracker/` | 21 | Tracker |
| | `textures/scrollbars/` | 4 | not managed |
| | `sound/…` | 13 | not managed |
| | `configs/` | 4 | ignored (config files) |
| models.pk3 | `progs/` | 42 (32 .mdl, 3 .md3, 4 .tga, 3 .png) | Progs (hidden) |
| | `textures/models/` | 18 | Models |

These two tables become tests that run against the real archives, read-only, and are skipped when the files aren't there.

## 5. Small fixes noticed while testing 0.3

- **Plural forms:** counts in the hints use the right form, e.g. "1 invalid file hidden", "1 skipped". Today the plural is used for 1, in every language.
- **Release builds:** they don't include the debug-only snapshot code. It's already compiled out with `#if DEBUG`; this is just a check.

## 6. Delete in the Active list

- Every file and folder in the Active list gets **Delete**, in both the right-click menu and the preview buttons. Today only invalid ones have it.
- **Confirmation:** "Move ibar.png to the Recycle Bin? The game will use its built-in picture." If a copy of that exact file is in Assets, the message says which folder it's in. If not, it warns that it's the only copy.
- **Also delete the copy in Assets:** the confirmation has a tickbox, "Also delete the copies in Assets (assets\wad\16 Oct 2026, assets\wad\simple)".
  - It lists every Assets file with the same content.
  - It's unticked by default.
  - When ticked, those copies go to the Recycle Bin too.
  - If there's no copy in Assets, the tickbox isn't shown.
- **Invalid files:** they keep **Move to assets…** as well.

## 7. No origin labels in the Active list

- **Why:** the labels "original", "from <folder>" and "changed outside the app" depend on the list of placed files in `assets/.qam/placed.json`. You saw them come out wrong after reopening the app. They're removed.
- **Active list:** each row shows only the thumbnail, the name, `invalid` (red) when the game never loads the name, and the modified date.
- **Assets list:** keeps `in game` (green) on the file whose content matches the file the game uses now. This is worked out from the files themselves each time, so it can't go stale.
- **Clean-up:** `placed.json` is no longer written or read. An existing one is deleted on first start.

## 8. Asset type tabs that don't fit

- The header wraps onto a second row (or more) when the shown types don't fit the window width. The right-hand buttons (Test, Settings, Help, About) stay on the first row.
- Resizing the window re-flows the tabs.
- The selected tab is easy to spot on either row (same orange highlight).

## 9. Remove the Maps, Lits and Locs types

- They're removed from the built-in types and from `asset-types.json`.
- **Migration:** on first start, the entries `maps`, `lits` and `locs` and their `names/*.json` files are deleted. Any `assets/maps`, `assets/lits` and `assets/locs` folders are left untouched.
- Files in `maps/`, `lits/`, `maps/lits/` and `locs/` are reported as "not managed".
- **Progs fixes found while checking this:**
  - Progs no longer accepts `.bsp`.
  - Its computed names no longer include the item boxes. Today `maps/b_bh100.bsp` would wrongly go to Progs; item boxes belong in `maps/`.

## 10. Sending each file to the right folder

I ran the rules against your game data and every file in your archives. These came out wrong:

| File | Would go to | Should go to | Cause |
|---|---|---|---|
| `id1/pak0.pak: maps/b_*.bsp` (13 files) | Progs | not managed (maps) | Progs accepted `.bsp` |
| `nquake.pk3: textures/scrollbars/arrow_up.jpg` | Skyboxes | not managed | skybox pattern `…up` matched by name |
| `textures.pk3: textures/#teleport.png` | Skyboxes | not managed (map texture) | skybox pattern `…rt` matched by name |
| `ezquake.pk3: textures/conpart.png` | Skyboxes | not managed | same |
| `ezquake.pk3: radars/dm2.png` (4 files) | Levelshots | not managed (radars) | map names matched by name |
| `end2` | Levelshots and Progs | depends on its folder | the name is both a map and a model |

### New rules, in order

1. **A known folder in the path decides.**
   - **Type folders:** `textures/models/`, `textures/wad/`, `gfx/`, `env/`, `crosshairs/`, `skins/`… The file goes to that type, shown or hidden (hidden types are offered too).
   - **Known folders the app doesn't manage:** `textures/` (map textures), `textures/scrollbars/`, `maps/`, `lits/`, `locs/`, `sound/`, `radars/`, `configs/`… The file is reported as not managed.
   - **Wrong name:** a file inside a type's folder whose name the game never loads is **copied, marked invalid** (red in Assets). These are usually renamed alternatives, such as `textures/particles/bps.png`; renaming one makes it usable. This replaces the earlier "skip" decision.
     - System junk is still left out and listed: `Thumbs.db`, `desktop.ini`, `.DS_Store`.
2. **Otherwise the name decides**, but only for types with fixed or computed name lists: Models, BModels, HUD, Tracker, Particles, and Progs by its model extensions. The name has to fit exactly one of them.
3. **Otherwise you pick.** An image whose type can't be told from its name is listed in the summary with a type dropdown. That covers any-name types such as Crosshairs, Skyboxes, Charsets, Skins and Gfx, and it means a dropped `mycrosshair.png` gets a choice. The options are the shown and hidden image types plus **Skip**; the default is Skip. Nothing is guessed.
4. **Anything else** is listed as unsupported.

Patterns (like the skybox `…rt/bk/lf/ft/up/dn` rule, or "any name") now only check names of files that are already in that type's folder. They never decide which folder a file goes to.

### Your decisions (16 Oct 2026)

| Case | Example | Decision |
|---|---|---|
| HUD picture names in `gfx/` | `gfx/ibar.png`, `gfx/sb_quad.tga` | **Gfx**: the folder they came in wins |
| Item-box textures in the `textures/` root | `textures/med100.jpg` | **Not managed**: `textures/` is map textures |
| Image with no type folder and an any-name type | `mycrosshair.png`, `mysky_rt.tga` | **You pick** the type in the summary (default Skip) |
| Wrong name inside a type folder | `textures/wad/ibar_old.png`, `textures/particles/bps.png` | **Copied, marked invalid** (changed after the scan of C:\Games\outros) |
| Mod models in `progs/` | `beartrap.mdl`, `g_airgun.mdl` | **Any model name** is accepted (`.mdl`, `.md3`, `.spr`) |
| gfx.wad pictures ezQuake doesn't load | `inv_rocket`, `anum_colon`, `face_invul1` | **Accepted as HUD names**: all lump names in id1's `gfx.wad` |

### Checking it

- Tests run every file of `id1/pak0.pak`, `pak1.pak`, `qw/*.pk3` and `ezquake/ezquake.pk3` through the rules, read-only, and compare the counts per type with an expected table.
- Any file whose result changes fails the test, so a future rule change can't silently send files to the wrong folder.
- The cases in the two tables above are tests of their own.
- **Open cases:** if building 0.4 turns up any other file where the right folder isn't certain, I'll stop and ask you about it before going on, one case at a time.

## 11. Copying from PAK/PK3 files: the archive's folder decides

**The rule:**
- A file inside a `.pak` or `.pk3` goes to the type of the folder it sits in inside the archive. The name is never used to move it somewhere else. There's a reason the file is in that folder.
- **Examples:**
  - `textures/wad/ibar.png` → HUD
  - `gfx/ibar.png` → Gfx
  - `progs/b_g_key.png` → Progs
- **Known folders the app doesn't manage:** files there stay out, and the summary lists them. These folders are `textures/` (map textures), `textures/scrollbars`, `sound`, `maps`, `lits`, `locs`, `radars` and `configs`.

**Unsure: the app asks.** When an archive file's folder isn't a known type folder or a known unmanaged folder (e.g. `hud/widgets/`, files at the archive root, a folder the app has never seen), a dialog asks before anything is staged:
```
Where should these files go?
  hud/widgets/  (4 files: StandardBox.tga, StandardBox.Skull.tga, …)   [ Skip            ▾]
  (archive root) (2 files: armor_0.png, armor_1.png)                   [ Models          ▾]  suggested from the file names
                                                       [Cancel]  [Continue]
```
- **One row per folder** in the archive, not per file, so a big archive is still quick to answer.
- **Dropdown:** every asset type (shown and hidden), listed by name and folder, e.g. "Crosshairs (qw\crosshairs)", plus **Skip**.
- **Pre-selected type:** when the file names in that row point to exactly one type, the dropdown starts on it. A name points to a type when it's on that type's fixed or computed name list, e.g. `armor_0.png` → Models or `ibar.png` → HUD. The row then shows "(suggested from the file names)".
  - If no type fits, or more than one does (the row's files point to different types, or a name fits several), it starts on **Skip**.
  - Any-name types (Crosshairs, Skins, Charsets, Gfx, Skyboxes) are never pre-selected, because every name fits them. (Decided 16 Oct 2026.)
- **Keys:** Enter = Continue, Esc = Cancel (nothing is copied).
- **Then** the usual summary follows, with OK and Revert.

**Loose files** (dropped files and folders, not archives) keep the rules in section 10:
1. folder
2. name, for fixed-list types
3. a type picker in the summary

## 12. Results from scanning C:\Games\outros (16 Oct 2026)

I scanned five Quake installs (Rotker-ezQuake, defs_quakeworld_2006_08_29, mush qw 2011, qw.ciscon, qw.mushi): 20,639 files, loose and inside archives, run through the planned rules, read-only.

| Result | Files |
|---|---|
| Assigned to a type | 2,968 (HUD 652, Gfx 506, Progs 383, Skins 351, Skyboxes 300, BModels 297, Crosshairs 254, Models 183, Levelshots 100, Tracker 95, Charsets 39, Particles 8) |
| Known folders not managed (sound, maps, map textures…) | 8,567 |
| Unsupported files (demos, exes, docs…) | 8,500 |
| In a type folder with a name the game doesn't load | 298 |
| Images with no type folder | 106 |

**What it changed:**
- **Levelshots:** 116 levelshots for maps that aren't installed. Already covered: any map name will be accepted.
- **Progs:** 102 mod models. Any model name will now be accepted.
- **HUD:** 27 gfx.wad pictures ezQuake doesn't load. All gfx.wad lump names will now be accepted. `anum_slash`, `face_invis2` and `sb_armor0` aren't real lumps, so they come in as invalid.
- **Crosshairs:** `crosshairs/crosshair.txt` is a file ezQuake reads (the 8×8 X/O crosshair). Crosshairs will accept it as a valid name.
- **Models:** `player_1`–`player_13.png` in `textures/models` aren't loaded, because `player.mdl` has 1 skin. They're alternatives, so they're copied marked invalid.
- **`hud/widgets/*.tga`:** ezQuake doesn't load them. In an archive the folder is asked about (section 11). In a Quake folder scan, `hud/` isn't a type folder, so it isn't scanned.
- **Screenshots and manual images** (`ezquake000.jpg`, `manual/…/pics`, 71 files): a Quake folder scan only reads type folders, so they're never offered. If they're dropped, they're "images with no type folder", and you pick a type or Skip.
- **Junk in type folders** (`Thumbs.db`, `skins.zip`, `face.zip`, `note.txt`): `Thumbs.db` is left out. Archives found inside a type folder during a scan are listed, not opened, as are text files.

## Order of work

1. Portable config folder, migration, and the Open-folder fix
2. Remove Maps, Lits and Locs, and fix Progs (sections 9 and 4)
3. New recognition rules with the archive tests (sections 10–12), the "where should these go?" dialog for archives, the type picker for loose files, and the Gfx rename
4. Active list: Delete (with the "also delete in Assets" option) and no origin labels (sections 6 and 7)
5. Header tabs wrap onto more rows (section 8)
6. Subfolder dialog at scan time (section 1)
7. Icon
8. Plural fixes, then release packages for Windows and Linux
