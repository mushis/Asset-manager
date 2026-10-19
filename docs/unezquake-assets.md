# unezQuake: asset files loaded from disk (vs ezQuake)

**Sources**
- unezQuake: `dusty-qw/unezquake`, shallow clone at commit `0deac22`. Paths are `src/*`.
- ezQuake: `QW-Group/ezquake-source` at commit `a6069a8` (the baseline in `qw-folder-names.md`).
- Method: diffed the source file lists, diffed every path-like string literal in `src/` (excluding the bundled `imgui/` and `discord/` libraries), and diffed every `R_Load*Image`, `Draw_CachePic*`, `FS_OpenVFS`, `FS_Load*File`, `S_PrecacheSound` and `Mod_ForName` call.

**Bottom line:** unezQuake keeps every ezQuake asset path unchanged and adds very little. Its new on-disk asset types are **spray decals** (`sprays/`) and **chat emoticons** (`textures/emoticons/`). It also adds 8 hard-coded predicted sounds and some new consumers of existing levelshots and maps. It has **no new TrueType/OpenType support**. The FreeType code (`fonts.c`) is ezQuake's and is identical in both clients.

All rules from `qw-folder-names.md` still apply: the search order is `id1` < `ezquake` < `qw`, pk3/pak files count, image lookup is tga > png > jpg > pcx (+ `.link`), and the extension is stripped.

---

## Candidate asset types

| Folder | What | Name rule | Extensions | New in unezQuake? | Source |
|---|---|---|---|---|---|
| `sprays/` (the folder is the cvar `cl_spray_image_path`, default `sprays`, relative to the gamedir; a leading `qw/` is dropped) | Spray decal images (`+spray`) | **Any name**, chosen by cvar `cl_spray_image` (default `spray`, so `sprays/spray.png`). Every image in the folder is also preloaded and cached at startup. Other players' sprays arrive over the network and are not files. | Preload enumerates `png jpg jpeg tga pcx`. The load itself uses `R_LoadImagePixels` (tga > png > jpg > pcx, `.link`). Max 128x128; larger images are downscaled. | **NEW** | `cl_sprays.c:33-34` (cvars), `:602-623` (dir normalise), `:627-648` (path = `<gamedir>/<dir>/<name>`), `:423` (load), `:785-806` (preload enumerate) |
| `textures/emoticons/` | Chat emoticons (`cl_emoticons 1`, default 0) | `emoticons.json` (fixed name) lists `{"code","file","aliases"}`. Each image is `textures/emoticons/<file>` (**any name listed in the json**). | json: `.json`. Images: tga > png > jpg > pcx (+ `.link`), extension stripped. | **NEW** | `cl_emoticons.c:122-163` |
| `textures/scoreboard/` | Scoreboard login flags. In unezQuake they are **also reused as `:xx:` chat emoticons** | `textures/scoreboard/<scr_scoreboard_login_flagfile>.json` (default `flags`) plus the image files named in it (atlas with x/y/w/h) | `.json` + images | Same in ezQuake. Reuse as emoticons is new. | `sbar.c:98, 2844-2884`; `cl_emoticons.c:189-207` |
| `textures/levelshots/` | Map preview pictures, now also used by the **new menu** (Quick Play / server browser) and the **map-vote HUD** | `<mapname>` | tga > png > jpg > pcx (+ `.link`) | The folder exists in ezQuake. Two new consumers. | `menu_ui_bridge.c:246`; `r_draw.c:1596` (`hud_mapvote`) |
| `maps/` | Animated main-menu background map | `maps/<name>.bsp` for each name in cvar `menu_background_map` (default `aerowalk ztndm3 dm4 dm6 dm2 dm3 schloss bravado`, the first existing one is picked at random) | `.bsp` (plus its usual textures/lits) | **NEW** consumer, existing folder | `menu_scene.c:45, 117-140` |
| `sound/` | Predicted movement sounds (EZCSQC) | Fixed: `player/land.wav player/land2.wav player/h2ojump.wav player/inh2o.wav player/inlava.wav player/slimbrn2.wav misc/outwater.wav weapons/chain1.wav` | exact name (`.wav`) | **NEW** hard-coded names. All exist in stock pak0, except `chain1.wav`, which nQuake ships. | `cl_pred.c:225-243` |
| `sound/` | Spray sounds | Cvars `cl_spray_sound` (default `builtin/spray.flac`) and `cl_spray_reject_sound` (default `builtin/reject.flac`) → `sound/<value>`. **Any name** | exact name | **NEW**. The defaults are compiled into the exe (`builtin_sounds.c:4633-4648`) and checked **before** the disk (`snd_mem.c:36`), so `sound/builtin/*.flac` on disk is never used. Only a custom cvar value reads a file. | `cl_sprays.c:38-39, 1437-1449` |
| *(OS fonts dir, not gamedir)* | Proportional HUD font | `font_facepath` / `fontload <name>` → `<Windows Fonts dir>/<name>.ttf` (`fontlist` lists `*.ttf`) | `.ttf` (`.otf` is only loaded if typed explicitly) | **Same in ezQuake** (missing from `qw-folder-names.md`). Identical code. | `fonts.c:63, 246-251, 470`; `sys_win.c:1678` (`CSIDL_FONTS`) |

### Fixed names
- **Emoticons shipped** (`misc/emoticons/emoticons.pk3`, 62 PNG at 64x64 plus `emoticons.json`; built by `misc/emoticons/build_pack.py` from Noto Emoji): `smile grin wink tongue sad cry heart surprised cool kiss neutral angry happy laugh meh thumbsup thumbsdown clap fire skull rocket bomb trophy hundred eyes thinking rofl joy sob sweat rage poop ghost party crown zap snail wave muscle pray ok victory facepalm shrug sleeping clown nerd devil angel hearteyes confused scream wow unamused pensive hug gun boom target medal swords`. The text triggers (`:)`, `:D`, `<3`, `^^`, ...) live in the json `aliases`. A user can add an emoticon with a loose `textures/emoticons/emoticons.json` plus PNGs, but a loose json **replaces** the pk3's json (one file, first found wins). It does not merge.
- **New predicted sounds**: see the table row above.

### Checked and found nothing new
- **HUD elements**: `hud_inlay.c` (teaminlay), `hud_total_strength.c`, `hud_mapvote.c`, `hud_[elem]_border*` and `r_tracker_iconoverlay` load no new images. The tracker still uses `textures/tracker/<fragfile image>` (`vx_tracker.c:1503`).
- **Crosshairs**: `crosshairvec*` is procedurally drawn (`R_LoadTexturePixels` from a buffer, `r_draw.c`), so it needs no file. `crosshairimage` / `crosshairs/` are unchanged.
- **Menu**: the new ImGui menu uses ImGui's built-in font and draws no gfx files. `menu_classic 1` uses the old `gfx/` menu pics as before.
- **Models/skins**: `cl_model_height` and `r_teammateskincolor*` are colour/offset cvars only. Model and skin lookup is unchanged. The new item table in `menu_scene.c` only references stock `progs/*.mdl` and `maps/b_*.bsp`.
- **Removed**: nothing. All ezQuake path literals still exist (the only removed literal is the `"%s/ezquake/"` format string).

---

## Already covered by the app
`textures/models`, `textures/bmodels`, `textures/wad`, `textures/tracker`, `textures/particles`, `crosshairs`, `env` + `gfx/env`, `gfx`, `textures/charsets`, `textures/levelshots` (the new menu and map-vote just give it more use), `skins`, `progs`. unezQuake does not change the name rules of any of them.

## Worth adding
1. **`sprays/`** (unezQuake only): any-name images, pick one via `cl_spray_image`. It fits the "crosshairs"-style manager exactly (folder of free-named images + one cvar). Note that the path is built as `<gamedir>/sprays/<name>`, so loose files in `qw/sprays/` are the safe target. Keep images at or under 128x128.
2. **`textures/emoticons/`** (unezQuake only): a json-driven pack. It is manageable as a whole pack (`emoticons.pk3`) or as images plus one json. Per-image replacement by name (e.g. drop in `fire.png`) works with no json edit, because a loose `textures/emoticons/fire.png` overrides the pk3 copy.
3. Optionally **`textures/scoreboard/`** (flag atlas + json, same in ezQuake). It is now also the source of country-flag emoticons.

## Not worth managing (and why)
- **TTF fonts**: they are read from the OS Fonts folder, not from the Quake dir, and the code is unchanged from ezQuake.
- **Spray / predicted sounds**: the defaults are built in or are stock sounds. `sound/` stays exact-name and mod-driven, as before.
- **Menu background maps**: these are just `maps/*.bsp` named by a cvar. They are map content, not replacement art.
- **Sprays received from other players**: these are network-cached in memory, not files.
- **`misc/sb/*.txt`**, `nick` overrides, session/Discord/ImGui files: these are server lists and config data, not visual assets.
