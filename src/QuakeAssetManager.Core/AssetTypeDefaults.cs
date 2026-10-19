namespace QuakeAssetManager.Core;

/// <summary>
/// The asset types the app knows about, written to asset-types.json on first run. The first five are shown;
/// the rest are hidden until switched on in Settings. Sources: docs/qw-folder-names.md.
/// </summary>
public static class AssetTypeDefaults
{
    private static readonly List<string> Images = [".tga", ".png", ".jpg", ".pcx", ".link"];
    private const string Gfx = "https://gfx.quakeworld.nu/browse/";

    /// <summary>Model and item-box textures: simple_* items and _luma glow maps depend on a setting.</summary>
    private static readonly CvarHint[] ItemHints =
    [
        new("^simple_", "gl_simpleitems"),
        new(@"_luma\.[^.]+$", "gl_lumatextures", "drawn"),
    ];

    /// <summary>The 62 images in unezQuake's emoticons.pk3 (names listed in its emoticons.json).</summary>
    public static readonly string[] EmoticonNames =
    [
        "smile", "grin", "wink", "tongue", "sad", "cry", "heart", "surprised", "cool", "kiss", "neutral", "angry", "happy",
        "laugh", "meh", "thumbsup", "thumbsdown", "clap", "fire", "skull", "rocket", "bomb", "trophy", "hundred", "eyes",
        "thinking", "rofl", "joy", "sob", "sweat", "rage", "poop", "ghost", "party", "crown", "zap", "snail", "wave", "muscle",
        "pray", "ok", "victory", "facepalm", "shrug", "sleeping", "clown", "nerd", "devil", "angel", "hearteyes", "confused",
        "scream", "wow", "unamused", "pensive", "hug", "gun", "boom", "target", "medal", "swords",
    ];

    public static List<AssetType> Create() =>
    [
        new()
        {
            Id = "models", RecognizeBy = "names", DisplayName = "Models", Visible = true,
            Description = "Model skins: weapons, armour, items, projectiles",
            ActiveFolderPath = "qw/textures/models", AssetsFolderName = "models",
            Extensions = [.. Images], NamesFile = "names/models.json",
            NameSources = ["mdl-skins", "mdl-simple", "md3-simple"],
            ArchivePrefixes = ["textures/models/"], FolderHints = ["models"], Hints = [.. ItemHints],
            Online =
            [
                new("Models (all)", Gfx + "models/"),
                new("Textures / Armor", Gfx + "textures/armor/"),
                new("Textures / Backpack", Gfx + "textures/backpack/"),
                new("Textures / Weapon", Gfx + "textures/weapon/"),
                new("Textures / Sets", Gfx + "textures/sets/"),
            ],
            Notes = "<model>_<skin>[_<frame>][_luma], simple_<model>_<n>. Model names and skin counts come from the .mdl files.",
        },
        new()
        {
            Id = "bmodels", RecognizeBy = "names", DisplayName = "BModels", Visible = true,
            Description = "Health and ammo box textures",
            ActiveFolderPath = "qw/textures/bmodels", AssetsFolderName = "bmodels",
            Extensions = [.. Images], NamesFile = "names/bmodels.json",
            NameSources = ["bsp-items", "bsp-items-simple"],
            ArchivePrefixes = ["textures/bmodels/"], FolderHints = ["bmodels"], Hints = [.. ItemHints],
            Notes = "Texture names inside maps/b_*.bsp ('*' becomes '#'), optional _luma, simple_<b_box>_0.",
        },
        new()
        {
            Id = "wad", RecognizeBy = "names", DisplayName = "HUD", Visible = true,
            Description = "HUD pictures: status bar, numbers, icons",
            ActiveFolderPath = "qw/textures/wad", AssetsFolderName = "wad",
            Extensions = [.. Images], NamesFile = "names/wad.json", NameSources = ["gfx-wad-lumps"],
            ArchivePrefixes = ["textures/wad/"], FolderHints = ["wad", "hud", "gfx"],
            Online =
            [
                new("HUD (all)", Gfx + "hud/"),
                new("Face and armor", Gfx + "hud/face_and_armor/"),
                new("Icons", Gfx + "hud/icons/"),
                new("Numbers", Gfx + "hud/numbers/"),
                new("Sets", Gfx + "hud/sets/"),
                new("WADs", Gfx + "hud/wads/"),
                new("Weapon", Gfx + "hud/weapon/"),
            ],
            Notes = "gfx.wad lump names the client asks for (sbar.c, cl_screen.c, r_draw.c).",
        },
        new()
        {
            Id = "tracker", RecognizeBy = "names", DisplayName = "Tracker", Visible = true,
            Description = "Frag tracker weapon icons",
            ActiveFolderPath = "qw/textures/tracker", AssetsFolderName = "tracker",
            Extensions = [.. Images], NamesFile = "names/tracker.json",
            NameSources = ["fragfile"],
            ArchivePrefixes = ["textures/tracker/"], FolderHints = ["tracker"],
            Hints = [new(".*", "cl_useimagesinfraglog", "shown")],
            Notes = "Image names from WEAPON_CLASS lines in fragfile.dat, e.g. \\rl\\ loads textures/tracker/rl.",
        },
        new()
        {
            Id = "particles", RecognizeBy = "names", DisplayName = "Particles", Visible = true,
            Description = "Particle texture atlas (particlefont)",
            ActiveFolderPath = "qw/textures/particles", AssetsFolderName = "particles",
            Extensions = [.. Images], NamesFile = "names/particles.json",
            ArchivePrefixes = ["textures/particles/"], FolderHints = ["particles"],
            Notes = "Only textures/particles/particlefont is loaded (a 256x192 atlas).",
        },

        // ---------- Known but hidden by default ----------
        new()
        {
            Id = "crosshairs", DisplayName = "Crosshairs",
            Description = "Crosshair images (crosshairimage)",
            ActiveFolderPath = "qw/crosshairs", AssetsFolderName = "crosshairs",
            Extensions = [.. Images, ".txt"], NamesFile = "names/crosshairs.json",
            ArchivePrefixes = ["crosshairs/"], FolderHints = ["crosshairs", "crosshair"],
            Hints = [new(@"^crosshair\.txt$", "crosshair"), new(".*", "crosshairimage", "chosen", "")],
            Online = [new("Crosshairs", Gfx + "crosshairs/")],
            Notes = "Any name: the crosshairimage setting picks the file.",
        },
        new()
        {
            Id = "skyboxes", DisplayName = "Skyboxes",
            Description = "Skybox faces (r_skyname + rt, bk, lf, ft, up, dn)",
            ActiveFolderPath = "qw/env", AssetsFolderName = "skyboxes",
            Extensions = [.. Images], NamesFile = "names/skyboxes.json",
            ArchivePrefixes = ["env/", "gfx/env/"], FolderHints = ["env", "sky", "skybox", "skyboxes"],
            Hints = [new(".*", "r_skyname", "chosen", "")],
            Notes = "Also read from gfx/env. A sky's _wind.cfg is only read from gfx/env.",
        },
        new()
        {
            Id = "gfx", DisplayName = "Gfx",
            Description = "2D graphics: console background, menus, cursor",
            ActiveFolderPath = "qw/gfx", AssetsFolderName = "gfx",
            Extensions = [.. Images, ".lmp"], NamesFile = "names/gfx.json",
            ArchivePrefixes = ["gfx/"], FolderHints = ["gfx", "conback", "conbacks"],
            Hints = [new("^conback", "scr_conpicture", "chosen", "")],
            Online = [new("Conbacks", Gfx + "conbacks/")],
            Notes = "gfx/<scr_conpicture> (default conback), menu pictures, cursor. Also a fallback for HUD pictures.",
        },
        new()
        {
            Id = "charsets", DisplayName = "Charsets",
            Description = "Console fonts (gl_consolefont)",
            ActiveFolderPath = "qw/textures/charsets", AssetsFolderName = "charsets",
            Extensions = [.. Images], NamesFile = "names/charsets.json",
            ArchivePrefixes = ["textures/charsets/"], FolderHints = ["charsets", "charset"],
            Hints = [new(".*", "gl_consolefont", "chosen", "")],
            Online = [new("Charsets", Gfx + "charsets/")],
            Notes = "Any name: gl_consolefont picks it. Optional <name>-<NNN> and <name>-cyr alternates.",
        },
        new()
        {
            Id = "levelshots", DisplayName = "Levelshots",
            Description = "Loading-screen picture per map",
            ActiveFolderPath = "qw/textures/levelshots", AssetsFolderName = "levelshots",
            Extensions = [.. Images], NamesFile = "names/levelshots.json",
            ArchivePrefixes = ["textures/levelshots/"], FolderHints = ["levelshots"],
            Hints = [new(".*", "scr_conback", "shown")],
            Notes = "textures/levelshots/<mapname>. Any map name, installed or not.",
        },
        new()
        {
            Id = "skins", DisplayName = "Skins",
            Description = "Player skins",
            ActiveFolderPath = "qw/skins", AssetsFolderName = "skins",
            Extensions = [".tga", ".png", ".jpg", ".pcx"], NamesFile = "names/skins.json",
            ArchivePrefixes = ["skins/"], FolderHints = ["skins"],
            Hints = [new(".*", "skin, teamskin, enemyskin", "chosen", "")],
            Online = [new("Skins", Gfx + "skins/")],
            Notes = "Any name. Tries tga, png, jpg, then pcx; falls back to base.pcx.",
        },
        new()
        {
            Id = "progs", DisplayName = "Progs",
            Description = "Models and sprites (.mdl, .md3, .spr)",
            ActiveFolderPath = "qw/progs", AssetsFolderName = "progs",
            Extensions = [".mdl", ".md3", ".spr", ".png", ".tga", ".jpg", ".pcx", ".skin"], NamesFile = "names/progs.json",
            RecognizeBy = "extension",
            ArchivePrefixes = ["progs/"], FolderHints = ["progs"],
            Hints = [new("^(w_|vwplayer)", "cl_novweps", "used", "0")],
            Notes = "Any model name (the server decides). Images and .skin files next to .md3 models. Item boxes (b_*.bsp) belong in maps/, not here.",
        },
        new()
        {
            Id = "sprays", DisplayName = "Sprays",
            Description = "Spray decals (unezQuake, cl_spray_image)",
            ActiveFolderPath = "qw/sprays", AssetsFolderName = "sprays",
            Extensions = [".png", ".jpg", ".jpeg", ".tga", ".pcx"], NamesFile = "names/sprays.json",
            ArchivePrefixes = ["sprays/"], FolderHints = ["sprays", "spray"],
            Hints = [new(".*", "cl_spray_image", "chosen", "")],
            Notes = "unezQuake only. Any name; at most 128x128. Every image in the folder is loaded at start-up.",
        },
        new()
        {
            Id = "emoticons", RecognizeBy = "names", DisplayName = "Emoticons",
            Description = "Chat emoticons (unezQuake, cl_emoticons)",
            ActiveFolderPath = "qw/textures/emoticons", AssetsFolderName = "emoticons",
            Extensions = [.. Images, ".json"], NamesFile = "names/emoticons.json",
            ArchivePrefixes = ["textures/emoticons/"], FolderHints = ["emoticons", "emoticon"],
            Hints = [new(".*", "cl_emoticons")],
            Notes = "unezQuake only. The images named in emoticons.json; a loose image replaces the one in emoticons.pk3.",
        },
    ];

    /// <summary>Default contents of names/&lt;id&gt;.json: fixed names and regex patterns (matched against the name without extension).</summary>
    public static NamesConfig DefaultNames(string id) => id switch
    {
        "wad" => new NamesConfig
        {
            Description = "gfx.wad lump names ezQuake looks up in textures/wad. Not loaded: anum_colon, inv_rocket, inv_nails, inv_shells, sba*, conchars.",
            Names = [.. ValidNameSet.WadNames()],
        },
        "tracker" => new NamesConfig
        {
            Description = "Icons ezQuake ships in ezquake.pk3. More come from fragfile.dat automatically.",
            Names = [.. ValidNameSet.DefaultTrackerNames],
        },
        "particles" => new NamesConfig { Description = "The QMB particle atlas.", Names = ["particlefont"] },
        "crosshairs" => new NamesConfig { Description = "Any image name (chosen with crosshairimage), plus crosshair.txt.", Patterns = [".+"] },
        "skyboxes" => new NamesConfig { Description = "<sky><suffix> or <sky>_<suffix>, suffix rt bk lf ft up dn.", Patterns = ["^.+(rt|bk|lf|ft|up|dn)$"] },
        "gfx" => new NamesConfig
        {
            Description = "Any name (scr_conpicture picks the console background). Listed: names the client asks for.",
            Names =
            [
                "conback", "cursor", "mcharset", "qplaque", "ttl_main", "ttl_sgl", "ttl_cstm", "mainmenu", "sp_menu", "mp_menu",
                "p_load", "p_save", "p_multi", "p_option", "pause", "loading", "complete", "finale", "inter", "ranking",
                "menudot1", "menudot2", "menudot3", "menudot4", "menudot5", "menudot6", "bigbox", "vidmodes", "menuplyr",
                "box_tl", "box_tm", "box_tr", "box_ml", "box_mm", "box_mm2", "box_mr", "box_bl", "box_bm", "box_br",
            ],
            Patterns = [".+"],
        },
        "levelshots" => new NamesConfig { Description = "Any map name.", Patterns = [".+"] },
        "progs" => new NamesConfig { Description = "Any model name.", Patterns = [".+"] },
        "charsets" => new NamesConfig { Description = "Any name (chosen with gl_consolefont).", Patterns = [".+"] },
        "skins" => new NamesConfig { Description = "Any name (chosen with skin, teamskin, enemyskin...). base is the fallback.", Names = ["base"], Patterns = [".+"] },
        "sprays" => new NamesConfig { Description = "Any image name (chosen with cl_spray_image, default spray).", Names = ["spray"], Patterns = [".+"] },
        "emoticons" => new NamesConfig
        {
            Description = "Images listed in emoticons.json (the names unezQuake ships), plus emoticons.json itself.",
            Names = ["emoticons", .. EmoticonNames],
        },
        _ => new NamesConfig { Description = "Extra names for this asset type. Names computed from the game data are added automatically." },
    };
}

/// <summary>names/&lt;id&gt;.json</summary>
public sealed class NamesConfig
{
    public string Description { get; set; } = "";
    /// <summary>File names without extension that the client loads.</summary>
    public List<string> Names { get; set; } = [];
    /// <summary>Regular expressions matched against the whole name without extension (any name: ".+").</summary>
    public List<string> Patterns { get; set; } = [];
}
