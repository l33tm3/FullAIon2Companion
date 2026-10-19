using System.Buffers.Binary;
using System.IO;
using System.Text.Json;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace FullAIon2Companion;

public sealed record PetDay(int PetId, string Name, int Souls, int Kills, double? SoulsPerKill);
public sealed record PetHunt(PetStatus? Tracked, IReadOnlyList<PetDay> Today, string? LastSoul, DateTime? LastSoulAt, PetStatus? Target = null,
    IReadOnlyList<PetStatus>? Pinned = null, int SessionSouls = 0);
/// <summary>A creature that gives a pet's souls: its catalog name, level and maps.</summary>
public sealed record PetSource(int Npc, string Name, int? Level, string Grade, IReadOnlyList<string> Maps);
public sealed record PetStatus(int PetId, string Name, int Level, int Progress, int Threshold, int Remaining, int Kills, int Souls,
    double? SoulsPerKill, int? KillsLeft, string Source, IReadOnlyList<string> Monsters);

/// <summary>
/// Pet insight ("comprensión") hunting. Progress rises by pet souls, not kills: 0x0D90 <c>| n | n x (pet u32, gained u32)</c>
/// after each soul (+1, +2 for two at once), 0x1490 <c>| 00 00 | pet u32 | level u32</c> on a level-up (progress back to
/// 0). Global thresholds: locked to Lv1 5 souls, Lv1 to Lv2 25, Lv2 to Lv3 75, Lv3 is the cap. The full state (0x0090)
/// only comes at login, so the tracked pet's level and progress can be set by hand and then follow the live frames;
/// kills of the creatures that drop its souls (catalog pets.json) give the drop rate and the kills still to go.
/// </summary>
public sealed class PetTracker
{
    const int Progress = 0x0D90, LevelUp = 0x1490;
    public sealed record Saved(int PetId, int Level, int Progress, int Kills, int Souls);
    /// <summary>Souls and kills per pet for the day (verified live 2026-10-11: 0D 90 | 01 | F7 03 00 00 | 01 00 00 00 =
    /// Kailin +1 right after a Kailin died), so the hunt shows without choosing a pet first.</summary>
    public sealed record Day(DateOnly Date, Dictionary<int, int[]> Pets, int? LastPet, DateTime? LastAt);
    Day day = new(DateOnly.FromDateTime(DateTime.Now), [], null, null);
    static string DayPath => Path.Combine(Program.DataDirectory, "pet-day.json");
    static readonly Dictionary<int, string> petNames = [];
    // Every language the catalog has (en, es, pt…), for NameOf in the interface language.
    static readonly Dictionary<int, Dictionary<string, string>> petNamesAll = [];
    static readonly Dictionary<int, string> petCategory = [];
    static readonly Dictionary<int, int> petOfNpc = [];
    static readonly Dictionary<int, List<int>> npcsOfPet = [];
    readonly object gate = new();
    Saved? tracked;
    string source = "manual";
    static string FilePath => Path.Combine(Program.DataDirectory, "pet-tracker.json");

    static PetTracker()
    {
        try
        {
            var root = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "pets", "pets.json"))).RootElement;
            foreach (var p in root.GetProperty("pets").EnumerateObject())
            {
                petNames[int.Parse(p.Name)] = p.Value.GetProperty("names").TryGetProperty("es", out var es) ? es.GetString()! : p.Value.GetProperty("names").GetProperty("en").GetString()!;
                petNamesAll[int.Parse(p.Name)] = p.Value.GetProperty("names").EnumerateObject().Where(n => n.Value.ValueKind == JsonValueKind.String).ToDictionary(n => n.Name, n => n.Value.GetString()!);
                petCategory[int.Parse(p.Name)] = CategoryNames.GetValueOrDefault(p.Value.TryGetProperty("category", out var c) ? c.GetString() ?? "" : "", "Especial");
            }
            foreach (var m in root.GetProperty("monsters").EnumerateObject())
            {
                int npc = int.Parse(m.Name), pet = m.Value.GetInt32();
                petOfNpc[npc] = pet;
                (npcsOfPet.TryGetValue(pet, out var list) ? list : npcsOfPet[pet] = []).Add(npc);
            }
        }
        catch { }
    }
    public PetTracker()
    {
        try { if (File.Exists(FilePath)) tracked = JsonSerializer.Deserialize<Saved>(File.ReadAllText(FilePath)); } catch { }
        try { if (File.Exists(DayPath) && JsonSerializer.Deserialize<Day>(File.ReadAllText(DayPath)) is { } d && d.Date == DateOnly.FromDateTime(DateTime.Now)) day = d; } catch { }
    }
    /// <summary>Every pet with its Spanish name, for the picker.</summary>
    public static IReadOnlyList<(int Id, string Name)> All => petNames.OrderBy(p => p.Value).Select(p => (p.Key, p.Value)).ToList();
    // The five groups of the pet window, in its order.
    static readonly Dictionary<string, string> CategoryNames = new() { ["Intellect"] = "Intelecto", ["Feral"] = "Salvaje", ["Nature"] = "Naturaleza", ["Trans"] = "Transformación", ["Special"] = "Especial" };
    public static IReadOnlyList<string> Categories => ["Intelecto", "Salvaje", "Naturaleza", "Transformación", "Especial"];
    public static string? NameOf(int petId) => petNamesAll.TryGetValue(petId, out var all) && all.TryGetValue(L.Language, out var local) && local.Length > 0 ? local : petNames.GetValueOrDefault(petId);
    public static string CategoryOf(int petId) => petCategory.GetValueOrDefault(petId, "Especial");
    public static int CountIn(string category) => petCategory.Values.Count(c => c == category);
    /// <summary>Souls gained today per pet.</summary>
    public IReadOnlyDictionary<int, int> SoulsToday() { lock (gate) return day.Date == DateOnly.FromDateTime(DateTime.Now) ? day.Pets.ToDictionary(p => p.Key, p => p.Value[0]) : new Dictionary<int, int>(); }
    public static int Threshold(int level) => level switch { <= 0 => 5, 1 => 25, 2 => 75, _ => 0 };

    /// <summary>Starts following a pet from the level and progress the game window shows; the session counts restart.</summary>
    public void Track(int petId, int level, int progress)
    {
        lock (gate)
        {
            if (!petNames.ContainsKey(petId)) { tracked = null; Save(); return; }
            tracked = new(petId, Math.Clamp(level, 0, 3), Math.Max(0, progress), 0, 0);
            source = "manual";
            Save();
        }
    }
    public void Add(ReadOnlySpan<byte> frame) => Frames.Walk(frame, Parse);
    void Parse(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 3) return;
        int opcode = BinaryPrimitives.ReadUInt16BigEndian(frame);
        lock (gate)
        {
            if (opcode == Progress)
            {
                int count = frame[2];
                for (int i = 0, p = 3; i < count && p + 8 <= frame.Length; i++, p += 8)
                {
                    int pet = BinaryPrimitives.ReadInt32LittleEndian(frame[p..]), gained = BinaryPrimitives.ReadInt32LittleEndian(frame[(p + 4)..]);
                    if (gained is <= 0 or >= 1000 || !petNames.ContainsKey(pet)) continue;
                    if (!SameSoul(pet, fromItem: false)) Count(pet, souls: gained);
                    if (tracked is null || pet != tracked.PetId) continue;
                    tracked = tracked with { Progress = tracked.Progress + gained, Souls = tracked.Souls + gained };
                    source = "juego";
                }
                Save();
            }
            else if (opcode == LevelUp && tracked is not null && frame.Length >= 12 && BinaryPrimitives.ReadInt32LittleEndian(frame[4..]) == tracked.PetId)
            {
                tracked = tracked with { Level = BinaryPrimitives.ReadInt32LittleEndian(frame[8..]), Progress = 0 };
                source = "juego";
                Save();
            }
        }
    }
    /// <summary>A creature died in a fight of ours: counts when it drops the tracked pet's souls.</summary>
    public void Killed(int? npcId)
    {
        lock (gate)
        {
            if (npcId is not int npc || !petOfNpc.TryGetValue(npc, out int pet)) return;
            Count(pet, kills: 1);
            if (tracked is null || pet != tracked.PetId) return;
            tracked = tracked with { Kills = tracked.Kills + 1 };
            Save();
        }
    }
    /// <summary>The tracked pet, taking the game's own state (login frame 0x0090) over the manual one when it came.</summary>
    public PetStatus? Status(IReadOnlyList<Aion2PetState> fromGame)
    {
        lock (gate)
        {
            if (tracked is null) return null;
            if (fromGame.FirstOrDefault(p => p.PetId == tracked.PetId) is { } live && (live.Level != tracked.Level || live.Progress != tracked.Progress))
            { tracked = tracked with { Level = live.Level, Progress = live.Progress }; source = "juego"; Save(); }
            int threshold = Threshold(tracked.Level), remaining = Math.Max(0, threshold - tracked.Progress);
            double? rate = tracked.Kills >= 10 && tracked.Souls > 0 ? (double)tracked.Souls / tracked.Kills : null;
            var monsters = npcsOfPet.GetValueOrDefault(tracked.PetId, []).Select(Catalog.NpcName).Where(n => !n.StartsWith("Criatura ")).Distinct().Take(6).ToList();
            return new(tracked.PetId, petNames[tracked.PetId], tracked.Level, tracked.Progress, threshold, remaining, tracked.Kills, tracked.Souls,
                rate, rate is double r && remaining > 0 ? (int)Math.Ceiling(remaining / r) : null, source, monsters);
        }
    }
    // Called under the lock.
    // A soul shows up twice: the soul item entering the inventory (0x1B56, "Dratona (vinculado) x1") and the pet's
    // progress (0x0D90), which the game does not send for a pet already at Lv3 and sometimes sends seconds later
    // (2026-10-12: Drakan detector, Dratona). Whichever comes first counts; the other one within 8 s is the same soul.
    // One entry per soul not yet paired, so two souls in a row pair one to one.
    readonly Dictionary<int, List<(DateTime At, bool FromItem)>> pendingSoul = [];
    bool SameSoul(int pet, bool fromItem)
    {
        var now = DateTime.Now;
        if (!pendingSoul.TryGetValue(pet, out var list)) pendingSoul[pet] = list = [];
        list.RemoveAll(x => (now - x.At).TotalSeconds > 8);
        int i = list.FindIndex(x => x.FromItem != fromItem);
        if (i >= 0) { list.RemoveAt(i); return true; }
        list.Add((now, fromItem));
        return false;
    }
    /// <summary>An item entered the inventory: a pet soul counts for its pet (see <see cref="SameSoul"/>).</summary>
    public void ItemGained(int item, long quantity)
    {
        if (quantity is <= 0 or > 100 || AionDPS.Aion2.Protocol.Aion2Pets.PetOfSoulItem(item) is not int pet || !petNames.ContainsKey(pet)) return;
        lock (gate)
        {
            if (SameSoul(pet, fromItem: true)) return;
            Count(pet, souls: (int)quantity);
            Save();
        }
    }
    void Count(int pet, int souls = 0, int kills = 0)
    {
        if (day.Date != DateOnly.FromDateTime(DateTime.Now)) day = new(DateOnly.FromDateTime(DateTime.Now), [], null, null);
        var row = day.Pets.TryGetValue(pet, out var r) ? r : day.Pets[pet] = new int[2];
        row[0] += souls; row[1] += kills; sessionSouls += souls;
        if (souls > 0) day = day with { LastPet = pet, LastAt = DateTime.Now };
        try { File.WriteAllText(DayPath, JsonSerializer.Serialize(day)); } catch { }
    }
    /// <summary>The day's hunt: every pet that gave souls or whose creatures died, and the followed pet's progress.</summary>
    /// <summary>
    /// The pet whose souls this creature gives, as the player has it now: level and progress from the game's pet list,
    /// kept live by its own messages (each soul +1/+2, level-ups); souls and kills of today and the kills still to go at
    /// today's rate. Null for creatures that give no souls. Category "?" while the pet list has not arrived yet.
    /// </summary>
    public PetStatus? ForNpc(int? npcId, IReadOnlyList<Aion2PetState> fromGame)
        => npcId is int npc && petOfNpc.TryGetValue(npc, out int pet) ? ForPet(pet, fromGame) : null;
    /// <summary>One pet as the player has it now (see <see cref="ForNpc"/>).</summary>
    public PetStatus? ForPet(int pet, IReadOnlyList<Aion2PetState> fromGame)
    {
        if (!petNames.TryGetValue(pet, out var name)) return null;
        lock (gate)
        {
            var state = fromGame.FirstOrDefault(p => p.PetId == pet);
            int level = state?.Level ?? 0, progress = state?.Progress ?? 0, threshold = Threshold(level), remaining = Math.Max(0, threshold - progress);
            int[] today = day.Date == DateOnly.FromDateTime(DateTime.Now) && day.Pets.TryGetValue(pet, out var r) ? r : [0, 0];
            double? rate = today[1] >= 5 && today[0] > 0 ? (double)today[0] / today[1] : null;
            return new(pet, NameOf(pet) ?? name, level, progress, threshold, remaining, today[1], today[0], rate,
                rate is double k && remaining > 0 ? (int)Math.Ceiling(remaining / k) : null, fromGame.Count > 0 ? CategoryOf(pet) : "?", []);
        }
    }
    int sessionSouls;
    /// <summary>Souls gained since the farm session started (reset with the session).</summary>
    public void ResetSession() { lock (gate) sessionSouls = 0; }
    /// <summary>The creatures that give this pet's souls, from the catalog: name, level, grade and maps.</summary>
    public static IReadOnlyList<PetSource> SourcesOf(int pet) => npcsOfPet.GetValueOrDefault(pet, []).Select(n =>
    {
        var r = Catalog.Npc(n);
        int? level = r is { } x && x.TryGetProperty("level", out var l) && l.TryGetInt32(out int lv) ? lv : null;
        string grade = r is { } y && y.TryGetProperty("gradeName", out var g) ? g.GetString() ?? "" : "";
        var maps = r is { } z && z.TryGetProperty("maps", out var m) && m.ValueKind == JsonValueKind.Array ? m.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : e.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "").Where(t => t.Length > 0).Select(t => string.Join(' ', t.Split('_', '-').Select(w => w.Length > 0 ? char.ToUpperInvariant(w[0]) + w[1..] : w))).Distinct().Take(3).ToList() : new List<string>();
        return new PetSource(n, Catalog.NpcName(n), level, grade, maps);
    }).Where(s => !s.Name.StartsWith("Criatura ")).GroupBy(s => (s.Name, s.Level)).Select(g => g.First()).OrderBy(s => s.Level ?? 999).Take(12).ToList();
    public PetHunt Hunt(IReadOnlyList<Aion2PetState> fromGame, int? targetNpc = null, IReadOnlyCollection<int>? pinned = null)
    {
        var followed = Status(fromGame);
        var target = ForNpc(targetNpc, fromGame);
        var pins = (pinned ?? []).Select(p => ForPet(p, fromGame)).OfType<PetStatus>().ToList();
        lock (gate)
        {
            var rows = day.Pets.Where(p => petNames.ContainsKey(p.Key) && (p.Value[0] > 0 || p.Value[1] > 0))
                .Select(p => new PetDay(p.Key, petNames[p.Key], p.Value[0], p.Value[1], p.Value[1] >= 5 && p.Value[0] > 0 ? (double)p.Value[0] / p.Value[1] : null))
                .OrderByDescending(r => r.Souls).ThenByDescending(r => r.Kills).ToList();
            return new(followed, rows, day.LastPet is int last && petNames.TryGetValue(last, out var n) ? n : null, day.LastAt, target, pins, sessionSouls);
        }
    }
    void Save() { try { File.WriteAllText(FilePath, JsonSerializer.Serialize(tracked)); } catch { } }
}
