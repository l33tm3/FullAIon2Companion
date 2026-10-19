using System.Buffers.Binary;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FullAIon2Companion;

/// <summary>
/// Learns when each boss enrages. The game's effect table names a few dozen "Boss Enrage" / "Enrage" / "Berserk"
/// effects; when one of them lands on a creature (a 0x2A38 / 0x2B38 effect frame naming that creature first and the
/// effect id somewhere after), the seconds since the group's first hit on it are kept per NPC (enrage.json). From the
/// next fight on, the boss panel counts down to it and projects the health left at that moment, like the game's own
/// enrage timer. Nothing is guessed: a boss never seen enraging shows no countdown.
/// </summary>
public sealed class EnrageWatch
{
    const int Applied = 0x2A38, Updated = 0x2B38;
    static readonly HashSet<uint> Effects = LoadEffects(@"\b(enrage|berserk)");
    // Diagnostics (--enrage-scan): every effect with a rage-like name, to find which ones bosses really get.
    static readonly HashSet<uint> RageLike = LoadEffects(@"enrage|berserk|frenzy|fury|\brage\b|wrath");
    public List<(int Entity, uint Effect, DateTime At, int Opcode)>? ScanLog { get; set; }
    readonly Dictionary<int, double> enrageAfter;
    readonly Dictionary<int, DateTime> seen = [];
    readonly object gate = new();
    static string FilePath => Path.Combine(Program.DataDirectory, "enrage.json");
    DateTime at;

    public EnrageWatch()
    {
        try { enrageAfter = File.Exists(FilePath) ? JsonSerializer.Deserialize<Dictionary<int, double>>(File.ReadAllText(FilePath)) ?? [] : []; }
        catch { enrageAfter = []; }
        // Known enrage timers (seconds after the first hit), from the other meter's countdown: Kromede's Desire
        // ("Ambición de Kromede", 2300371) enrages at 5:00 (2026-10-11: 3:59 fought + 1:01 left).
        foreach (var (npc, seconds) in Known) enrageAfter.TryAdd(npc, seconds);
    }
    static readonly (int Npc, double Seconds)[] Known = [(2300371, 300), (2300218, 300)]; // Kromede; Auldor sagrado (0x218D, 2026-10-12)
    static HashSet<uint> LoadEffects(string pattern)
    {
        var set = new HashSet<uint>();
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "skills", "buff_names_en.json");
            foreach (var p in JsonDocument.Parse(File.ReadAllText(path)).RootElement.EnumerateObject())
                if (p.Value.GetString() is { } name && Regex.IsMatch(name, pattern, RegexOptions.IgnoreCase))
                    set.Add(Convert.ToUInt32(p.Name, 16));
        }
        catch { }
        return set;
    }
    /// <summary>Seconds after the first hit at which this NPC enraged last time, if ever seen.</summary>
    public double? EnrageAfter(int npc) { lock (gate) return enrageAfter.TryGetValue(npc, out var s) ? s : null; }
    readonly Dictionary<int, DateTime> enrageAtClock = [];
    double serverSkew; // seconds the game server's clock runs ahead of this PC (learned from 0x218D)
    /// <summary>The moment the server says this boss enrages (from its 0x4136 record), local clock.</summary>
    public DateTime? EnrageAtClock(int entity) { lock (gate) return enrageAtClock.TryGetValue(entity, out var t) ? t : null; }
    /// <summary>When this entity got an enrage effect in this session.</summary>
    public DateTime? EnragedAt(int entity) { lock (gate) return seen.TryGetValue(entity, out var t) ? t : null; }
    public void Add(ReadOnlySpan<byte> frame, DateTime timestamp) { at = timestamp; Frames.Walk(frame, Parse); }
    /// <summary>Live sessions: the NPC id of an entity, and where to log every rage-like effect or cast on creatures
    /// (enrage-candidates.log), to learn which ones each boss really uses.</summary>
    public Func<int, int?>? NpcOf { get; set; }
    public string? CandidateLog { get; set; }
    /// <summary>Live sessions: the entity's health in percent, for the candidate log.</summary>
    public Func<int, double?>? HpPctOf { get; set; }
    readonly HashSet<int> fights = [];
    /// <summary>Logs the first hit of a boss fight once, so the log shows the whole timeline.</summary>
    public void NoteFight(int entity, int npc, DateTime first)
    {
        if (CandidateLog is null) return;
        lock (gate) if (!fights.Add(entity)) return;
        try { File.AppendAllText(CandidateLog, $"{first:yyyy-MM-dd HH:mm:ss.fff}\tfirst-hit\tnpc={npc}\t{Catalog.NpcName(npc)}\tentity={entity}\n"); } catch { }
    }
    readonly HashSet<(int, uint)> logged = [];
    void Candidate(int entity, uint id, string kind, string? name)
    {
        if (CandidateLog is null || NpcOf?.Invoke(entity) is not int npc || !logged.Add((entity, id))) return;
        try { File.AppendAllText(CandidateLog, $"{at:yyyy-MM-dd HH:mm:ss.fff}\t{kind}\tnpc={npc}\t{Catalog.NpcName(npc)}\tentity={entity}\tid={id}\t{name}\thp={(HpPctOf?.Invoke(entity) is double hp ? hp.ToString("0.0") + "%" : "?")}\n"); } catch { }
    }
    static readonly Regex RageName = new(@"enrage|berserk|frenzy|fury|\brage\b|wrath", RegexOptions.IgnoreCase);
    static readonly Regex EnrageName = new(@"\b(enrage|berserk)", RegexOptions.IgnoreCase);
    void Parse(ReadOnlySpan<byte> f)
    {
        if (f.Length < 7) return; // 0x218D out-of-combat frames are 7 bytes
        int opcode = BinaryPrimitives.ReadUInt16BigEndian(f);
        if (opcode == 0x218D)
        {
            // A creature's combat state: entity varint | 00 00 out of combat | 00 01 in combat | 01 01 + the server's Unix
            // time in ms when this encounter enrages (2026-10-12 Auldor sagrado: 302.78 s after the frame on both pulls, i.e.
            // 5:00 plus the server clock running 2.78 s ahead; "Frenzy" landed on it at that moment). Leaving combat without
            // dying resets the boss: its enrage moment is dropped until the next pull brings a new one.
            int q = 2; long who = 0;
            for (int shift = 0; q < f.Length && shift <= 28; shift += 7) { byte b = f[q++]; who |= (long)(b & 127) << shift; if (b < 128) break; }
            if (who <= 0 || q + 2 > f.Length) return;
            if (f[q] == 0 && f[q + 1] == 0) { lock (gate) { enrageAtClock.Remove((int)who); seen.Remove((int)who); } return; }
            if (f[q] != 1 || q + 10 > f.Length) return;
            long ms = BinaryPrimitives.ReadInt64LittleEndian(f[(q + 2)..]);
            if (ms <= 0) return;
            var server = DateTimeOffset.FromUnixTimeMilliseconds(Math.Clamp(ms, 0, 253_402_300_799_000)).LocalDateTime;
            double ahead = (server - at).TotalSeconds;
            if (ahead is < 5 or > 3600) return;
            // Enrage timers are whole half-minutes: what is left over is how far the server's clock runs ahead of this PC.
            double rounded = Math.Round(ahead / 30) * 30, skew = ahead - rounded;
            if (Math.Abs(skew) < 10) lock (gate) serverSkew = skew;
            lock (gate) { enrageAtClock[(int)who] = server.AddSeconds(-serverSkew); seen.Remove((int)who); }
            // Kept per boss too, for a later fight whose pull frame is missed (the app started mid-fight).
            if (NpcOf?.Invoke((int)who) is int npc && Math.Abs(skew) < 10) Learn(npc, rounded);
            Candidate((int)who, 0x218D, "combat", $"enrage at {server:HH:mm:ss.fff} (server), {ahead:0.00} s ahead");
            return;
        }
        if (opcode == 0x4136)
        {
            // A boss's full record (sent as the fight starts) ends with its gauge section: 36 00 00 00 | 03 01 | max u32 |
            // current u32 | 4 bytes | 00 | a Unix time in ms. On 2026-10-12 (Auldor sagrado) that time was 4:48 after the first
            // hit, 5:00 after the fight began, like Kromede's 5:00 enrage: it is taken as the moment the boss enrages.
            int q = 2; long who = 0;
            for (int shift = 0; q < f.Length && shift <= 28; shift += 7) { byte b = f[q++]; who |= (long)(b & 127) << shift; if (b < 128) break; }
            int mark = f.LastIndexOf((ReadOnlySpan<byte>)[0x36, 0x00, 0x00, 0x00, 0x03, 0x01]);
            if (who <= 0 || mark < 0 || mark + 27 > f.Length) return;
            long ms = BinaryPrimitives.ReadInt64LittleEndian(f[(mark + 19)..]);
            var when = DateTimeOffset.FromUnixTimeMilliseconds(Math.Clamp(ms, 0, 253_402_300_799_000)).LocalDateTime;
            if (Math.Abs((when - at).TotalMinutes) > 60) return; // not a time near now: another layout
            lock (gate) enrageAtClock.TryAdd((int)who, when.AddSeconds(-serverSkew)); // a 0x218D pull time wins
            Candidate((int)who, 0x4136, "record", $"enrage at {when:HH:mm:ss.fff}");
            return;
        }
        if (opcode == 0x0938)
        {
            // A monster skill cast: caster varint | flag | skill u32 (BossCasts). A boss casting "Enrage" / "Berserk" enrages.
            int q = 2; long caster = 0;
            for (int shift = 0; q < f.Length && shift <= 28; shift += 7) { byte b = f[q++]; caster |= (long)(b & 127) << shift; if (b < 128) break; }
            if (caster <= 0 || q + 5 > f.Length) return;
            int skill = BinaryPrimitives.ReadInt32LittleEndian(f[(q + 1)..]);
            string english = AionDPS.Aion2.Protocol.Aion2SkillNames.NameOf(skill);
            if (english == skill.ToString() || !RageName.IsMatch(english)) return;
            Candidate((int)caster, (uint)skill, "cast", english);
            ScanLog?.Add(((int)caster, (uint)skill, at, opcode));
            if (EnrageName.IsMatch(english)) lock (gate) seen.TryAdd((int)caster, at);
            return;
        }
        if (f.Length < 12 || Effects.Count == 0) return;
        if (opcode is not (Applied or Updated)) return;
        int p = 2; long entity = 0;
        for (int shift = 0; p < f.Length && shift <= 28; shift += 7) { byte b = f[p++]; entity |= (long)(b & 127) << shift; if (b < 128) break; }
        if (ScanLog is { } log)
            for (int i = p; i + 4 <= f.Length; i++)
                if (RageLike.Contains(BinaryPrimitives.ReadUInt32LittleEndian(f[i..]))) { log.Add(((int)entity, BinaryPrimitives.ReadUInt32LittleEndian(f[i..]), at, opcode)); break; }
        if (CandidateLog is not null)
            for (int i = p; i + 4 <= f.Length && i < p + 24; i++)
                if (RageLike.Contains(BinaryPrimitives.ReadUInt32LittleEndian(f[i..]))) { Candidate((int)entity, BinaryPrimitives.ReadUInt32LittleEndian(f[i..]), "effect", null); break; }
        for (int i = p; i + 4 <= f.Length && i < p + 24; i++)
            if (Effects.Contains(BinaryPrimitives.ReadUInt32LittleEndian(f[i..])))
            {
                lock (gate) seen.TryAdd((int)entity, at);
                return;
            }
    }
    /// <summary>Keeps how long the fight on this NPC lasted before it enraged (called once the snapshot sees it).</summary>
    public void Learn(int npc, double seconds)
    {
        if (seconds < 15 || seconds > 3600) return;
        lock (gate)
        {
            if (enrageAfter.TryGetValue(npc, out var known) && Math.Abs(known - seconds) < 2) return;
            enrageAfter[npc] = Math.Round(seconds, 1);
            try { File.WriteAllText(FilePath, JsonSerializer.Serialize(enrageAfter)); } catch { }
        }
    }
}
