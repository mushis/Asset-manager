# Which client cvars decide whether, or which, asset files are loaded

This is a research note for the asset manager's per-file hints, such as "Used when gl_simpleitems 1" or "Picked by crosshairimage". It complements `qw-folder-names.md` (name rules) and `unezquake-assets.md` (unezQuake additions).

**Sources**
- ezQuake: `QW-Group/ezquake-source` at commit `a6069a8`. Paths are `src/*.c`.
- unezQuake: `dusty-qw/unezquake` at `0deac22`. Only the differences are listed, marked **(unez)**.

**Conventions**
- *Cvar names* are the console names. Many C variables have other names. For example, the C variable `amf_tracker_frags` is the cvar `r_tracker_frags`, and the C variable `cl_rocket2grenade` is the cvar `cl_r2g`. Always use the console name in hints.
- *Default* is the value in the `cvar_t` initializer. nQuake's configs may override it.
- **V** means the behaviour was verified in code. **I** means it was inferred (not traced end to end).
- *Takes effect*:
  - `CVAR_RELOAD_GFX` cvars change at once, but textures that are already loaded stay until `vid_reload` (`cvar.h:55`, `vid_sdl2.c:1796`) or until the next map/model load.
  - "OnChange" cvars reload their file immediately.
  - "draw-time" cvars only change what is drawn. The file is loaded anyway.

---

## Global switches that disable all external textures

| Switch | Default | Effect | Source |
|---|---|---|---|
| `gl_no24bit` | `0` | **1 blocks almost every 24-bit file.** It blocks every loader that goes through `R_LoadTextureImage`, `R_LoadPicImage` or `R_LoadCharsetImage`, plus the explicit checks in model skins, world/bmodel textures and player skins. RELOAD_GFX (needs `vid_reload`). | `r_texture_cvars.c:53`; macro `r_texture_load.c:42`; checks `:69`, `:124`, `:200`; `r_aliasmodel_skins.c:90`; `r_texture_cvars.c:168-171`; `skin.c:258` |
| `-no24bit` (command line) | – | Same as `gl_no24bit 1` for the generic loaders (`Block24BitTextures`). | `cmdline_params_ids.h:13`, `r_texture_load.c:42` |
| `gl_externalTextures_world` | `1` | 0 → no `textures/<map>/`, `textures/<group>/`, `textures/<miptex>` (+`_luma`), and no sky `_solid/_alpha` for the world model. RELOAD_GFX. | `r_texture_cvars.c:36,170`; `r_brushmodel_textures.c:87`; `r_brushmodel_load.c:379` |
| `gl_externalTextures_bmodels` | `1` | 0 → no `textures/bmodels/<miptex>` (+`_luma`) for non-world brush models (`maps/b_*.bsp`). RELOAD_GFX. | `r_texture_cvars.c:37,170`; `r_brushmodel_textures.c:87` |

**What `gl_no24bit 1` does NOT block** (these call `R_LoadImagePixels` directly, and that function has no 24-bit check, `r_texture_load.c:268`; **V**):
- Skyboxes `env/`, `gfx/env/` (`r_brushmodel_sky.c:201`).
- `textures/particles/particlefont` (`r_particles_qmb.c:363`). QMB still turns itself off, because `textures/shockwavetex` goes through the blocked loader and its failure aborts QMB init (`r_particles_qmb.c:353,385-388`). So particlefont is loaded but never used.
- `textures/chaticons` (`r_chaticons.c:232`).
- **(unez)** sprays (`cl_sprays.c:423`).

Blocked by `gl_no24bit` (**V**): `textures/models`, `textures/bmodels`, world textures, `simple_*`, md3 textures, `textures/sprites`, `textures/wad`, 24-bit `gfx/*`, crosshairimage, scr_conpicture, levelshots, charsets, tracker images, hud group pictures, 24-bit player skins (it falls back to `.pcx`), caustics/detail/shellmap, and **(unez)** emoticons.

Not texture switches (draw-only): `gl_texturemode`, `gl_picmip`, `gl_max_size`, `gl_lerpimages` and `gl_scale*Textures` only change filtering and size. They never stop a file from loading. `r_drawflat` and `gl_textureless` ignore world textures at draw time.

---

## textures/models (alias-model skins, `simple_*`, `_luma`)

| Files | Cvar | Default | Effect | Source |
|---|---|---|---|---|
| `textures/models/<mdl>_<n>[_<frame>]`, fallback `textures/<mdl>_…` | `gl_no24bit` | `0` | 1 → none loaded (RELOAD_GFX) | `r_aliasmodel_skins.c:90` |
| `textures/models/simple_<mdl>_<n>` (then `textures/simple_…`) | **`gl_simpleitems`** | `0` | **Draw-time.** The simple textures are always loaded at model load. When the cvar is 1, any entity whose model has a valid `simple_` texture is drawn as a 2D sprite instead of the 3D model. **V** | `r_rmain.c:229,910-926`; load `r_model.c:601-645` (called from `r_aliasmodel.c:878`, `r_aliasmodel_md3.c:347`); `cl_ents.c:245` |
| same | `gl_simpleitems_size`, `gl_simpleitems_orientation` | `16`, `2` | Only size/orientation, no file change | `r_rmain.c:230-231` |
| same | `gl_scaleModelSimpleTextures` | `0` | Scaling only (RELOAD_GFX) | `r_texture_cvars.c:49` |
| `*_luma` model skins | **`gl_lumatextures`** | `1` | **Draw-time.** The luma file is loaded anyway, but 0 drops the fullbright layer when drawing | `r_rmain.c:177`; `r_aliasmodel.c:401` |
| `*_luma` | `gl_luma_level` (C: `gl_wicked_luma_level`) | `1` | Changes how luma pixels are thresholded. RELOAD_GFX | `r_texture_cvars.c:38`; `r_aliasmodel_skins.c:46` |
| `textures/models/player_*`, `eyes_*` | `r_fullbrightSkins` | `1` | Brightness only, not a file switch (locked to 0 by MTFL) | `skin.c:62`; `rulesets.c:494` |
| `progs/missile.mdl` skin vs `progs/grenade.mdl` skin | `cl_r2g` | `0` | 1 → rockets are drawn with the grenade model, so the rocket skins are unused | `cl_main.c:184`; `cl_ents.c:1222` |

**Hard-coded and ruleset restrictions (models)**
- `progs/eyes.mdl`: **never** gets an external skin, a `simple_` texture or a `_luma`, in every ruleset (`rulesets.c:64-74`, `76-93`, `921-938`). So `textures/models/eyes_*` files are dead. **V**
- `progs/backpack.mdl`:
  - `_luma` is never loaded, in every ruleset (`rulesets.c:921-938`).
  - The external skin `backpack_*` and `simple_backpack_*` are blocked under the **smackdown, qcon and smackdrive** rulesets. They are allowed under default, thunderdome and mtfl (`rulesets.c:69-70`, `86-87`).
- `progs/player.mdl`: external skin allowed. **`simple_player_*` is never used**, and **`player_*_luma` is never loaded** (`rulesets.c:76-93`, `921-938`).
- TF `progs/turrgun.mdl`, `progs/detpack.mdl`: no `simple_` and no `_luma` (same functions; modhints at `r_model.c:484-489`).
- Server side: serverinfo `24bit_fbs 0` turns lumas off for players, but not for spectators or demos (`cl_view.c:1101-1107`). This is draw-time.
- **(unez)** has no `ruleset` cvar. The ruleset is fixed to `rs_modern` (`rulesets.c:49`), which is not in the smackdown/qcon/smackdrive list. So **backpack external and simple textures are allowed** there. The eyes/player/backpack-luma rules above still apply.

## textures/bmodels (ammo/health boxes `maps/b_*.bsp`)

| Files | Cvar | Default | Effect | Source |
|---|---|---|---|---|
| `textures/bmodels/<miptex>`, `textures/<miptex>` | `gl_externalTextures_bmodels` | `1` | 0 → none loaded (RELOAD_GFX) | `r_texture_cvars.c:37`; `r_brushmodel_textures.c:87,113-117` |
| same | `gl_no24bit` | `0` | 1 → none loaded | `r_texture_cvars.c:170` |
| `<miptex>_luma` | `gl_lumatextures` | `1` | Draw-time. The luma is loaded regardless; 0 hides it. Turbulent (`#`/`*`) names never load a luma | `r_brushmodel_textures.c:135-139`; `glc_brushmodel.c:541`; `glm_rsurf.c:134` |
| bmodel fullbrights | `gl_fb_bmodels` | `1` | Draw-time only (8-bit fullbright colours) | `r_rmain.c:187` |
| `textures/bmodels/simple_b_<box>_0` | **`gl_simpleitems`** | `0` | Draw-time (see models). Also used when a `maps/b_*.md3` replaces the box | `r_model.c:627-636`; `r_brushmodel_load.c:1652` |
| `maps/b_*.md3` replacing `.bsp` | – | – | **No cvar.** The `.md3` is always tried first | `r_model.c:257-264` |

## World textures (`textures/<map>/`, `textures/<group>/`, `textures/`)

These are included for completeness, even though they are not in the app's type list.

| Files | Cvar | Default | Effect | Source |
|---|---|---|---|---|
| all world textures + `_luma` + sky `_solid/_alpha` | `gl_externalTextures_world` | `1` | 0 → none (RELOAD_GFX; also applies on the next map) | `r_texture_cvars.c:36`; `r_brushmodel_textures.c:87`; `r_brushmodel_load.c:379` |
| `textures/<group>/…` | `mapgroup` (command) | system group `exmy` | Changes which subfolder names count | `r_brushmodel_textures.c:104-111` |
| `textures/water_caustic` | `gl_caustics` | `0` | Always loaded; only drawn when 1 (the cvar only adds TEX_COMPLAIN) | `r_rmisc.c:70`; `r_rmain.c:176` |
| `textures/detail` | `gl_detail` | `0` | Same as caustics | `r_rmisc.c:71`; `r_rmain.c:173` |
| `textures/shellmap` | `gl_powerupshells` | `1` | Always loaded (a built-in texture is generated if it is missing); drawn when > 0 | `r_rmisc.c:73-76`; `r_rmain.c:213` |
| `textures/chaticons` | `r_chaticons_alpha` | `0.8` | Always loaded; 0 makes the icons invisible | `r_chaticons.c:232`; `cl_screen.c:116` |

## textures/wad (HUD/status-bar pics; fallback `gfx/<lump>`)

The lumps are always loaded at startup (`sbar.c:216-240` → `Draw_CacheWadPic`, `r_draw.c:357-358`). No cvar stops them from loading. These cvars decide whether they are **shown**:

| Files | Cvar | Default | Effect | Source |
|---|---|---|---|---|
| all `sb_*`, `ibar`, `sbar`, `inv_*`, `num_*`, `face*`… | `scr_newhud` | `0` | 0 = classic status bar (uses the wad pics). 1 = new HUD only. 2 = both (classic bar + HUD elements) | `cl_screen.c:107,990`; `hud.c:1539` |
| classic bar | `cl_sbar` | `0` | 1 = solid classic bar layout (still wad pics) | `cl_main.c:160`; `cl_screen.c:349-361` |
| `num_*`, `anum_*` in the new HUD | `hud_<elem>_style` (health, armor, ammo…) | `0` | Style 0/2 = 24-px **wad digit pics**. Style 1/3 = charset text (no wad pics) | `hud_common.c:306-316` |
| `inv_*`, `inv2_*` in the new HUD | `hud_gunN_style` / `hud_guns_style` | `0` | 0 = wad weapon pics. 1-8 = text variants | `hud_guns.c:30-120` |
| everything HUD | `r_drawhud` | `1` | 0 = no HUD drawn | `cl_screen.c:131,988` |
| `gfx/<value>` (HUD background) | **`hud_group1_picture` … `hud_group9_picture`** | `""` | **Picks the file** `gfx/<value>` (24-bit only). Shown when `hud_groupN_show 1` | `hud_groups.c:34,130-148,213` |
| same | ruleset | – | qcon/smackdown/smackdrive/thunderdome (**unez:** also modern) **block changing** the picture while playing (spectators, demos and standby are allowed). A value already set still loads | `rulesets.c:895-906`; `hud_groups.c:162` |

## textures/tracker (frag-tracker weapon icons)

| Files | Cvar | Default | Effect | Source |
|---|---|---|---|---|
| `textures/tracker/<img>` | (fragfile) | – | **The file name comes from `fragfile.dat`**: the 5th argument of each weapon-class definition. The images are loaded when the fragfile loads | `fragstats.c:331`; `vx_tracker.c:1453-1455` |
| same | `cl_loadFragfiles` | `1` | 0 → no fragfile, so no tracker images | `fragstats.c:29,790-793` |
| same | `loadFragfile <name>` (command) | `fragfile` | Picks `fragfile.dat` (or `../ezquake/fragfile.dat`), which in turn picks the images | `fragstats.c:231-238,861` |
| same | **`cl_useimagesinfraglog`** | `0` | **Draw-time.** 1 = show the weapon images instead of text in the tracker and frag log | `fragstats.c:31,867`; `vx_tracker.c:685,745` |
| same | `r_tracker` / `r_tracker_frags` | `1` / `1` | Tracker on/off; frag lines on/off (2 = alternative format) | `vx_tracker.c:94,96,168` |
| same | `r_tracker_flags`, `r_tracker_streaks` | `0`, `0` | Flag/streak lines | `vx_tracker.c:95,97` |
| same | `r_tracker_images_scale` | `1` | Size only | `vx_tracker.c:109` |

## textures/particles (QMB `particlefont`) and QMB effect textures

`QMB_InitParticles` always loads `textures/particles/particlefont` and then `textures/shockwavetex`, `textures/zing1` and `textures/sparktex`. **If any of these is missing, QMB is disabled entirely** (`qmb_initialized = false`), and every QMB cvar silently falls back to classic particles (`r_particles_qmb.c:353,363-397`). No cvar stops the load. These cvars decide whether QMB textures are **used**:

| Cvar | Default | Uses QMB textures when | Source |
|---|---|---|---|
| `gl_part_explosions`, `gl_part_blobs`, `gl_part_lavasplash`, `gl_part_telesplash` | `0` | ≠0 | `r_rmain.c:194,206-208`; `r_part.c:860-870` |
| `gl_part_blood`, `gl_part_gunshots`, `gl_part_spikes`, `gl_part_inferno` | `0` | ≠0 | `r_rmain.c:203-209`; `r_part.c:781-785` |
| `gl_part_trails` | `0` | ≠0 | `r_rmain.c:196`; `r_part.c:827,850` |
| `gl_part_bloodtrails` | **`1`** | ≠0 | `r_rmain.c:195`; `r_part.c:819,842` |
| `gl_part_bubble` | **`1`** | ≠0 | `r_rmain.c:210` |
| `r_rocketTrail`, `r_grenadeTrail` | `1`, `1` | value 8, 10 or 11 (QMB trails; otherwise they fall back to 1) | `cl_main.c:236-237`; `r_part_trails.c:39` |
| `r_explosionType` | `1` | 7 (with `gl_part_explosions`) or 8 | `cl_main.c:240`; `cl_tent.c:733-736` |
| `gl_particle_*` (C: `amf_part_*`), `gl_lightning`, `gl_lightning_sparks`, `gl_coronas`, `gl_extratrails`, `gl_nailtrail`, `gl_turb_trails`, `gl_buildingsparks`… | mostly `0` | ≠0 | `vx_stuff.c:34-92` |
| `r_drawparticles` | `1` | 0 = no particles at all | `r_part.c:767` |

Ruleset: **smackdown, qcon and smackdrive** set `restrictParticles`. This disables the `gl_particle_sparks/spikes/gunshots` and `gl_lightning` effects (`rulesets.c:314,380,576`; `cl_tent.c:604-665,828,1152`) and forces particles to be drawn (`r_part.c:767`). The `gl_part_*` cvars are not restricted.

## crosshairs

| Files | Cvar | Default | Effect | Source |
|---|---|---|---|---|
| `crosshairs/<value>` (tga > png > jpg > pcx, 24-bit only) | **`crosshairimage`** | `""` | **Picks the file.** OnChange → loads immediately. **When it loads, it is drawn regardless of `crosshair`** (the draw condition ORs in `CROSSHAIR_IMAGE`) | `r_draw.c:62,192-210,617-619` |
| `crosshairs/crosshair.txt` | **`crosshair`** | `4` | Used only when `crosshair 1` **and** no `crosshairimage` is loaded. Values 2-7 are built-in crosshairs, 0 = none (if no image) | `cl_view.c:78`; `r_draw.c:222,617-653` |
| all | `crosshairalpha` | `1` | 0 hides every crosshair | `r_draw.c:64,624` |
| all | `scr_showcrosshair` | `1` | Hides the crosshair while the scoreboard is shown, when 0 | `cl_screen.c:127,713` |
| **(unez)** all crosshair files | `crosshairvec` | `0` | 1 = procedural vector crosshair. It **overrides `crosshairimage` and `crosshair.txt`** (drawn first) | unez `r_draw.c:79,932-958` |

## env + gfx/env (skyboxes)

| Files | Cvar | Default | Effect | Source |
|---|---|---|---|---|
| `env/<v>{rt,bk,lf,ft,up,dn}`, `gfx/env/<v>…`, `env/<v>_…`, `gfx/env/<v>_…` | **`r_skyname`** | `""` | **Picks the base name.** OnChange → loads immediately. The commands `loadsky <name>` and `r_skyname <name>` are equivalent. Also set by the BSP worldspawn `sky`/`skyname` at map load and by a server stuffcmd. Names containing `.` are rejected | `r_rmain.c:171`; `r_brushmodel_sky.c:93-135,184-208,537`; `r_brushmodel_load.c:946-947` |
| same | `skygroup` (command) | system group `exmy` | If the current map is in a sky group, **the group name replaces `r_skyname`** as the base name | `r_brushmodel_sky.c:101-102`; `cl_skygroups.c` |
| `gfx/env/<v>_wind.cfg` | `r_skywind` | `1` | 1 → the wind cfg is loaded with the sky | `r_rmain.c:172`; `r_brushmodel_sky.c:119-121` |
| skybox | `r_fastsky` | `0` | Draw-time: 1 = flat colour (`r_skycolor`). The skybox is still loaded | `r_rmain.c:158`; `glc_sky.c:211` |
| skybox | `gl_no24bit` | – | **Does not block** skyboxes (see globals) | `r_brushmodel_sky.c:201` |

## gfx (console background, menu pics, HUD fallback)

| Files | Cvar | Default | Effect | Source |
|---|---|---|---|---|
| `gfx/<value>` (24-bit) | **`scr_conpicture`** | `conback` | **Picks the file.** OnChange → loads immediately. `gfx/conback.lmp` is still required | `r_draw.c:52,175-190,1041` |
| `gfx/<value>` | `hud_groupN_picture` | `""` | see textures/wad | `hud_groups.c:34` |
| `gfx/<lump>` | – | – | Fallback for `textures/wad/<lump>`, used only when no `textures/wad/` version exists | `r_draw.c:357-358` |
| console background | `scr_conback` | `1` | When `textures/levelshots/<map>` exists it **replaces** `gfx/<scr_conpicture>`: while loading (1) or always (2). 0 = never | `r_draw.c:50,1061-1088` |
| console background | `scr_conalpha` | `0.8` | Opacity only | `r_draw.c:49` |
| **(unez)** menu pics `gfx/ttl_*`, `p_*`, … | `menu_classic` | – | Only the classic menu draws the `gfx/` menu pics (from `unezquake-assets.md`; I) | – |

## textures/charsets (console font)

| Files | Cvar | Default | Effect | Source |
|---|---|---|---|---|
| `textures/charsets/<value>` | **`gl_consolefont`** | `povo5` | **Picks the file.** OnChange → loads immediately. `original` = wad `conchars`. Also set by the command `loadcharset <name>` | `r_draw_charset.c:38,215-230,682` |
| `textures/charsets/<value>-cyr` / `<value>-001…-223` | `gl_charsets_min` | `1` | 1 → only `-cyr` is loaded. 0 → all `-NNN` alternates as well | `r_draw_charset.c:40,192` |
| all | `gl_no24bit` | – | 1 → only the 8-bit wad `conchars` is used | `r_texture_load.c:200` |

`gfx/mcharset` (the menu charset) has no cvar.

## textures/levelshots

| Files | Cvar | Default | Effect | Source |
|---|---|---|---|---|
| `textures/levelshots/<current map>` | **`scr_conback`** | `1` | 0 = never loaded. 1 = shown as the console background while the map loads. 2 = always | `r_draw.c:50,1061-1082` |
| same | `gl_no24bit` | – | 1 → never loaded | `r_texture_load.c:124` |
| **(unez)** same | new menu / `hud_mapvote` | – | Extra consumers; no gating cvar found (I) | unez `menu_ui_bridge.c:246`; `r_draw.c:1596` |

## skins (player skins)

| Files | Cvar | Default | Effect | Source |
|---|---|---|---|---|
| `skins/<value>` | **`skin`** (userinfo) | `""` | **Picks your own skin.** Other players' skins come from their userinfo | `cl_main.c:255`; `skin.c:147` |
| `skins/<value>` | **`baseskin`** | `base` | The fallback when no skin is set or the skin fails to load (RELOAD_GFX) | `skin.c:56,155,369` |
| `skins/<value>` | **`teamskin`**, **`enemyskin`** | `""` | **Picks the file** for teammates/enemies (team games only; not in TF, and not if the server's FPD forbids it) | `teamplay.c:63-64`; `skin.c:189-193` |
| `skins/<value>` | `teamquadskin`, `enemyquadskin`, `teampentskin`, `enemypentskin`, `teambothskin`, `enemybothskin` | `""` | Picks the file when the player has quad / pent / both | `teamplay.c:65-70`; `skin.c:178-187` |
| `skins/<value>` | `allskins` (command, not a cvar) | `""` | Forces one skin on everyone | `skin.c:139-140`; `host.c:618` |
| `skins/<name>`, `skins/<userid>`, `skins/t<n>`, `skins/e<n>` | `teamforceskins`, `enemyforceskins` | `0` | 1 = name, 2 = userid, 3 = `t1`/`e1` numbering. `enemyforceskins` is not allowed in play when the FPD forbids it, and cannot change mid-match | `skin.c:53-54,70-129,940-960` |
| same | `cl_name_as_skin` | `0` | Same as above, but only in demos/spectating | `skin.c:57,90-93` |
| all `skins/*` | **`noskins`** | `0` | 1 = no skins loaded at all (always base colours). 2 = load local skins but do not download | `skin.c:51,339,418` |
| `skins/*.tga/png/jpg` vs `.pcx` | `gl_no24bit` | `0` | 1 → only `.pcx` | `skin.c:258` |
| skins | `r_fullbrightSkins`, `r_enemyskincolor`, `r_teamskincolor`, `r_skincolormode` | `1`, `""`, `""`, `0` | Draw-time colour/brightness only | `skin.c:58-62` |
| **(unez)** skins | `r_teammateskincolors_enable`, `r_teammateskincolor1..6` | `0`, `""` | Colour only | unez `skin.c` |

## progs (models)

| Files | Cvar | Default | Effect | Source |
|---|---|---|---|---|
| `progs/<x>.md3` replacing `<x>.mdl` | – | – | **No cvar.** Always tried first | `r_model.c:257-264` |
| md3 textures (`.skin`, shader names) | `gl_no24bit` | `0` | 1 → no md3 textures (a dummy texture is used) | `r_aliasmodel_md3.c:298-325` |
| `progs/vwplayer.mdl`, `progs/w_*.mdl` (vweps) | **`cl_novweps`** | `0` | 1 → the vwep models are not loaded or downloaded | `cl_main.c:234`; `cl_parse.c:645-650` |
| same | **`r_drawvweps`** | `1` | 0 → loaded but not drawn (the plain `player.mdl` is used) | `cl_main.c:235`; `cl_ents.c:2205,2363` |
| `progs/v_*.mdl` | `r_drawviewmodel` | `1` | Draw-time | `cl_view.c:61` |
| `progs/grenade.mdl` for rockets | `cl_r2g` | `0` | see models | `cl_main.c:184` |
| `textures/sprites/<spr>_<n>`, `textures/<spr>_<n>` | `gl_no24bit` | `0` | 1 → only the internal sprite frames | `r_sprites.c:43-48` |
| any custom `progs/*` | `f_modified` / ruleset qcon | – | qcon treats an alternative `progs/player.mdl` as modified (`Rulesets_AllowAlternateModel`); f_modified checksums the stock models | `rulesets.c:166-175`; `fmod.c:332` |

## sprays (unezQuake only)

| Files | Cvar | Default | Effect | Source |
|---|---|---|---|---|
| `<cl_spray_image_path>/<value>` | **`cl_spray_image`** | `spray` | **Picks your spray file** | unez `cl_sprays.c:33,651` |
| folder | **`cl_spray_image_path`** | `sprays` | **Picks the folder** | unez `cl_sprays.c:34,604` |
| all sprays | `cl_spray_show` | `0.5` | ≤0 = sprays are not drawn | unez `cl_sprays.c:30,2240` |
| sounds `sound/<value>` | `cl_spray_sound`, `cl_spray_reject_sound` | `builtin/spray.flac`, `builtin/reject.flac` | Pick the files (the defaults are built-in) | unez `cl_sprays.c:38-39` |
| | `gl_no24bit` | – | Not blocked (`R_LoadImagePixels`) | unez `cl_sprays.c:423` |

## textures/emoticons (unezQuake only)

| Files | Cvar | Default | Effect | Source |
|---|---|---|---|---|
| `textures/emoticons/emoticons.json` + the images listed in it | **`cl_emoticons`** | `0` | Always loaded; **used only when 1** | unez `cl_emoticons.c:35,125,163,312,373` |
| same | `gl_no24bit` | – | 1 → images are not loaded (`R_LoadTextureImage`) | unez `cl_emoticons.c:163` |
| `textures/scoreboard/<value>.json` (+ atlas images) | `scr_scoreboard_login_flagfile` | `flags` | Picks the flag file (also used as `:xx:` emoticons in unez) | `sbar.c:90,2629` |

---

## Ruleset summary (asset-relevant only)

| Ruleset | Asset effect | Source |
|---|---|---|
| all | `eyes.mdl`: no external, simple or luma textures. `player.mdl`: no simple, no luma. `backpack.mdl`: no luma. TF turrgun/detpack: no simple, no luma | `rulesets.c:64-93,921-938` |
| smackdown, qcon, smackdrive | additionally: `backpack.mdl` gets no external skin and no `simple_` | `rulesets.c:69-70,86-87` |
| smackdown, qcon, smackdrive | QMB sparks/spikes/gunshot/lightning effects are off | `rulesets.c:314,380,576` |
| qcon, smackdown, smackdrive, thunderdome (**unez:** + modern) | `hud_groupN_picture` cannot change in play | `rulesets.c:895-906` |
| MTFL | `r_fullbrightSkins 0`, `gl_textureless 0`, `r_drawflat 0` locked; `gl_picmip` ≤ 3; `gl_max_size` ≥ 512 (quality only) | `rulesets.c:491-504` |
| qcon | an alternative `progs/player.mdl` counts as modified in `f_modified` | `rulesets.c:166-175` |
| server | serverinfo `24bit_fbs 0` turns off lumas (players); `fbskins` caps fullbright skins | `cl_view.c:1101-1107` |
| **(unez)** | No `ruleset` cvar. Always `rs_modern` (backpack external/simple allowed) | unez `rulesets.c:49` |

## Verification notes
- Every cvar name and default above was read from its `cvar_t` initializer (**V**).
- These are **I** (inferred, not traced end to end):
  - `scr_newhud 2` meaning "both"
  - unez `menu_classic` and the levelshot consumers
  - the exact `hud_*_style` value ranges for elements other than numbers and guns
