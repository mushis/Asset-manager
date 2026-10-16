namespace QuakeAssetManager;

/// <summary>
/// The "Details about QuakeWorld assets" text in the About dialog (sources: docs/qw-folder-names.md).
/// Markup: "# " heading, **bold**, `code`. Each line is translated on its own (lang/*.json).
/// </summary>
public static class AboutFacts
{
    public static readonly string[] Lines =
    [
        "# How the client finds a file",
        "• **Search order:** ezQuake looks in `id1`, then `ezquake`, then `qw`. A file in a later folder wins.",
        "• **Loose files win:** inside a game folder, a loose file beats the same file inside a `.pak` or `.pk3`.",
        "• **Image formats:** the client tries `.tga`, then `.png`, `.jpg` and `.pcx`. The first one found wins, so a `.png` next to a `.tga` with the same name is never used.",
        "• **Links:** a `<name>.link` text file can point to another image (its first line is a path under `textures/`).",
        "• **Special characters:** in file names `*` becomes `#`. That's why water, lava and slime textures are named like `#water1.png`.",
        "• **Glow layers:** `_luma` files add a glowing (fullbright) layer. Only model skins and map/item-box textures use them; `_glow` is never loaded.",
        "• **Letter case:** names are case sensitive on Linux, so this app always copies files into the game in lower case.",
        "• **Subfolders:** subfolders inside a texture folder are not loaded (that's why \"original\" folders do nothing).",
        "",
        "# Asset types shown by default",
        "• **Models** (`qw/textures/models`): `<model>_<skin>`, e.g. `armor_0`, `armor_1`, `armor_2` (armor has 3 skins; most models have 1). Optional `_luma`. Animated skins use `<model>_<skin>_<frame>`; simple items use `simple_<model>_<n>`. `eyes.mdl` never uses replacement skins.",
        "• **BModels** (`qw/textures/bmodels`): the health and ammo boxes (`maps/b_*.bsp`), named after the textures inside them: `med3_0`, `med100`, `+0_med25` (animation frames keep their `+0_` prefix), `shot0sid`, `rockettop`… Simple items: `simple_b_bh100_0`.",
        "• **HUD** (`qw/textures/wad`): status bar pictures from `gfx.wad`, a fixed list: `num_0`..`9`, `anum_0`..`9`, `sb_*`, `face1`..`5`, `inv_*` weapon icons, `ibar`, `sbar`, `scorebar`, `disc`, `ram`, `net`, `turtle`, `backtile`. Never loaded: `anum_colon`, `inv_rocket`, `conchars`.",
        "• **Tracker** (`qw/textures/tracker`): frag tracker weapon icons. Names come from the WEAPON_CLASS lines of `fragfile.dat`, e.g. `\\rl\\` loads `textures/tracker/rl`.",
        "• **Particles** (`qw/textures/particles`): a single file, `particlefont`: a 256×192 atlas cut into blood, smoke, lava, flare, bubble and spark particles.",
        "",
        "# Other asset types (hidden by default, switch them on in Settings)",
        "• **Crosshairs** (`qw/crosshairs`): any image name, chosen with `crosshairimage`.",
        "• **Skyboxes** (`qw/env`, also `gfx/env`): `<sky>` plus `rt`, `bk`, `lf`, `ft`, `up`, `dn` (all 6 faces). A sky's `_wind.cfg` is only read from `gfx/env`.",
        "• **Gfx** (`qw/gfx`): the console background (`conback`, or the name in `scr_conpicture`), menu pictures and the cursor. `gfx/conback.lmp` must exist or the game won't start.",
        "• **Charsets** (`qw/textures/charsets`): console fonts, any name, chosen with `gl_consolefont`.",
        "• **Levelshots** (`qw/textures/levelshots`): the loading-screen picture of each map, named after the map.",
        "• **Skins** (`qw/skins`): player skins, any name. Tries `.tga`, `.png`, `.jpg`, then `.pcx`; falls back to `base`.",
        "• **Progs** (`qw/progs`): models and sprites, any name. A `.md3` with the same name replaces the `.mdl`.",
        "",
        "# Not managed by the app",
        "• **Sounds** (`qw/sound`): exact paths with subfolders; a `.ogg` never replaces a requested `.wav`.",
        "• **Maps** (`qw/maps`): `<map>.bsp` plus side files; the item boxes (`b_*.bsp`) live here too.",
        "• **Lits and Locs** (`qw/lits`, `qw/locs`): coloured lighting (`<map>.lit`) and team-play location names (`<map>.loc`).",
        "• **Map textures** (`qw/textures/<map>/`): looked up per map, then per map group, then in `textures/`.",
    ];
}
