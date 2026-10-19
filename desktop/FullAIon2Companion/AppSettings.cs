using System.IO;
using System.Text.Json;

namespace FullAIon2Companion;
public sealed class AppSettings
{
    public bool Sounds { get; set; }
    public double Volume { get; set; } = .35;
    public int LeadMinutes { get; set; } = 3;
    public int RegionOffset { get; set; } = -180;
    public HashSet<string> SelectedEvents { get; set; } = ["rift", "beritra", "boss-watcher-kaira", "defend-shugo"];
    public HashSet<string> SentAlerts { get; set; } = [];
    public int FarmGoal { get; set; } = 20;
    public bool ShareTelemetry { get; set; }
    /// <summary>Upload each character's pet collection to the paired account.</summary>
    public bool SyncPets { get; set; } = true;
    /// <summary>Voluntary diagnostics to the account (version, channel, capture and sync state; nothing else). Off by default.</summary>
    public bool ShareDiagnostics { get; set; }
    /// <summary>Update channel: "stable" or "beta".</summary>
    public string UpdateChannel { get; set; } = "beta";
    /// <summary>Announcements already shown as a notification (shown once each).</summary>
    public HashSet<string> SeenAnnouncements { get; set; } = [];
    public double OverlayOpacity { get; set; } = .82;
    public double? OverlayLeft { get; set; }
    public double? OverlayTop { get; set; }
    public bool OverlayVisible { get; set; }
    public bool BossSounds { get; set; } = true;
    public bool OverlayDrops { get; set; } = true;
    // Configuración > Combate
    public int CombatIdleSeconds { get; set; } = 15;
    public bool PartyOnly { get; set; } = true;
    /// <summary>In a troop: the whole troop (true) or only the own group (false), in the roster and the damage ranking.</summary>
    public bool OverlayTroopAll { get; set; } = true;
    public bool BossOnlyDamage { get; set; }
    // Configuración > Pantalla
    public double OverlayScale { get; set; } = 1;
    public bool OverlayShowParty { get; set; } = true;
    public bool OverlayShowCasts { get; set; } = true;
    public bool OverlayShowEvent { get; set; } = true;
    /// <summary>On: the overlay belongs to the game window - above AION 2, below any other app brought to the front,
    /// hidden with the game minimized. Off: always on top of every window.</summary>
    public bool OverlayAttachToGame { get; set; } = true;
    /// <summary>Where a boss skill warning goes: "center" (big, mid-screen), "overlay" (banner in the overlay) or "both".</summary>
    public string CastAlertPlace { get; set; } = "center";
    /// <summary>Pets pinned as cards under the overlay (Mascotas tab).</summary>
    public List<int> PinnedPets { get; set; } = [];
    /// <summary>The farm session block in the overlay: time, kills, souls and their rates.</summary>
    public bool OverlayShowFarm { get; set; } = true;
    /// <summary>The loot HUD under the farm block: the last items gained with their Market price (Mercado tab).</summary>
    public bool OverlayShowLootPrices { get; set; } = true;
    /// <summary>The server the Mercado tab shows and prices (0: the character's). The loot HUD always uses the character's.</summary>
    public int MarketServer { get; set; }
    /// <summary>The Abyss ranking line in the overlay (class and all classes, from the Clasificación tab).</summary>
    public bool OverlayShowRanking { get; set; } = true;
    /// <summary>Countdown chips under the overlay header: next field boss, rift and Shugo.</summary>
    public bool OverlayShowHeaderTimers { get; set; } = true;
    /// <summary>The overlay's style (OverlayWindow.Styles): «clasico», the one there was, by default.</summary>
    public string OverlayStyle { get; set; } = "clasico";
    /// <summary>The blocks folded in the Módulos structures (combate, grupo, mascotas).</summary>
    public HashSet<string> OverlayFolded { get; set; } = [];
    // Combat assistant bar (buffs and cooldowns)
    // Hidden cubes: the bar over the overlay and the sound when the server announces one
    public bool CubeAlerts { get; set; } = true;
    public bool CubeSound { get; set; } = true;
    /// <summary>News already seen (an update's version, the site's notices' ids): the strip's dot and the toast skip them.</summary>
    public List<string> NewsSeen { get; set; } = [];
    public bool AssistantVisible { get; set; } = true;
    public bool AssistantShowBuffs { get; set; } = true;
    public bool AssistantShowCooldowns { get; set; } = true;
    public bool AssistantSound { get; set; }
    public double AssistantScale { get; set; } = 1;
    public double? AssistantLeft { get; set; }
    public double? AssistantTop { get; set; }
    // Farm objective: kills, kinah or loot value, optionally within a time limit ("reto", minutes; 0 = none)
    public string GoalKind { get; set; } = "kills";
    public long GoalTarget { get; set; }
    public int GoalMinutes { get; set; }
    /// <summary>Field bosses marked with a star (NPC ids), whether they announce, and how many minutes before.</summary>
    public HashSet<int> FieldBossStars { get; set; } = [];
    public bool FieldBossAlerts { get; set; } = true;
    public int FieldBossLeadMinutes { get; set; } = 5;
    /// <summary>Columns shown per skill in the fight details (hits, crit, back, front, perfect, double, multi, avg, max).</summary>
    public HashSet<string> BreakdownColumns { get; set; } = ["hits", "crit", "avg", "max"];
    /// <summary>Interface language: "es", "en" or "pt" (Brazilian Portuguese).</summary>
    public string Language { get; set; } = "es";
    /// <summary>The farm block folded to one line (click its title in move mode).</summary>
    public bool FarmCollapsed { get; set; }
    /// <summary>Which casters warn, for the visual warning and for the sound: "dungeon" (bosses in a dungeon or instance),
    /// "field" (field and world bosses), "elite" (elite monsters), "target" (whatever monster is the target).</summary>
    public HashSet<string> CastBannerKinds { get; set; } = ["dungeon", "field", "target"];
    public HashSet<string> CastSoundKinds { get; set; } = ["dungeon", "field", "target"];
    /// <summary>Event notifications in a corner of the game window, and which corner ("top-right", "top-left", "bottom-right", "bottom-left").</summary>
    public bool EventToasts { get; set; } = true;
    public string EventToastCorner { get; set; } = "top-right";
    /// <summary>The boss card (group DPS, time to kill, enrage): "overlay", "window" (under the game's boss bar) or "both".</summary>
    public string BossPacePlace { get; set; } = "both";
    /// <summary>The boss window also draws the health bar (with the enrage tick) and the enrage timer bar.</summary>
    public bool BossPaceBars { get; set; } = true;
    public double? BossPaceLeft { get; set; }
    public double? BossPaceTop { get; set; }
    /// <summary>The boss-skill warning sound: "es" (voice, ¡Cuidado!), "en" (voice, English) or "alarm" (the klaxon).</summary>
    public string BossAlertVoice { get; set; } = "es";
    /// <summary>The own character's server, for profile lookups of players whose server the game did not name
    /// (2401 = Israphel, the server of the configured character; updated whenever the game names it).</summary>
    public int LastServerId { get; set; } = 2401;
    // Minimap (atlas tiles around the player)
    public bool MinimapVisible { get; set; } = true;
    public bool OverlayShowPet { get; set; } = true;
    public int OdMax { get; set; } = 840;
    public bool OverlayShowOd { get; set; } = true;
    public double? MinimapLeft { get; set; }
    public double? MinimapTop { get; set; }
    public double MinimapZoom { get; set; } = 1;
    public double MinimapWidth { get; set; } = 280;
    /// <summary>Minimap background opacity and the size of its markers (1 = normal).</summary>
    public double MinimapOpacity { get; set; } = .94;
    public double MinimapIconScale { get; set; } = 1;
    // Breakdown window (per-skill analysis), where it was left.
    public double? BreakdownLeft { get; set; }
    public double? BreakdownTop { get; set; }
    public double BreakdownWidth { get; set; } = 820;
    public double BreakdownHeight { get; set; } = 560;
    public double MinimapHeight { get; set; } = 330;
    /// <summary>Minimap layers shown: tropa, vivos (live creatures), jefes, cubos, minerales, plantas, od, rastros, mascotas, lugares, servicios, pnj, zonas, misiones, criaturas (atlas spawns).</summary>
    public List<string> MinimapFilters { get; set; } = ["tropa", "vivos", "jefes", "cubos", "minerales", "plantas", "od"];
    public bool MinimapFiltersOpen { get; set; } = true;
    // Configuración > Atajos: action -> "Ctrl+Alt+F" ("" = sin definir)
    public Dictionary<string, string> Hotkeys { get; set; } = new() { ["overlay"] = "Ctrl+Alt+F", ["move"] = "Ctrl+Alt+M", ["drops"] = "Ctrl+Alt+B", ["panel"] = "Ctrl+Alt+P", ["reset"] = "", ["minimap"] = "Ctrl+Alt+N", ["breakdown"] = "Ctrl+Alt+D" };
    public static string FilePath => Path.Combine(Program.DataDirectory, "settings.json");
    public static AppSettings Load()
    {
        AppSettings settings;
        try { settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new(); } catch { settings = new(); }
        // Shortcuts added in a later version get their default; ones the user cleared ("") stay cleared.
        foreach (var (action, combo) in new AppSettings().Hotkeys) settings.Hotkeys.TryAdd(action, combo);
        return settings;
    }
    public void Save() => File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
}
