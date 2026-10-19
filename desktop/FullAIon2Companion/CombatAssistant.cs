using System.Buffers.Binary;
using System.IO;
using System.Text.Json;

namespace FullAIon2Companion;

/// <summary>An effect on a player during a fight: share of the time active and how many times it came.</summary>
public sealed record BuffUptime(int Skill, string Name, string? Icon, double Uptime, int Count, double AverageSeconds = 0);

/// <summary>One tile of the combat assistant bar: a buff on the player or a skill cooling down.</summary>
public sealed record TimerTile(int Skill, string Name, string? Icon, double Left, double Total, bool Buff);

/// <summary>
/// The player's own buffs and skill cooldowns, from the combat stream (2026-10-13, recording of 2026-10-11 03:28):
/// <list type="bullet">
/// <item>0x2A38 effect applied: <c>target | 01 | flag | slot varint | effect u32 | duration ms u32 | …</c>. The effect id
/// over 10 is the skill that gave it (111000071 = 11100007, «Embestida de perdición»); 20 s and 30 s durations lasted
/// exactly that until the removal.</item>
/// <item>0x2B38 effect refreshed: <c>target | flag | slot | effect | duration ms</c>; 0x2C38 removed: <c>target | n | (00 slot
/// reason) × n</c>.</item>
/// <item>0x0138 the server confirms a skill of ours: <c>| 3 bytes | skill u32 | …</c>. Cooldowns are the game's table
/// (data/skill-timers.json); the reuse seen in play (gear and stats shorten it ~8 %) is learned per skill
/// (skill-cooldowns.json). Skills that hits shorten get the time left from the server instead (0x4738).</item>
/// </list>
/// </summary>
public sealed class CombatAssistant
{
    const int Applied = 0x2A38, Updated = 0x2B38, Removed = 0x2C38, OwnSkill = 0x0138, CooldownLeft = 0x4738;
    readonly object gate = new();
    readonly Dictionary<long, (int Skill, DateTime End, double Total)> buffs = [];
    readonly Dictionary<int, (DateTime Start, double Cooldown, double Total)> cooldowns = [];
    // Skills whose cooldown the server reports (0x4738): hits shorten it, so the gaps between casts are not a cooldown to learn.
    readonly HashSet<int> serverTimed = [];
    readonly Dictionary<int, double> learned;
    // Effect intervals on everyone (target, base skill, start, end), for the uptime in the fight details.
    sealed record Interval(int Target, int Skill, DateTime Start, DateTime End);
    readonly List<Interval> intervals = [];
    readonly Dictionary<(int, long), int> open = [];
    static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
    /// <summary>Effects on a player during [from, to]: share of the time each one was active and how many times it came.</summary>
    public IReadOnlyList<BuffUptime> Uptime(int target, DateTime from, DateTime to)
    {
        double span = Math.Max(1, (to - from).TotalSeconds);
        lock (gate)
            return intervals.Where(i => i.Target == target && i.End > from && i.Start < to).GroupBy(i => i.Skill)
                .Select(g =>
                {
                    // Union of the intervals, clipped to the fight.
                    double active = 0; DateTime cursor = from;
                    foreach (var i in g.OrderBy(i => i.Start))
                    {
                        var a = i.Start < cursor ? cursor : i.Start; var b = i.End > to ? to : i.End;
                        if (b > a) { active += (b - a).TotalSeconds; cursor = b; }
                    }
                    var info = Skill(g.Key);
                    return new BuffUptime(g.Key, info?.Name ?? g.Key.ToString(), info?.Icon, Math.Min(100, active / span * 100), g.Count(), g.Average(i => (i.End - i.Start).TotalSeconds));
                })
                .OrderByDescending(b => b.Uptime).ToList();
    }
    /// <summary>The game's cooldown of a class skill (seconds, any variant id), or 0.</summary>
    public static double CooldownSec(int skill) => skills.TryGetValue(BaseOf(skill), out var s) ? s.Cooldown : 0;
    DateTime at;
    static string LearnedPath => Path.Combine(Program.DataDirectory, "skill-cooldowns.json");
    static readonly Dictionary<int, (string Name, double Cooldown, string Icon, string Type)> skills = LoadSkills();
    static Dictionary<int, (string, double, string, string)> LoadSkills()
    {
        var map = new Dictionary<int, (string, double, string, string)>();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "skill-timers.json")));
            foreach (var p in doc.RootElement.EnumerateObject())
                if (int.TryParse(p.Name, out int id))
                    map[id] = (p.Value[0].GetString() ?? "", p.Value[1].GetDouble(), p.Value[2].GetString() ?? "", p.Value[3].GetString() ?? "");
        }
        catch { }
        return map;
    }
    public CombatAssistant()
    {
        try { learned = File.Exists(LearnedPath) ? JsonSerializer.Deserialize<Dictionary<int, double>>(File.ReadAllText(LearnedPath)) ?? [] : []; }
        catch { learned = []; }
    }
    /// <summary>The local player's combat id (0 while unknown), refreshed by the snapshot.</summary>
    public int LocalId { get; set; }
    static int BaseOf(int skill) => skill - skill % 10000;
    /// <summary>Name in the interface language and icon of a class skill (any variant id).</summary>
    public static (string Name, string? Icon)? Skill(int skill)
    {
        int id = BaseOf(skill);
        if (!skills.TryGetValue(id, out var s)) return null;
        string name = L.Language == "es" ? s.Name : AionDPS.Aion2.Protocol.Aion2SkillNames.Display(AionDPS.Aion2.Protocol.Aion2SkillNames.NameOf(id));
        if (name == id.ToString() || name.Length == 0) name = s.Name;
        return (name, s.Icon.Length > 0 ? s.Icon : null);
    }
    double CooldownOf(int id) => learned.TryGetValue(id, out var s) ? s : skills.TryGetValue(id, out var d) ? d.Cooldown : 0;

    public void Add(ReadOnlySpan<byte> frame, DateTime timestamp) { at = timestamp; Frames.Walk(frame, Parse); }
    void Parse(ReadOnlySpan<byte> f)
    {
        if (f.Length < 9) return;
        int opcode = BinaryPrimitives.ReadUInt16BigEndian(f);
        if (opcode == OwnSkill) { Cast(BinaryPrimitives.ReadInt32LittleEndian(f[5..])); return; }
        if (opcode == CooldownLeft) { Left(f); return; }
        if (opcode is not (Applied or Updated or Removed)) return;
        int local = LocalId, p = 2;
        if (!Varint(f, ref p, out long target) || target <= 0) return;
        bool own = local > 0 && target == local;
        if (opcode == Removed)
        {
            int n = f[p++];
            lock (gate)
                for (int i = 0; i < n && p < f.Length; i++)
                {
                    p++; // 00
                    if (!Varint(f, ref p, out long slot)) break;
                    p++; // reason
                    if (own) buffs.Remove(slot);
                    if (open.Remove(((int)target, slot), out int index)) intervals[index] = intervals[index] with { End = Min(intervals[index].End, at) };
                }
            return;
        }
        if (opcode == Applied) { if (f[p] != 1) return; p++; }
        p++; // flag
        if (!Varint(f, ref p, out long id) || p + 8 > f.Length) return;
        int effect = BinaryPrimitives.ReadInt32LittleEndian(f[p..]);
        uint duration = BinaryPrimitives.ReadUInt32LittleEndian(f[(p + 4)..]);
        lock (gate)
        {
            int skill = effect / 10;
            bool classSkill = skills.ContainsKey(BaseOf(skill));
            // Every class-skill effect of 1 s or more on anyone, for the fight's uptime table (procs included).
            if (classSkill && duration is >= 1000 and <= 3_600_000)
            {
                var key = ((int)target, id);
                if (open.TryGetValue(key, out int index) && intervals[index].End > at.AddSeconds(-1)) intervals[index] = intervals[index] with { End = at.AddMilliseconds(duration) };
                else if (opcode == Applied)
                {
                    if (intervals.Count > 60_000) { intervals.RemoveRange(0, 30_000); open.Clear(); }
                    open[key] = intervals.Count;
                    intervals.Add(new((int)target, BaseOf(skill), at, at.AddMilliseconds(duration)));
                }
            }
            if (!own) return;
            // The bar: only buffs from class skills with a real duration: dodges, parries (0.3-5 s), procs every few
            // seconds and permanent states would only flicker.
            if (duration < 4000 || duration > 3_600_000 || !classSkill)
            {
                if (opcode == Applied) buffs.Remove(id);
                return;
            }
            double total = duration / 1000.0;
            if (opcode == Updated && buffs.TryGetValue(id, out var old)) total = Math.Max(old.Total, total);
            else if (opcode == Updated) return;
            buffs[id] = (skill, at.AddMilliseconds(duration), total);
        }
    }
    void Cast(int skill)
    {
        int id = BaseOf(skill);
        lock (gate)
        {
            if (!skills.TryGetValue(id, out var s) || s.Cooldown <= 0) return;
            double cooldown = CooldownOf(id);
            if (cooldowns.TryGetValue(id, out var prev))
            {
                double gap = (at - prev.Start).TotalSeconds;
                if (gap < 1) return; // the same cast, its parts confirmed one by one
                // Used again before the cooldown the app expected: the real one is shorter (cooldown reduction). A much
                // shorter gap is a reset by another skill, not a cooldown.
                if (gap < cooldown - 0.3 && gap >= s.Cooldown * 0.75 && !serverTimed.Contains(id))
                {
                    learned[id] = Math.Round(gap, 1);
                    cooldown = learned[id];
                    try { File.WriteAllText(LearnedPath, JsonSerializer.Serialize(learned)); } catch { }
                }
            }
            // A server-timed skill keeps the server's figure until its next report.
            if (serverTimed.Contains(id) && cooldowns.TryGetValue(id, out var timed) && (at - timed.Start).TotalSeconds < 1) return;
            cooldowns[id] = (at, cooldown, cooldown);
        }
    }
    // 0x4738: | n | (skill u32 | ms left varint) × n - the cooldown left as the server counts it (2026-10-11: "Embestida de
    // perdición" sent every half second while hits shortened it, 1.35 s per 0.53 s; 0 when ready).
    void Left(ReadOnlySpan<byte> f)
    {
        int n = f[2], p = 3;
        lock (gate)
            for (int i = 0; i < n && p + 4 < f.Length; i++)
            {
                int id = BaseOf(BinaryPrimitives.ReadInt32LittleEndian(f[p..])); p += 4;
                if (!Varint(f, ref p, out long ms) || ms < 0 || ms > 3_600_000) return;
                if (!skills.ContainsKey(id)) continue;
                if (serverTimed.Add(id) && learned.Remove(id)) try { File.WriteAllText(LearnedPath, JsonSerializer.Serialize(learned)); } catch { }
                double left = ms / 1000.0;
                double total = cooldowns.TryGetValue(id, out var cd) ? Math.Max(cd.Total, left) : Math.Max(CooldownOf(id), left);
                if (left <= 0 && !cooldowns.ContainsKey(id)) continue;
                cooldowns[id] = (at, left, total);
            }
    }
    /// <summary>The bar now: buffs on the player (shortest first) and skills still cooling down, plus those just ready
    /// (Left = 0 for 2 s, to flash).</summary>
    public (IReadOnlyList<TimerTile> Buffs, IReadOnlyList<TimerTile> Cooldowns) View(DateTime now)
    {
        lock (gate)
        {
            var b = buffs.Values.Where(x => x.End > now)
                .GroupBy(x => BaseOf(x.Skill)).Select(g => g.MaxBy(x => x.End))
                .Select(x => Skill(x.Skill) is { } s ? new TimerTile(BaseOf(x.Skill), s.Name, s.Icon, (x.End - now).TotalSeconds, x.Total, true) : null)
                .OfType<TimerTile>().OrderBy(x => x.Left).Take(12).ToList();
            var c = new List<TimerTile>();
            foreach (var (id, cd) in cooldowns.ToList())
            {
                double left = cd.Cooldown - (now - cd.Start).TotalSeconds;
                if (left < -2) { cooldowns.Remove(id); continue; }
                if (Skill(id) is { } s) c.Add(new(id, s.Name, s.Icon, Math.Max(0, left), cd.Total, false));
            }
            return (b, c.OrderBy(x => x.Left).Take(12).ToList());
        }
    }
    public void Clear() { lock (gate) { buffs.Clear(); cooldowns.Clear(); } }
    static bool Varint(ReadOnlySpan<byte> f, ref int p, out long value)
    {
        value = 0;
        for (int shift = 0; p < f.Length && shift <= 56; shift += 7)
        {
            byte b = f[p++]; value |= (long)(b & 127) << shift;
            if (b < 128) return true;
        }
        return false;
    }
}
