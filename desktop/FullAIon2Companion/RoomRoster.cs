using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.Json;

namespace FullAIon2Companion;

public sealed record RoomMember(int Slot, uint Key, string Name, int ServerId, string ServerTag, int Level, int GearScore, long CombatPower, bool Leader, bool Ready = false, int ClassCode = 0)
{
    /// <summary>The class code is the website's pcId: class = (code - 5) / 4 over the official order (8 Gladiator, 19 Assassin, 31 Cleric).</summary>
    public string? ClassKey => ClassCode >= 5 && (ClassCode - 5) / 4 < Classes.Length ? Classes[(ClassCode - 5) / 4] : null;
    static readonly string[] Classes = ["gladiator", "templar", "ranger", "assassin", "elementalist", "sorcerer", "cleric", "chanter"];
}
/// <param name="DungeonWorld">The world (map load) where the roster came from inside the dungeon, if it did.</param>
public sealed record RoomInfo(string Title, string Dungeon, int Capacity, IReadOnlyList<RoomMember> Members, int? DungeonWorld = null);

/// <summary>
/// The instance room (party finder room) roster, frame 0x0297 (wire 02 97), as decoded from a 2026-10-11 Krao Cave
/// room and checked against the in-game list and another meter (PE 2137/264/350/348, PC 100268/14645/16352/18904):
/// <code>
/// 02 97 | room key u32 | title (len + UTF-8) | capacity u8 | dungeon id u32 | 00 03 | leader key u32 | 00 00 |
/// server u16 | 3 state bytes | slot count u8 | slots...
/// slot: mask u8 | slot u8 | roster key u32 | 00 00 | server u16 | name (len + UTF-8) | class u32 | level u32 |
///       gear score (PE) u32 | server u16 | server u16 | 04 | combat power (PC) u32 | tail
/// </code>
/// Empty slots carry no name. The slot's first byte (mask) has READY in bit 0x02.
/// 0x1D97 (leaving) closes the room.
/// </summary>
public sealed class RoomRoster
{
    // 0B 97: one member joined (a roster slot with one extra byte before the repeated server). 16 97: a member's READY
    // state - roster key u32 | 00 00 | server u16 | 01 ready / 00 not ready (2026-10-11: Tobirama 01, 00, 01 as he toggled;
    // xShadower, MEPORTOREMAL and DougSiccK 01, matching the other meter's "Listo"). Members leaving come as a new full
    // roster (02 97) whose state bytes are the occupied-slot mask (07, 1B, 1F).
    // 15 97 | 00 00 | 01: the local player's own ready state (2026-10-13 13:23:06: Darkni pressed "Listo"; the game sent
    // 16 97 for the other members but not for him).
    const int Roster = 0x0297, Exit = 0x1D97, Joined = 0x0B97, ReadyState = 0x1697, OwnReady = 0x1597;
    /// <summary>The local character's name, to apply its own ready state (set by the capture engine).</summary>
    public Func<string?>? LocalName { get; set; }
    /// <summary>The current world (map load id), set by the capture engine.</summary>
    public Func<int?>? World { get; set; }
    static readonly Dictionary<string, (string Dungeon, string Difficulty)> dungeons = [];
    static readonly Dictionary<int, string> servers = [];
    readonly object gate = new();
    RoomInfo? room;
    // Ready states for the room's whole life: a roster resend can leave someone out and bring him back (14:02:50).
    readonly Dictionary<uint, bool> ready = [];
    uint roomKey;
    public sealed record Saved(DateTime At, RoomInfo Room);
    static string FilePath => Path.Combine(Program.DataDirectory, "room.json");
    // The game sends the roster only when the room changes: keep it across app restarts, for an hour at most.
    public RoomRoster()
    {
        try { if (File.Exists(FilePath) && JsonSerializer.Deserialize<Saved>(File.ReadAllText(FilePath)) is { } saved && DateTime.Now - saved.At < TimeSpan.FromHours(1)) room = saved.Room; } catch { }
    }
    void Store() { try { if (room is null) File.Delete(FilePath); else File.WriteAllText(FilePath, JsonSerializer.Serialize(new Saved(DateTime.Now, room))); } catch { } }
    static RoomRoster()
    {
        try
        {
            var root = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "rooms.json"))).RootElement;
            foreach (var d in root.GetProperty("dungeons").EnumerateObject())
                dungeons[d.Name] = (d.Value.GetProperty("dungeon").GetString() ?? "", d.Value.GetProperty("difficulty").GetString() ?? "");
            foreach (var s in root.GetProperty("servers").EnumerateObject()) servers[int.Parse(s.Name)] = s.Value.GetString()!;
        }
        catch { }
    }
    /// <summary>A server's name from data/rooms.json (null when unknown).</summary>
    public static string? ServerName(int id) => servers.GetValueOrDefault(id);
    public RoomInfo? Current { get { lock (gate) return room; } }
    /// <summary>
    /// Leaving the dungeon (or the party, from inside it) sends no 1D 97: the roster simply stops coming (2026-10-13 21:03,
    /// Feroz Horn Den: a roster every 10 s for 15 minutes, then nothing, and the overlay kept the room for an hour). Inside
    /// a dungeon the roster carries the instance, so once the player is in another world the room is over. A roster that
    /// comes again brings it back.
    /// </summary>
    public void CheckLeft()
    {
        lock (gate)
        {
            if (room?.DungeonWorld is not int dungeon || World?.Invoke() is not int now || now == dungeon) return;
            room = null; ready.Clear(); roomKey = 0; Store();
        }
    }
    public void Add(ReadOnlySpan<byte> frame) => Frames.Walk(frame, Parse);
    void Parse(ReadOnlySpan<byte> f)
    {
        if (f.Length < 4) return;
        int opcode = BinaryPrimitives.ReadUInt16BigEndian(f);
        if ((opcode & 0xFF) is 0x97 or 0x96)
        {
            Apply(f, opcode);
            Log(opcode, f);
            return;
        }
    }
    // Every room/party-family frame with the room as it stood after it: when the overlay disagrees with the game, this
    // file shows which frame did it.
    void Log(int opcode, ReadOnlySpan<byte> f)
    {
        try
        {
            string path = Path.Combine(Program.DataDirectory, "room-frames.log");
            if (File.Exists(path) && new FileInfo(path).Length > 4_000_000) File.Delete(path);
            string state = room is null ? "(sin sala)" : string.Join(" ", room.Members.Select(m => $"{m.Slot}:{m.Name}{(m.Leader ? "(L)" : m.Ready ? "+" : "-")}"));
            File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} 0x{opcode:X4} {Convert.ToHexString(f[..Math.Min(f.Length, 600)])} => {state}{Environment.NewLine}");
        }
        catch { }
    }
    void Apply(ReadOnlySpan<byte> f, int opcode)
    {
        if (opcode == Exit) { lock (gate) { room = null; ready.Clear(); Store(); } return; }
        if (opcode == Joined && f.Length > 30)
        {
            lock (gate)
            {
                if (room is null || !TryMember(f, 2, room.Members.FirstOrDefault(m => m.Leader)?.Key ?? 0, out var joined, out _, out _, extraByte: true)) return;
                room = room with { Members = room.Members.Where(m => m.Slot != joined.Slot && m.Key != joined.Key).Append(joined).OrderBy(m => m.Slot).ToList() };
                Store();
            }
            return;
        }
        if (opcode == OwnReady && f.Length == 5)
        {
            bool mine = f[4] == 1;
            string? me = LocalName?.Invoke();
            lock (gate)
            {
                if (room is null || me is null || room.Members.FirstOrDefault(m => m.Name.Equals(me, StringComparison.OrdinalIgnoreCase)) is not { } self) return;
                ready[self.Key] = mine;
                room = room with { Members = room.Members.Select(m => m.Key == self.Key ? m with { Ready = mine } : m).ToList() };
                Store();
            }
            return;
        }
        if (opcode == ReadyState && f.Length >= 11)
        {
            uint key = BinaryPrimitives.ReadUInt32LittleEndian(f[2..]);
            bool ready = f[10] == 1;
            lock (gate)
            {
                if (room is null || room.Members.All(m => m.Key != key)) return;
                this.ready[key] = ready;
                room = room with { Members = room.Members.Select(m => m.Key == key ? m with { Ready = ready } : m).ToList() };
                Store();
            }
            return;
        }
        if (opcode != Roster || f.Length < 40) return;
        int p = 6;
        int titleLength = f[p++];
        if (p + titleLength + 16 > f.Length) return;
        string title = Utf8(f.Slice(p, titleLength)) ?? ""; p += titleLength;
        int capacity = f[p++];
        uint dungeonId = BinaryPrimitives.ReadUInt32LittleEndian(f[p..]); p += 4;
        p += 2; // 00 03
        uint leader = BinaryPrimitives.ReadUInt32LittleEndian(f[p..]);
        var members = new List<RoomMember>();
        bool inside = false;
        for (int i = p + 4; i + 32 < f.Length; i++)
            if (TryMember(f, i, leader, out var member, out int next, out bool instance)) { members.Add(member); inside |= instance; i = next - 1; }
        string dungeon = dungeons.TryGetValue(dungeonId.ToString(), out var d) ? $"{d.Dungeon} · {d.Difficulty}" : $"Mazmorra {dungeonId}";
        lock (gate)
        {
            // The full roster states who is ready (bit 0x02 of each slot); the 16 97 / 15 97 frames update it in between.
            uint key = BinaryPrimitives.ReadUInt32LittleEndian(f[2..]);
            if (key != roomKey) { roomKey = key; ready.Clear(); }
            room = new(title, dungeon, capacity is > 0 and <= 20 ? capacity : members.Count,
                members.OrderBy(m => m.Slot).ToList(), inside ? World?.Invoke() ?? room?.DungeonWorld : null);
            foreach (var m in members) ready[m.Key] = m.Ready;
            Store();
        }
    }
    // One slot, accepted only when every field fits: the server repeated twice after the stats, the 04 tag before the PC.
    static bool TryMember(ReadOnlySpan<byte> f, int i, uint leader, out RoomMember member, out int next, out bool instance, bool extraByte = false)
    {
        member = null!; next = i; instance = false;
        int slot = f[i + 1];
        if (slot is < 1 or > 20 || f[i + 6] != 0 || f[i + 7] != 0) return false;
        uint key = BinaryPrimitives.ReadUInt32LittleEndian(f[(i + 2)..]);
        int server = BinaryPrimitives.ReadUInt16LittleEndian(f[(i + 8)..]);
        int nameLength = f[i + 10];
        if (key == 0 || server is < 1000 or > 9999 || nameLength is < 1 or > 36) return false;
        int p = i + 11 + nameLength;
        if (p + 22 > f.Length || Utf8(f.Slice(i + 11, nameLength)) is not { } name) return false;
        int level = BinaryPrimitives.ReadInt32LittleEndian(f[(p + 4)..]), gear = BinaryPrimitives.ReadInt32LittleEndian(f[(p + 8)..]);
        if (level is < 1 or > 100 || gear is < 0 or > 20000) return false;
        // After the stats come two servers: the member's own one, then the same again outside a dungeon or the instance
        // inside one (2026-10-13 Feroz Horn Den: 2401 -> 4404). The server before the name is the room's: a member from
        // another server has his own one only here (2026-10-17: «我 [Zik]», 2401 before the name, 2402 Zik after, was
        // dropped while the game listed him). An optional byte comes before them: always in a join (0B 97), in a full
        // roster only for some members (2026-10-11: Brynhild of Kai, 01). The byte before the combat power is a tag,
        // usually 04 (Yüna: 01). Without or with that byte: first the room's server, then a server repeated, then any.
        _ = extraByte;
        int x = -1;
        for (int tier = 0; tier < 3 && x < 0; tier++)
            foreach (int option in (int[])[0, 1])
            {
                if (p + 21 + option > f.Length || f[p + 16 + option] > 0x10 || ServerAt(f, p + 12 + option) is not int mine) continue;
                if (tier == 0 && mine != server || tier == 1 && mine != ServerAt(f, p + 14 + option)) continue;
                x = option; break;
            }
        if (x < 0) return false;
        int own = ServerAt(f, p + 12 + x)!.Value;
        long power = BinaryPrimitives.ReadUInt32LittleEndian(f[(p + 17 + x)..]);
        // The slot's first byte: bit 0x02 is READY (2026-10-13 13:26: Darkni 0C before pressing "Listo", 0E after; the
        // members already ready when he joined came as 0E, the leader 0C).
        bool ready = (f[i] & 0x02) != 0;
        instance = BinaryPrimitives.ReadUInt16LittleEndian(f[(p + 14 + x)..]) != own;
        member = new(slot, key, name, own, servers.GetValueOrDefault(own, own.ToString()), level, gear, power, key == leader, ready, BinaryPrimitives.ReadInt32LittleEndian(f[p..]));
        next = p + 21 + x;
        return true;
    }
    static int? ServerAt(ReadOnlySpan<byte> f, int at) => at + 2 <= f.Length && BinaryPrimitives.ReadUInt16LittleEndian(f[at..]) is var s and >= 1000 and <= 9999 ? s : null;
    static string? Utf8(ReadOnlySpan<byte> bytes)
    {
        foreach (byte b in bytes) if (b < 0x20) return null;
        try { return new UTF8Encoding(false, true).GetString(bytes); } catch { return null; }
    }
}
