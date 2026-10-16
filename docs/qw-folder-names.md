# Which file names the QuakeWorld client loads from each `qw/` folder

This is a research note for the asset manager's valid/invalid classification.

**Sources**
- **id**: id Software `quake` repo, `QW/client/*` (the original QuakeWorld client). Paths below are relative to that repo.
- **ezQuake**: `QW-Group/ezquake-source` at commit `a6069a8`. Paths are `src/*.c`. nQuake ships ezQuake, so ezQuake is the client that matters.
- **Data**: `id1/pak0.pak`, `id1/pak1.pak`, `qw/*.pk3` and `ezquake/ezquake.pk3` from `C:\Games\nQuake`.

All line numbers are for those exact revisions.

---

## 0. Rules that apply to every folder

### 0.1 Search path (ezQuake)
`fs.c:774-787` adds the game directories in this order: `id1`, then `ezquake`, then `qw`. Each later directory takes priority. Inside a directory, `.pak` and `.pk3` files are searched along with the loose files. This means a file in `qw/` overrides the one with the same name in `id1/pak0.pak` or `ezquake/ezquake.pk3`. The folder names below are relative to any of these roots.

### 0.2 Image extension resolution (ezQuake) - `R_LoadImagePixels`, `r_texture_load.c:268-366`
Almost every 24-bit image lookup in ezQuake goes through this function:
1. The requested name has its extension stripped (`COM_StripExtension`). Every `*` in the name becomes `#` (`r_texture_load.c:274-279`). This is why water/lava/slime textures are stored as `#water1.png`.
2. **`.link` redirect** (`r_texture_load.c:281-319`). If `<base>.link` exists, its first line is read as a path **relative to `textures/`** and loaded instead. Only the `tga`, `png`, `jpg` and `pcx` link targets are honoured.
3. Otherwise the formats are tried in this order: **`tga`, `png`, `jpg`, `pcx`** (`r_texture_load.c:321-331`). The first one that exists wins. A later format replaces an earlier one only when the earlier one came from a copy-protected pak and the later one did not (`r_texture_load.c:341-350`). `pcx` is skipped when the caller passes `TEX_NO_PCX`, which player skins do.
4. Nothing is loaded when `gl_no24bit 1` is set or `-no24bit` is passed (`r_texture_load.c:42`, `r_texture_cvars.c:53`).

So **for any image slot the app should accept `.tga`, `.png`, `.jpg` and `.pcx`, plus `.link`**. Exceptions are noted per folder. When two files differ only in extension, only the first one in that order is used. The others are shadowed, so the app could flag them as "duplicate/unused".

### 0.3 `_luma` suffix (ezQuake)
`_luma` is a fullbright overlay. It is appended to the base name and only exists for world/bmodel textures (`r_brushmodel_textures.c:134-138`) and alias-model skins (`r_aliasmodel_skins.c:113-117`). It is **not** used for crosshairs, gfx, charsets, levelshots, skyboxes or player skins. ezQuake never uses `_glow` or `_gloss`.

### 0.4 Map groups (ezQuake)
`mapgroup` lets several maps share assets. One **system** group is built in: `exmy` = `start e1m1 ... e4m8` (`match_tools.c:1079-1096`). Users can add more groups in a cfg. For lits and locs, only user groups are used, not the system group (`r_brushmodel_load.c:119,124`; `teamplay_locfiles.c:559`). Map textures use every group, the system one included (`r_brushmodel_textures.c:94,104-111`).

### 0.5 Validity classes used in this note
- **(a) Fixed list**: the names are hard-coded in the engine or are the stock id data.
- **(b) Derivable**: the name is built from something the app can see on disk, such as maps in `maps/` or in paks, models in `progs/`, or BSP miptex names.
- **(c) Open-ended**: a cvar or console command picks the name, so any name is reachable. The app can only check the extension, and possibly the set completeness.

---

## Summary table

| Folder | What | Name rule | Extensions | Valid-name check for the app | Source |
|---|---|---|---|---|---|
| `crosshairs/` | Crosshair images | `crosshairs/<crosshairimage>`, plus fixed `crosshairs/crosshair.txt` | tga > png > jpg > pcx (+ .link); `crosshair.txt` | (c) any image name. (a) `crosshair.txt` | ezQuake |
| `env/` | Skyboxes | `env/<r_skyname><suf>` or `env/<r_skyname>_<suf>`, suf ∈ `rt bk lf ft up dn` | tga > png > jpg > pcx (+ .link) | (c) any base name, but the file must end in one of the 6 suffixes. Warn when a set is incomplete. `*_wind.cfg` here is **not** read | ezQuake |
| `gfx/` | Console background **and** menu/HUD/2D pics | `gfx/<scr_conpicture>` (default `conback`); fixed menu pic list; wad-lump fallback `gfx/<lump>`; `gfx/<hud_groupN_picture>`; cursor/editor icons; `gfx/env/` skies | 24-bit: tga > png > jpg > pcx. 8-bit: `.lmp` for fixed names | (a) fixed list + (c) any image (conpicture/hud groups) | id + ezQuake |
| `lits/` | Coloured lighting | `lits/<mapname>.lit` (3rd of 5 locations) | `.lit` only | (b) the basename must be a map that exists, or a user map group | ezQuake |
| `maps/` | Maps and per-map data | `<mapname>.bsp` from the server; `.lit`, `.ent`, `.pts`, `.qpn` side-files; `b_*.md3` replacements; `maps/lits/` | `.bsp`; side-files `.lit .ent .pts .qpn`; `b_*.md3` (no `.loc` here - see `locs/`) | (c) any `.bsp`. (b) side-files need a matching map | id + ezQuake |
| `progs/` | Models and sprites | Exact name from the server's model list (QC), plus client hard-coded names; `.mdl` → `.md3` replacement | `.mdl`, `.spr`, `.bsp`, `.md3`; md3 textures `.tga/.png/.jpg/.pcx`, `.skin` | (a) stock list + (c) any mod model. `.md3` valid when the matching `.mdl` name is known | id + ezQuake |
| `skins/` | Player skins | `skins/<skin>` where skin = userinfo `skin`, `teamskin`/`enemyskin`/..., `baseskin` (default `base`), forced name/id/`t#`/`e#` | 24-bit tga > png > jpg (no pcx), then 8-bit `.pcx` | (c) any name. `base.pcx` is required for the fallback | id + ezQuake |
| `sound/` | Sounds | Exact path from the server's sound list (QC), plus client hard-coded paths, plus cvar-chosen paths | Whatever the requester names (almost always `.wav`). No extension substitution | (a) stock list + (c) any mod sound. **Must be the exact file name** | id + ezQuake |
| `textures/` | Map (world) textures, plus misc effect textures | `textures/<map>/<miptex>`, `textures/<mapgroup>/<miptex>`, `textures/<miptex>`; sky `<miptex>_solid/_alpha`; `_luma`; fixed effect names | tga > png > jpg > pcx (+ .link) | (b) miptex names from the BSPs; subfolder must be a map/group name. (a) fixed effect names | ezQuake |
| `textures/bmodels/` | Ammo/health box textures | miptex of `maps/b_*.bsp` (`*`→`#`), `_luma`, `simple_<bsp>_0` | as above | (b)/(a) (already researched) | ezQuake |
| `textures/charsets/` | Console font | `textures/charsets/<gl_consolefont>`, plus `<name>-<NNN>` / `<name>-cyr` alternates | tga > png > jpg > pcx (+ .link) | (c) any image name. Optional `-001`..`-223` / `-cyr` siblings | ezQuake |
| `textures/levelshots/` | Loading-screen picture | `textures/levelshots/<mapname>` | tga > png > jpg > pcx (+ .link) | (b) basename = a map name | ezQuake |
| `textures/models/` | Alias model skins | `<mdlbase>_<skin>[_<frame>][_luma]`, `simple_<base>_<n>` | as above | (b) (already researched) | ezQuake |
| `textures/wad/` | HUD/status bar pics | `textures/wad/<gfx.wad lump>` | as above | (a) gfx.wad lump list (already researched) | ezQuake |

---

## 1. `crosshairs/`: crosshair images (ezQuake only)

The original id client has no crosshair images. It only has the built-in 8x8 `+` drawn from `cs_data` (`QW/client/gl_draw.c:408`).

ezQuake:
- **Image crosshair.** The `crosshairimage` cvar (`r_draw.c:61`, default empty) loads **`crosshairs/<value>`** through `Draw_CachePicSafe(va("crosshairs/%s", s), false, true)` (`r_draw.c:192-210`). `only24bit = true`, so no `.lmp` is used. The extension is stripped, so `crosshairimage 10` and `crosshairimage 10.png` both resolve to `crosshairs/10.{tga,png,jpg,pcx}`, in that order. Sub-paths work too (`crosshairimage foo/bar`).
- **Text crosshair.** `crosshairs/crosshair.txt` is a fixed name (`r_draw.c:222`). It holds 64 `x`/`o` characters (an 8x8 bitmap) and is used when `crosshair 1` (`r_draw.c:216-251`, `618`).
- No `_luma`.
- **The folder is correct.** It is `qw/crosshairs/` (or `ezquake/crosshairs/`), **not** `textures/crosshairs/`.

**App rule:** (c) accept any `*.tga|png|jpg|pcx` (and `*.link`) under `crosshairs/`. Accept `crosshair.txt`. Anything else is invalid. Note the shadowing: when `10.tga` and `10.png` both exist, `10.png` is never used. nQuake ships `crosshairs/1..20` and sets `crosshairimage "10"` in `nquake_default.cfg`.

---

## 2. `env/`: skyboxes (ezQuake only)

The id QW client has no skyboxes.

ezQuake, `R_LoadSkyTexturePixels` (`r_brushmodel_sky.c:184-208`):
- The base name is the cvar **`r_skyname`** (`r_rmain.c:171`, default empty). It can also be set with the `skybox`/`r_skyname` command, by the worldspawn key `sky` or `skyname` in the BSP (`r_brushmodel_load.c:946-947`), or by a server stuffcmd `r_skyname` (`cl_parse.c:3294`).
- The face suffixes, in load order, are **`rt bk lf ft up dn`** (`r_brushmodel_sky.c:186`).
- Search order **for each face** (`r_brushmodel_sky.c:187`):
  1. `env/<name><suf>`
  2. `gfx/env/<name><suf>`
  3. `env/<name>_<suf>`
  4. `gfx/env/<name>_<suf>`
- The extension comes from the generic resolver (tga > png > jpg > pcx, + `.link`).
- If any face fails, the whole skybox is rejected (`r_brushmodel_sky.c:256-268`).
- No `_luma`.
- **Sky wind config** is read only from **`gfx/env/<name>_wind.cfg`** (`r_brushmodel_sky.c:49,308,363`). A `*_wind.cfg` placed in `env/` (nQuake has `env/doom2016_vortex_wind.cfg`) is **never read**.
- Sky groups (`cl_skygroups.c`) only group map names for sky selection. They do not change file names.

**App rule:** (c) a file is valid when it matches `^(.+?)_?(rt|bk|lf|ft|up|dn)\.(tga|png|jpg|pcx|link)$`. Group the files by base name (with and without the trailing `_`) and warn when a set lacks any of the six faces. `readme.txt` and `*_wind.cfg` in `env/` are not loaded. `gfx/env/` is an equivalent, lower-priority location that the user's list does not mention.

---

## 3. `gfx/`: console background **and** other 2D pictures

The user's description ("console backgrounds") is **incomplete**. `gfx/` holds every 2D menu/HUD picture as well.

### 3.1 Console background
- **id:** fixed `gfx/conback.lmp` (320x200 QPIC) (`QW/client/gl_draw.c:412-414`). Nothing else.
- **ezQuake:**
  - `gfx/conback.lmp` is still **required**. The client exits with an error if it is missing or not 320x200 (`r_draw.c:1031-1037`).
  - A 24-bit picture then replaces it: **`gfx/<scr_conpicture>`** (`r_draw.c:52`, default `"conback"`; loaded at `r_draw.c:1041` and in the on-change handler `r_draw.c:182`). By default this is `gfx/conback.{tga,png,jpg,pcx}`. nQuake ships `gfx/conback.jpg` in `nquake.pk3`. Setting `scr_conpicture mybg` loads `gfx/mybg.*`.
  - The **per-map console background is not in `gfx/`.** It is `textures/levelshots/<map>` (see section 13). `scr_conback 1` (default) shows it only while loading, and `2` shows it always (`r_draw.c:50,1061-1082`).
  - No textures/conback folder exists.

### 3.2 Other fixed `gfx/` names
- `gfx/palette.lmp` and `gfx/colormap.lmp` are required (`cl_main.c:2247-2255`). They are checked by `f_modified` (`fmod.c:623-624`).
- ezQuake's menu/intermission pics use `Draw_CachePicSafe(path, crash, only24bit=false)`. The `.lmp` and a 24-bit sibling with the same base name are both valid, and the 24-bit one wins (`r_draw.c:390-462`). The names (`r_draw.c:465-496`) are: `pause loading box_tl box_ml box_bl box_tm box_mm box_mm2 box_bm box_tr box_mr box_br ttl_main mainmenu menudot1..menudot6 ttl_sgl qplaque sp_menu p_load p_save p_multi ranking complete inter finale`.
- **gfx.wad lump fallback.** Every status-bar/HUD lump is first looked up as `textures/wad/<lump>` and then as **`gfx/<lump>`** (`r_draw.c:357-358`). Those lump names are also valid in `gfx/`: `num_0..9 num_minus num_slash num_colon anum_0..9 anum_minus sbar ibar scorebar sb_* inv_* inv2_* inva1..5_* sba1..5_* face* conchars backtile disc ram net turtle in_1..8 out_1..8`. The full lump list was read from `gfx.wad` in pak0. For example, nQuake's `gfx/backtile.jpg` comes through this path.
- `gfx/mcharset` (`draw.h:73` `"gfx/mcharset.png"`, extension stripped; `r_draw_charset.c:805`): the menu charset.
- `gfx/conchars-<locale>.lmp`: an 8-bit fallback for alternate charsets (`r_draw_charset.c:71,154`). For example, ezquake.pk3 has `gfx/conchars-cyr.lmp`.
- `gfx/cursor` (`cl_screen.c:1082`; `.lmp` first, then 24-bit, `cl_screen.c:1041-1053`), and `gfx/hud_move_icon`, `gfx/hud_resize_icon`, `gfx/hud_align_icon`, `gfx/hud_place_icon` (`hud_editor.c:2806-2809`).
- `gfx/ui/*` for the EZ controls (`ez_window.c:94` `gfx/ui/ez_titlebar`; ezquake.pk3 also has `ez_button_*`, `ez_window`).
- `gfx/env/*` for skyboxes (section 2).

### 3.3 Open-ended `gfx/` names
- `hud_group1_picture` .. `hud_group9_picture` load `gfx/<value>` (`hud_groups.c:34,130-145`). nQuake sets `hud_group3_picture "weaplist_full"`, so `gfx/weaplist*.tga` are valid.
- The old HUD `hud_262` `img` element loads an arbitrary path (`hud_262.c:173-178`).
- `scr_conpicture` (above).

### 3.4 Not loaded by ezQuake
Stock pak0 `gfx/*.lmp` that ezQuake's QW menus never load: `ttl_cstm p_option vidmodes menuplyr bigbox mp_menu help0..5 netmen1..5 sell dim_* pop` (none of these names appear in `src/`). They are harmless in a pak, but 24-bit replacements for them (nQuake ships `gfx/ttl_cstm.jpg`, `p_option.jpg`, `vidmodes.jpg`, `mp_menu.tga`, `bigbox.tga`) are dead files unless a HUD group picture points at them.

**App rule:** (a) the fixed list above (menu pics + wad lumps + palette/colormap/conback/cursor/mcharset), in `.lmp` or 24-bit form. Plus (c) any image when the user uses `scr_conpicture`/`hud_groupN_picture`. A practical rule is to accept any `tga/png/jpg/pcx/lmp` and mark the names on the fixed list as "known".

---

## 4. `lits/`: coloured lighting (ezQuake only)

The id QW client has no `.lit` support.

ezQuake, `LoadColoredLighting` (`r_brushmodel_load.c:89-130`):
- It is active when `gl_loadlitfiles` is 1 (default) or 2 (`r_rmain.c:190`). It only runs for the **world model** `maps/<mapname>.bsp` (`r_brushmodel_load.c:103`).
- Search order (first found wins):
  1. `maps/lits/<mapname>.lit`
  2. `maps/<mapname>.lit`
  3. **`lits/<mapname>.lit`**
  4. `maps/<groupname>.lit` (user mapgroup only)
  5. `lits/<groupname>.lit` (user mapgroup only)
- The extension is `.lit` only. No suffixes.

**App rule:** (b) `lits/<name>.lit` is valid when `<name>` is a map that exists (`maps/<name>.bsp` loose or in any pak/pk3) or a user map group. Flag the file as shadowed when `maps/<name>.lit` or `maps/lits/<name>.lit` also exists, because those win. nQuake's `nquake.pk3` has `lits/{aerowalk,dm2,dm3,dm4,dm6,e1m2,e1m7,end,povdmm4,ztndm3}.lit`.

---

## 5. `maps/`: maps and per-map side files

- **`.bsp`**: the server sends the world model name as `maps/<map>.bsp` (id `QW/client/gl_model.c:909`; ezQuake server `sv_init.c:464-476`). The client downloads it with `CL_CheckOrDownloadFile` if it is missing (id `QW/client/cl_parse.c:224`). Any name is valid.
- **Item boxes** `maps/b_*.bsp` are model names from QC: `b_bh10 b_bh25 b_bh100 b_shell0 b_shell1 b_nail0 b_nail1 b_rock0 b_rock1 b_batt0 b_batt1 b_explob b_exbox2` (pak0/pak1).
- **`maps/b_*.md3`** (ezQuake): when a model `maps/b_*.bsp` is requested, `maps/b_*.md3` is tried first (`r_model.c:259-264`).
- **`.lit`**: `maps/<map>.lit` and `maps/lits/<map>.lit` (section 4).
- **`.ent`** (ezQuake built-in server only, `sv_loadentfiles`): `maps/<sv_loadentfiles_dir>/<map>.ent`, then `maps/<map>.ent` (`sv_init.c:576-597`). The client does not read them.
- **`.pts`**: the pointfile `maps/<map>.pts` (`r_part.c:210`).
- **`.qpn`**: external physics normals `maps/<map>.qpn` (`cmodel.c:987`).
- **`.loc`**: **not** read from `maps/`. Locations come from **`locs/<map>.loc`**, then `locs/<group>.loc` (`teamplay_locfiles.c:94-102,558-560`). The `locs/` folder is missing from the user's list, and nQuake has `qw/locs/`.
- **`.txt`** (e.g. `maps/aerowalk.txt`): these are map readme files. No client code reads them.
- id `r_part.c:95` has the `.pts` code commented out.

**App rule:** (c) any `*.bsp` (and `b_*.md3`) is valid. (b) `*.lit`, `*.ent`, `*.pts`, `*.qpn` are valid when a matching map exists (`.ent` can also live in `maps/<dir>/`). `*.txt` is "documentation", which is neither loaded nor harmful. Stock map list (pak0/pak1): `start e1m1..e1m8 e2m1..e2m7 e3m1..e3m7 e4m1..e4m8 end dm1..dm6`.

---

## 6. `progs/`: models

- The names come from the server's **model list** (the QC precache). The client loads `progs/<name>` exactly as given (id `gl_model.c:266`; ezQuake `r_model.c:266-270`), and downloads it if missing (`cl_parse.c:224`).
- **ezQuake `.md3` replacement:** for any `*.mdl` (and `maps/b_*.bsp`), `<same base>.md3` is tried first (`r_model.c:259-264`). nQuake ships `progs/{lavaball,w_g_key,w_s_key,b_g_key,b_s_key,m_g_key,m_s_key,teleport,v_spike}.md3`.
- **MD3 textures:** for each surface the engine tries, in order: (1) the name from a `.skin` file, (2) the shader name inside the md3, (3) `textures/<shader>`, (4) `<model dir>/<shader>` (`r_aliasmodel_md3.c:298-322`). It also reads skin files `<model>_<n>.skin` and `<model>_{default,blue,red,green,yellow}.skin` (`r_aliasmodel_md3.c:76-103`). Image files next to the md3 are therefore valid when the md3 references them (e.g. `progs/teleport.png`).
- **MDL skins** are external in `textures/models/` (already researched), then `textures/<id>` (`r_aliasmodel_skins.c:98-111`).
- **Sprites** `.spr`: external frames are `textures/sprites/sprites/<base>_<n>`, then `textures/sprites/<base>_<n>` (`r_sprites.c:41-48,91`). The identifier contains `sprites/`, so the first path really is doubled.
- **Stock list** (pak0 + pak1):
  - `.mdl`: `armor g_shot g_nail g_nail2 g_rock g_rock2 g_light w_s_key m_s_key w_g_key m_g_key b_s_key b_g_key end1 end2 end3 end4 invulner suit invisibl quaddama player eyes h_player gib1 gib2 gib3 v_axe v_shot v_nail v_rock v_shot2 v_nail2 v_rock2 v_light v_spike bolt bolt2 bolt3 lavaball missile grenade spike s_spike backpack zom_gib flame flame2 laser teleport k_spike w_spike s_light ogre h_ogre demon h_demon shambler h_shams knight h_knight soldier h_guard wizard h_wizard dog h_dog zombie h_zombie boss tarbaby hknight h_hellkn fish shalrath h_shal enforcer h_mega oldone`
  - `.spr`: `s_bubble s_explod s_light`
- **nQuake/KTX extras** (nquake.pk3): `bit flag spawn star v_coil v_star vwplayer w_axe w_coil w_light w_nail w_nail2 w_rock w_rock2 w_shot w_shot2`.
- **ezQuake hard-coded names** (special rendering/flags, `r_model.c:405-470` and others), from grep of `src/`: `amf_spike armabody armalegs basbkey.bsp basrkey.bsp beam biggren caltrop coil detpack detpack2 dgib dgib2 dgib3 disp e_spike1 e_spike2 ff_flag flame0 flame3 flare grem gren1.bsp grenade2 grenade3 harbflag hgren2 hook kkr minimissile newplayer princess s_expl.spr scor stag tesgib1..4 tesla tf_flag tf_stan tgib1..3 turrbase turrgun v_bio v_grap v_knife v_knife2 v_medi v_span` (TF/CTF/mods).
- **`f_modified`** (`fmod.c`) checksums many of these files (`armor backpack bolt2 end1-4 eyes g_* gib1-3 grenade invisibl invulner missile quaddama s_spike spike suit player s_bubble.spr s_explod.spr maps/b_bh100.bsp` ...). Replacing them with custom models is reported by `f_modified`, so the app may want to flag them as "ruleset-visible".

**App rule:** (a) the stock + nQuake + ezQuake lists are "known". (c) any other `.mdl/.spr/.bsp/.md3` may be a mod model, so accept it but mark it "unknown". A `.md3` is "replacement" when the `.mdl` with the same base is known. Image/`.skin` files in `progs/` are valid only as md3 textures (b).

---

## 7. `skins/`: player skins

- **id:** the name is the player's userinfo `skin`, or `baseskin` (default `"base"`) when that is empty (`QW/client/skin.c:23,40-60`). It loads **`skins/<name>.pcx`** and falls back to `skins/<baseskin>.pcx` (`skin.c:120-125`). Missing skins are downloaded (`skin.c:238`). PCX only, at most 320x200.
- **ezQuake** (`skin.c`):
  - Name selection (`skin.c:132-215`):
    - `allskins`
    - forced skins: `enemyforceskins`/`teamforceskins`/`cl_name_as_skin` give the player name, the user id, or `t<n>`/`e<n>` (`skin.c:100-129`)
    - the userinfo `skin`
    - `baseskin`
    - per-state overrides `teamskin`, `enemyskin`, `teamquadskin`, `enemyquadskin`, `teampentskin`, `enemypentskin`, `teambothskin`, `enemybothskin` (`teamplay.c:63-70`)
    - A name containing `..` or starting with `.` falls back to `baseskin` (`skin.c:194-196`). The extension is stripped (`skin.c:222`).
  - File: `skins/<name>.pcx` is built (`skin.c:352`), then `Skin_PixelsLoad` (`skin.c:252-279`) does the following:
    1. When 24-bit is allowed, it tries **`skins/<name>.tga`, `.png`, `.jpg`** through `R_LoadImagePixels(..., TEX_NO_PCX)` (no PCX at this step, `.link` honoured). The size is unlimited.
    2. Otherwise it loads **`skins/<name>.pcx`** as 8-bit, at most 320x200.
    3. On failure it does the same for `skins/<baseskin>.*` (`skin.c:367-376`).
  - No `_luma`.
- nQuake sets `teamskin "player_silver"` and `enemyskin "player_lgtgreen"`.

**App rule:** (c) any `*.pcx|tga|png|jpg|link` under `skins/` is valid, because any name can be chosen by a cvar or by another player's userinfo. `base.pcx` (or `base.*`) should exist as the fallback. pak0 does not ship it, but the QW server/client distribution does. The app could flag that file as "required".

---

## 8. `sound/`: sounds

- The names come from the server's **sound list** (QC `precache_sound`) and from `play`/`playvol` commands. They are loaded as **`sound/<name>` exactly** (id `QW/client/snd_mem.c:114`; ezQuake `snd_mem.c:747`). Missing files are downloaded (`cl_parse.c:279`).
- **No extension substitution.** `foo.wav` is never satisfied by `foo.ogg`. ezQuake decodes through libsndfile (default build, `snd_mem.c:592-803`), so the *content* may be WAV, OGG, FLAC and similar, but the *file name* must match the requested one. That is almost always `*.wav`.
- **Client hard-coded sounds (ezQuake):**
  - `ambience/water1.wav`, `ambience/wind2.wav` (`snd_main.c:360-361`)
  - `misc/menu1.wav`, `misc/menu2.wav`, `misc/menu3.wav`, `misc/talk.wav`, `misc/basekey.wav`, `doors/runeuse.wav`
  - predicted weapon sounds `weapons/ax1 guncock shotgn2 rocket1i spike2 grenade sgun1 lstart lhit coilgun .wav`, `player/plyrjmp8.wav` (`cl_pred.c:62-72`)
  - temp entities `wizard/hit.wav hknight/hit.wav weapons/tink1.wav weapons/ric1-3.wav weapons/r_exp3.wav` (`cl_tent.c:87-93`)
  - `player/axhit2.wav` (`cl_parse.c:1988`)
  - teamplay pickup triggers `items/armor1 damage health1 inv1 protect r_item1 r_item2 suit .wav` (`teamplay.c:2702-2736`)
  - **ezQuake-only:** `client/streakx1.wav` .. `client/streakx6.wav` (`vx_tracker.c:76-81`) and `dmg-notification.wav` at the root of `sound/` (`cl_screen.c:1308`)
  - chat sounds come from cvars `s_mm1_file`, `s_mm2_file`, `s_spec_file`, `s_otherchat_file`, default `misc/talk.wav` (`console.c:68-71`) → (c)
- **`f_modified`** checks: `buttons/airbut1 items/armor1 damage damage2 damage3 health1 inv1 inv2 inv3 itembk2 protect protect2 protect3 r_item1 r_item2 misc/basekey menu1 menu2 menu3 outwater talk water1 water2 player/land land2 plyrjmp8 weapons/pkup doors/runeuse` (`fmod.c:540-620`).
- **Stock list:** pak0/pak1 contain 246 `sound/**/*.wav` files. The folders are `ambience blob boss1 boss2 buttons demon dog doors enforcer fish hknight items knight misc ogre plats player shalrath shambler soldier weapons wizard zombie`.
- **nQuake extras:** `blob/land1.wav misc/flagcap.wav misc/flagtk.wav rune/rune1 rune2 rune22 rune3 rune4.wav weapons/bounce2 chain1 chain2 chain3 coilgun.wav`.
- **Frogbot:** `ra/1 2 3 fight.wav sf1..sf9.wav sffight.wav sfround.wav`.

**App rule:** (a) the stock + client lists are "known". (c) anything else under `sound/` may be referenced by a mod (KTX `ca/`, frogbot `sf*.wav`, custom chat sounds), so accept it but mark it "unknown". Flag files whose extension is not `.wav` as "only used if a mod requests that exact name".

---

## 9. `textures/`: map textures (and misc effect textures) (ezQuake only)

The id QW client has no external textures. Textures come only from the BSP miptex lumps.

### 9.1 World textures, `Mod_LoadExternalTexture` (`r_brushmodel_textures.c:75-160`)
The texture name is the **BSP miptex name** (`tx->name`), with `*` → `#`. For the **world model**, the search order is:
1. `textures/<mapname>/<miptex>`
2. `textures/<mapgroup>/<miptex>`, including the system `exmy` group (`r_brushmodel_textures.c:104-111`)
3. `textures/<altname>`, only for the 9 known name clashes, identified by checksum: `metal5_2_arc metal5_2_x metal5_4_arc metal5_4_double metal5_8_back metal5_8_rune plat_top1_bolt plat_top1_cable` and `sky4`→`sky1` (`r_brushmodel_textures.c:33-48`)
4. `textures/<miptex>`

Then **`<found path>_luma`** is loaded, except for turbulent textures whose name starts with `*` (`r_brushmodel_textures.c:135`). As a result, files such as `#lava1_luma.jpg`/`#slime_luma.jpg` in `textures.pk3` are **never loaded**.

For brush models other than the world (b_*.bsp, doors are part of the world), the path is `textures/bmodels/<miptex>`, then the alt name, then `textures/<miptex>` (`r_brushmodel_textures.c:113-129`).

Active when `gl_externalTextures_world` / `gl_externalTextures_bmodels` are on and `gl_no24bit 0` (`r_texture_cvars.c:170`).

### 9.2 Sky textures (`r_brushmodel_load.c:371-410`)
Miptex names that start with `sky` load `textures/<mapname>/<sky>_solid`, then `textures/<alt>_solid`, then `textures/<sky>_solid`. The same applies to `_alpha`.

### 9.3 Fixed effect textures in `textures/`
- `textures/water_caustic`, `textures/detail`, `textures/shellmap` (`r_rmisc.c:70-73`)
- `textures/particles/particlefont` (`r_particles_qmb.c:363`)
- `textures/shockwavetex`, `textures/zing1`, `textures/sparktex` (`r_particles_qmb.c:385-393`)
- `textures/chaticons` (`r_chaticons.c:232`)
- `textures/scrollbars/{slidebg,arrow_up,arrow_down,slider}` (`Ctrl_ScrollBar.c:43-46`)
- `textures/tracker/<weapon>` (`vx_tracker.c:1453`)
- `textures/scoreboard/...` (`sbar.c:2543,2576`; scoreboard_flags.pk3)
- `textures/sprites/...` (section 6)
- `.link` targets, which are always relative to `textures/`

### 9.4 Name derivation for the app
Read the miptex lump of each BSP the app knows (pak0/pak1 maps, `maps/*.bsp`, pk3 maps). Map `*` to `#`, keep `+0`, `+a` and `{` prefixes, then:
- `textures/<miptex>[_luma].<ext>` is valid when some map uses that miptex. Root-level textures are shared by all maps.
- `textures/<dir>/<miptex>[_luma].<ext>` is valid when `<dir>` is a map name (or `exmy`/user group) and that map/group uses the miptex.
- `_solid`/`_alpha` siblings for sky miptex.
- Known subfolders `bmodels charsets levelshots models wad sprites particles scrollbars tracker scoreboard` must be excluded from the map-name check.

nQuake's `textures.pk3` has 656 root textures plus per-map folders `dm3 dm4 e1m1 e1m2 e2m1 e2m5 e3m1 e4m1 e4m6`.

---

## 10-11. `textures/bmodels/`, `textures/models/`, `textures/wad/`

These were already researched and the code confirms them:
- `textures/bmodels/`: `r_brushmodel_textures.c:113-118` (miptex of `b_*.bsp`, `*`→`#`, `_luma`), and `r_model.c:630-643` (`simple_<bsp>_0`).
- `textures/models/`: `r_aliasmodel_skins.c:98-117`.
- `textures/wad/`: `r_draw.c:357`. Note that `gfx/<lump>` is a second, lower-priority location for the same names (section 3.2), so the HUD lumps can also live in `gfx/`.

Nothing contradicts the earlier findings.

---

## 12. `textures/charsets/`: console font (ezQuake only)

- id: the console font is only the `conchars` lump in `gfx.wad`.
- ezQuake, `Draw_LoadCharset` (`r_draw_charset.c:170-212`):
  - `gl_consolefont` (`r_draw_charset.c:38`, default `"povo5"`; also the `loadcharset` command) loads **`textures/charsets/<name>`** through `R_LoadCharsetImage` (tga > png > jpg > pcx, + `.link`).
  - The special value `original` uses the wad `conchars` lump. If loading fails at startup, the cvar is set back to `original` (`r_draw_charset.c:713-723`).
  - **Alternate charsets** (same base name, extension stripped):
    - `gl_charsets_min 1` (default): only `textures/charsets/<name>-cyr`.
    - `gl_charsets_min 0`: `textures/charsets/<name>-001` .. `<name>-223` (`MAX_USER_CHARSETS 0xE0`, `r_texture.h:43`), with `-cyr` as the fallback for slot 4.
    - The 8-bit fallback for each is `gfx/conchars-<locale>.lmp` (`r_draw_charset.c:140-166`).
  - The default fonts `povo5` and `moondid2` live in `ezquake/ezquake.pk3` (`textures/charsets/`), not in `qw/`. nQuake's `qw/textures/charsets/1..20.png` and `1_fixed.png` are user-selectable alternatives.

**App rule:** (c) any image under `textures/charsets/` is valid. Names matching `<base>-(\d{3}|cyr)` are locale add-ons for `<base>`.

---

## 13. `textures/levelshots/`: loading-screen pictures (ezQuake only)

- `Draw_ConsoleBackground` (`r_draw.c:1058-1088`) loads **`textures/levelshots/<host_mapname>.xxx`**. The `.xxx` is stripped, so the real lookup is tga > png > jpg > pcx (+ `.link`), with `only24bit`.
- It is shown when `scr_conback 1` (default, only while loading) or `2` (always). The picture is stretched to the conback size.
- No `_luma`. No map-group fallback.
- ezquake.pk3 ships 102 levelshots. nQuake's nquake.pk3 adds `aerowalk dm2 dm3 dm4 dm6 e1m2 ztndm3`.

**App rule:** (b) valid when the basename is a map name that exists (loose or in a pak/pk3). Otherwise it is "orphan" (harmless, never shown).

---

## 14. Things the user's folder list gets wrong or misses

1. **`gfx/` is not just console backgrounds.** It also holds menu pictures, HUD-group pictures, the cursor, the menu charset, and a fallback for every gfx.wad HUD lump. The per-map loading background lives in `textures/levelshots/`, not `gfx/`. There is no `textures/conback`.
2. **`env/`**: ezQuake also searches `gfx/env/`. The skybox wind config is read only from `gfx/env/<sky>_wind.cfg`, so `env/*_wind.cfg` is dead.
3. **`lits/`** is only the third location searched. `maps/lits/<map>.lit` and `maps/<map>.lit` take precedence.
4. **`crosshairs/` is correct** (it is not `textures/crosshairs/`). `crosshairs/crosshair.txt` is also a valid file.
5. **Missing folders** that ezQuake reads: `locs/` (`locs/<map>.loc`), `textures/sprites/`, `textures/particles/`, `textures/scrollbars/`, `textures/tracker/`, `textures/scoreboard/`, `gfx/ui/`, `gfx/env/`, `maps/lits/`.
6. **Dead files in the shipped nQuake data:**
   - `_luma` images for `#` (turbulent) textures
   - `gfx/{ttl_cstm,p_option,vidmodes}.jpg`, `gfx/{mp_menu,bigbox}.tga`
   - `env/*_wind.cfg`, `env/*readme.txt`
   - `maps/*.txt`
7. **Shadowing:** the extension order tga > png > jpg > pcx means duplicates with another extension are ignored. A file in `qw/` overrides `ezquake/` and `id1/`.
