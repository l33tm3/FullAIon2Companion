using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.Json;

namespace FullAIon2Companion;

/// <summary>One member of the party or troop. ClassCode as the game codes it (8 gladiator, 11 templar, 35 chanter …).</summary>
public sealed record TroopMember(uint Key, string Name, int ServerId, int Group, int Slot, int ClassCode, int Level, long Hp, string? Legion,
    int GearScore = 0, long CombatPower = 0, float? X = null, float? Y = null, DateTime? SeenAt = null)
{
    static readonly string[] Classes = ["gladiator", "templar", "ranger", "assassin", "elementalist", "sorcerer", "cleric", "chanter"];
    public string? ClassKey => ClassCode >= 5 && (ClassCode - 5) / 4 < Classes.Length ? Classes[(ClassCode - 5) / 4] : null;
}
/// <summary>The groups the local player belongs to: one (a party) or up to four (a troop). OwnGroup is the player's.</summary>
public sealed record TroopInfo(bool IsTroop, int OwnGroup, IReadOnlyList<TroopMember> Members);

/// <summary>
/// Party and troop ("tropa") rosters, decoded from a 2026-10-11 troop of four groups (Krao / Isla Inmortal, 17:29–17:36):
/// <list type="bullet">
/// <item>0x0296 <c>| 00 | troop id (8) | leader key u32 | 00 00 | server | 00 | capacity u32 (20) | 02 01 02 01 | own group id (8) | group no. | …</c>: the troop.</item>
/// <item>0x1D96 <c>| group id (8) | group no. | capacity u32 | …</c> and 0x0092 <c>| 00 | group id (8) | …</c>: a group's roster.</item>
/// <item>0x0A96 <c>| group id (8) | flag | slot | …</c>: one member joined that group.</item>
/// <item>0x2296 <c>| key u32 | 00 00 | server | group id (8) | …</c>: a member left; 0x2396 <c>| group id (8)</c>: a group left the troop.</item>
/// <item>0x0C96 <c>00 00</c>: the local player left (party or troop): everything goes.</item>
/// <item>0x2C96 <c>| key u32 | 00 00 | server | class u32 | … | level u32 | u32 | gear score u32 | legion | u32 | combat power u32</c>: a member's details.</item>
/// </list>
/// A member entry is <c>24 | account GUID (36) | key u32 | 00 00 | server | len | name | class u32 | hp varint | max hp varint | …</c>
/// (checked: Darkni 8 = gladiator, 26 403 HP as the game showed; Odo 35 = chanter; Eridamus 11 = templar).
/// </summary>
public sealed class TroopRoster
{
    const int Troop = 0x0296, GroupList = 0x1D96, OwnList = 0x0092, Joined = 0x0A96, Left = 0x2296, GroupLeft = 0x2396, YouLeft = 0x0C96, Detail = 0x2C96, Status = 0x1C92, Groups = 0x1E96;
    sealed record Group(int Number, Dictionary<uint, TroopMember> Members);
    public sealed record Saved(DateTime At, string? Own, Dictionary<string, int> Numbers, List<TroopMember> Members, Dictionary<uint, int[]> Details);
    readonly Dictionary<string, Group> groups = [];
    // Class, gear score and combat power by member key; 2C96 comes often and for members of any group.
    readonly Dictionary<uint, int[]> details = [];
    // World position by member key (2C 96 and 1C 92 carry it every second or two): the minimap shows the troop.
    readonly Dictionary<uint, (float X, float Y, DateTime At)> positions = [];
    readonly object gate = new();
    string? ownGroup;
    bool troop;
    static string FilePath => Path.Combine(Program.DataDirectory, "troop.json");

    public TroopRoster()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<Saved>(File.ReadAllText(FilePath)) is { } s && DateTime.Now - s.At < TimeSpan.FromHours(2))
            {
                ownGroup = s.Own;
                foreach (var (id, n) in s.Numbers) groups[id] = new(n, []);
                foreach (var m in s.Members) if (groups.FirstOrDefault(g => g.Value.Number == m.Group).Value is { } g) g.Members[m.Key] = m;
                foreach (var (k, v) in s.Details) details[k] = v;
                troop = groups.Count > 1;
            }
        }
        catch { }
    }
    /// <summary>The roster now, or null outside any party.</summary>
    public TroopInfo? Current
    {
        get
        {
            lock (gate)
            {
                if (ownGroup is null || !groups.TryGetValue(ownGroup, out var own) || groups.Values.Sum(g => g.Members.Count) == 0) return null;
                var all = groups.Values.SelectMany(g => g.Members.Values).Select(WithDetails).OrderBy(m => m.Group).ThenBy(m => m.Slot).ToList();
                return new(troop || groups.Count > 1, own.Number, all);
            }
        }
    }
    TroopMember WithDetails(TroopMember m)
    {
        if (details.TryGetValue(m.Key, out var d)) m = m with { ClassCode = m.ClassCode > 0 ? m.ClassCode : d[0], Level = d[1] > 0 ? d[1] : m.Level, GearScore = d[2], CombatPower = d[3] };
        return positions.TryGetValue(m.Key, out var p) ? m with { X = p.X, Y = p.Y, SeenAt = p.At } : m;
    }

    public void Add(ReadOnlySpan<byte> frame) => Frames.Walk(frame, Parse);
    void Parse(ReadOnlySpan<byte> f)
    {
        if (f.Length < 4) return;
        int opcode = BinaryPrimitives.ReadUInt16BigEndian(f);
        lock (gate)
        {
            switch (opcode)
            {
                case YouLeft when f.Length == 4 && f[2] == 0 && f[3] == 0:
                    groups.Clear(); ownGroup = null; troop = false; Save(); return;
                case Troop when f.Length > 40:
                {
                    // The own group's id and number follow the 02 01 02 01 marker.
                    int at = f.IndexOf((ReadOnlySpan<byte>)[0x02, 0x01, 0x02, 0x01]);
                    if (at < 0 || at + 13 > f.Length) return;
                    string id = Convert.ToHexString(f.Slice(at + 4, 8));
                    int number = f[at + 12];
                    if (number is < 1 or > 8) return;
                    troop = true;
                    SetGroup(id, number, Entries(f[(at + 13)..], number), replace: true);
                    ownGroup = id;
                    Save(); return;
                }
                case GroupList when f.Length > 20:
                {
                    string id = Convert.ToHexString(f.Slice(2, 8));
                    int number = f[10];
                    if (number is < 1 or > 8) return;
                    troop = true;
                    SetGroup(id, number, Entries(f[11..], number), replace: false);
                    Save(); return;
                }
                case OwnList when f.Length > 60:
                {
                    string id = Convert.ToHexString(f.Slice(3, 8));
                    int number = groups.TryGetValue(id, out var known) ? known.Number : 1;
                    var members = Entries(f[11..], number);
                    if (members.Count == 0) return;
                    SetGroup(id, number, members, replace: true);
                    ownGroup = id;
                    Save(); return;
                }
                case Joined when f.Length > 60:
                {
                    string id = Convert.ToHexString(f.Slice(2, 8));
                    // Joins of other parties (party finder lists) also come as 0A 96: only groups we know count.
                    if (!groups.TryGetValue(id, out var g)) return;
                    foreach (var m in Entries(f[10..], g.Number)) { Unique(m.Key, id); g.Members[m.Key] = m; }
                    Save(); return;
                }
                case Left when f.Length >= 18:
                {
                    uint key = BinaryPrimitives.ReadUInt32LittleEndian(f[2..]);
                    string id = Convert.ToHexString(f.Slice(10, 8));
                    if (groups.TryGetValue(id, out var g) && g.Members.Remove(key)) Save();
                    return;
                }
                case GroupLeft when f.Length >= 10:
                {
                    string id = Convert.ToHexString(f.Slice(2, 8));
                    if (id != ownGroup && groups.Remove(id)) { troop = groups.Count > 1; Save(); }
                    return;
                }
                case Groups when f.Length > 30:
                {
                    // 1E 96 | number of groups | (group id (8) | group no. | capacity u32 | count | member entries)…:
                    // the full roster of the groups a change touched (a member moving from group 3 to 2 sends both).
                    // Seen 2026-10-11 18:20–19:04 on every move; without it the moved member stayed in both groups.
                    var suffix = f.Slice(6, 5).ToArray();
                    var heads = new List<(int At, string Id, int Number)>();
                    for (int i = 3; i + 17 <= f.Length; i++)
                        if (f.Slice(i + 3, 5).SequenceEqual(suffix) && f[i + 8] is >= 1 and <= 8 && BinaryPrimitives.ReadInt32LittleEndian(f[(i + 9)..]) is >= 1 and <= 8)
                        { heads.Add((i, Convert.ToHexString(f.Slice(i, 8)), f[i + 8])); i += 13; }
                    for (int k = 0; k < heads.Count; k++)
                    {
                        int from = heads[k].At + 14, to = k + 1 < heads.Count ? heads[k + 1].At : f.Length;
                        var members = Entries(f[from..to], heads[k].Number);
                        if (members.Count == 0 && f[heads[k].At + 13] != 0) continue;
                        troop = true;
                        SetGroup(heads[k].Id, heads[k].Number, members, replace: true);
                    }
                    Save(); return;
                }
                case Detail when f.Length > 60:
                    Position(f); ParseDetail(f); return;
                case Status when f.Length > 60:
                    Position(f); return;
            }
        }
    }
    void SetGroup(string id, int number, List<TroopMember> members, bool replace)
    {
        // A group number belongs to one group: a new id with that number replaces the old one.
        foreach (var stale in groups.Where(g => g.Key != id && g.Value.Number == number).Select(g => g.Key).ToList()) groups.Remove(stale);
        if (!groups.TryGetValue(id, out var g) || g.Number != number) groups[id] = g = new(number, g?.Members ?? []);
        if (replace) g.Members.Clear();
        foreach (var m in members) { Unique(m.Key, id); g.Members[m.Key] = m with { Group = number }; }
    }
    // One member, one group: whoever the game just placed somewhere left wherever we had him.
    void Unique(uint key, string keep)
    {
        foreach (var (id, g) in groups) if (id != keep) g.Members.Remove(key);
    }
    // 2C 96 | key | 00 00 | server | class u32 | u16 | 6 bytes | map u32 | x y z | level u32 | u32 | gear score u32 | len legion | u32 | combat power u32
    // Same head in 2C 96 and 1C 92: | key | 00 00 | server | class u32 | u16 | 6 bytes | map u32 | x y z (floats at 26).
    void Position(ReadOnlySpan<byte> f)
    {
        uint key = BinaryPrimitives.ReadUInt32LittleEndian(f[2..]);
        float x = BinaryPrimitives.ReadSingleLittleEndian(f[26..]), y = BinaryPrimitives.ReadSingleLittleEndian(f[30..]);
        if (!float.IsFinite(x) || !float.IsFinite(y) || Math.Abs(x) > 1_000_000 || Math.Abs(y) > 1_000_000 || Math.Abs(x) + Math.Abs(y) < 100) return;
        positions[key] = (x, y, DateTime.Now);
        if (positions.Count > 500) positions.Clear();
    }
    void ParseDetail(ReadOnlySpan<byte> f)
    {
        uint key = BinaryPrimitives.ReadUInt32LittleEndian(f[2..]);
        int cls = BinaryPrimitives.ReadInt32LittleEndian(f[10..]);
        int p = 10 + 4 + 2 + 6 + 4 + 12;
        if (p + 13 > f.Length || cls is < 1 or > 60) return;
        int level = BinaryPrimitives.ReadInt32LittleEndian(f[p..]);
        int gear = BinaryPrimitives.ReadInt32LittleEndian(f[(p + 8)..]);
        int legion = f[p + 12];
        int q = p + 13 + legion + 4;
        if (level is < 1 or > 100 || gear is < 0 or > 20000 || q + 4 > f.Length) return;
        long power = BinaryPrimitives.ReadUInt32LittleEndian(f[q..]);
        details[key] = [cls, level, gear, (int)Math.Min(power, int.MaxValue)];
    }
    /// <summary>Every member entry (GUID-anchored) in a roster span.</summary>
    static List<TroopMember> Entries(ReadOnlySpan<byte> f, int group)
    {
        var list = new List<TroopMember>();
        for (int i = 0; i + 50 < f.Length; i++)
        {
            if (f[i] != 0x24 || !IsGuid(f.Slice(i + 1, 36))) continue;
            uint key = BinaryPrimitives.ReadUInt32LittleEndian(f[(i + 37)..]);
            int server = BinaryPrimitives.ReadUInt16LittleEndian(f[(i + 43)..]);
            int len = f[i + 45];
            if (len is < 1 or > 36 || i + 46 + len + 4 > f.Length || Utf8(f.Slice(i + 46, len)) is not { } name) continue;
            int p = i + 46 + len;
            int cls = BinaryPrimitives.ReadInt32LittleEndian(f[p..]);
            p += 4;
            Varint(f, ref p, out long hp);
            // Before the 24: slot | 2-byte member id | 00 00 | server | server.
            int slot = i >= 9 ? f[i - 9] : 0;
            list.Add(new(key, name, server, group, slot is >= 1 and <= 8 ? slot : 9, cls is >= 1 and <= 60 ? cls : 0, 0, hp, null));
            i += 45 + len;
        }
        return list;
    }
    void Save()
    {
        try
        {
            if (ownGroup is null) { File.Delete(FilePath); return; }
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new Saved(DateTime.Now, ownGroup, groups.ToDictionary(g => g.Key, g => g.Value.Number),
                groups.Values.SelectMany(g => g.Members.Values).ToList(), details.Where(d => groups.Values.Any(g => g.Members.ContainsKey(d.Key))).ToDictionary(d => d.Key, d => d.Value))));
        }
        catch { }
    }
    static bool Varint(ReadOnlySpan<byte> f, ref int p, out long value)
    {
        value = 0;
        for (int shift = 0; p < f.Length && shift <= 56; shift += 7)
        {
            byte b = f[p++];
            value |= (long)(b & 127) << shift;
            if ((b & 128) == 0) return true;
        }
        return false;
    }
    static bool IsGuid(ReadOnlySpan<byte> s)
    {
        for (int i = 0; i < 36; i++)
        {
            byte c = s[i];
            if (i is 8 or 13 or 18 or 23) { if (c != '-') return false; }
            else if (!(c is >= (byte)'0' and <= (byte)'9' or >= (byte)'A' and <= (byte)'F' or >= (byte)'a' and <= (byte)'f')) return false;
        }
        return true;
    }
    static string? Utf8(ReadOnlySpan<byte> bytes)
    {
        foreach (byte b in bytes) if (b < 0x20) return null;
        try { return new UTF8Encoding(false, true).GetString(bytes); } catch { return null; }
    }
}
