using System.Buffers.Binary;

namespace FullAIon2Companion;

/// <summary>A room of the game's party finder, as its room list shows it. Immediate: anyone joins at once («Participar de
/// inmediato», «Invadir»); otherwise the leader accepts («Solicitar participación»).</summary>
public sealed record BoardRoom(uint Key, string Title, int Capacity, bool Immediate, IReadOnlyList<RoomMember> Members);
/// <summary>The room list of one dungeon level, as the game last sent it, placed as the game's windows show it: Expedición
/// (tab Conquista or Exploración, the dungeons by stars, Normal or Difícil) or Trascendencia (Fase 1-4).</summary>
public sealed record BoardList(uint Content, string Category, string Tab, string Dungeon, int Stars, string Level, int Order, DateTime At, IReadOnlyList<BoardRoom> Rooms);

/// <summary>
/// The party finder's room lists (2026-10-23, the user: «¿podremos ver qué equipos hay? … en tiempo real»): frame 0x0197,
/// sent when a dungeon is picked in the Expedición or Trascendencia window and again every 8-10 s while it stays open.
/// Checked against two screenshots: Draupnir (600033) «Room title one» of LeaderOne [AZP] 4/5 by request
/// and «Leaving now.» of LeaderTwo 1/5 at once; Arkanis (600063) LeaderThree 4/5, LeaderFour 2/5 («Invadir») and LeaderFive 4/5.
/// <code>
/// 01 97 | 00 00 | content u32 | 25 bytes | room count u8 | rooms...
/// room: key u32 | title (len + UTF-8) | capacity u8 | content u32 | u8 | 03 | leader key u32 | 00 00 | server u16
///       | join u8 (01 at once, 02 by request) | 2 bytes | slots as in the own room's roster (0x0297) | open slots
/// </code>
/// A room is found by its title before the content id, and its members by the own room's slot reader (RoomRoster.TryMember).
/// </summary>
public sealed class RoomBoard
{
    const int ListOpcode = 0x0197;
    readonly object gate = new();
    readonly Dictionary<uint, BoardList> lists = [];
    DateTime at;
    /// <summary>Raises with every list.</summary>
    public int Version { get; private set; }
    /// <summary>The lists seen, the newest first.</summary>
    public IReadOnlyList<BoardList> Lists { get { lock (gate) return lists.Values.OrderByDescending(l => l.At).ToList(); } }

    public void Add(ReadOnlySpan<byte> frame, DateTime timestamp) { at = timestamp; Frames.Walk(frame, Parse); }
    void Parse(ReadOnlySpan<byte> f)
    {
        if (f.Length < 40 || BinaryPrimitives.ReadUInt16BigEndian(f) != ListOpcode || Read(f, at) is not { } list) return;
        lock (gate) { lists[list.Content] = list; Version++; }
    }

    public static BoardList? Read(ReadOnlySpan<byte> f, DateTime at)
    {
        if (f.Length < 40) return null;
        uint content = BinaryPrimitives.ReadUInt32LittleEndian(f[4..]);
        if (content == 0) return null;
        // Each room: its capacity and the content id right after its title. The longest title that fits is the real one: a
        // longer one would take in the length byte and the room key, which hold control bytes.
        var starts = new List<(int Key, int Capacity)>();
        for (int i = 39; i + 4 <= f.Length; i++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(f[i..]) != content || f[i - 1] is < 1 or > 20) continue;
            int capacity = i - 1, found = -1;
            for (int length = 1; length <= 60; length++)
            {
                int t = capacity - length - 1;
                if (t < 38) break;
                if (f[t] == length && RoomRoster.Utf8(f.Slice(t + 1, length)) is not null) found = t;
            }
            if (found >= 0) { starts.Add((found - 4, capacity)); i = capacity + 5; }
        }
        var rooms = new List<BoardRoom>();
        for (int n = 0; n < starts.Count; n++)
        {
            var (key, capacity) = starts[n];
            int end = n + 1 < starts.Count ? starts[n + 1].Key : f.Length;
            if (capacity + 16 > end) continue;
            uint leader = BinaryPrimitives.ReadUInt32LittleEndian(f[(capacity + 7)..]);
            var members = new List<RoomMember>();
            for (int i = capacity + 16; i + 32 < f.Length && i < end; i++)
                if (TryMember(f, i, leader, out var member, out int next)) { members.Add(member); i = next - 1; }
            int title = key + 4;
            rooms.Add(new(BinaryPrimitives.ReadUInt32LittleEndian(f[key..]), RoomRoster.Utf8(f.Slice(title + 1, f[title])) ?? "",
                f[capacity], f[capacity + 15] == 1, members.OrderBy(m => m.Slot).ToList()));
        }
        var place = Places.TryGetValue(content, out var known) ? known : new Place("Otras", "", RoomRoster.DungeonName(content), 0, "", 99);
        return new(content, place.Category, place.Tab, place.Dungeon, place.Stars, place.Level, place.Order, at, rooms);
    }
    // Where each level sits in the game's windows (2026-10-23, the user: «Expedición tiene Exploración y Conquista y en
    // Conquista hay un modo Difícil y tienen estrellas»; Trascendencia, Arkanis Fragmentado and the Deus base with Fase 1-4).
    // The ids are the website catalog's difficulty ids (public/data/game/dungeons): Easy = Exploración, Normal = Conquista,
    // Hard = Difícil, and the four Transcendence ids of each base its phases. Names and stars as the windows show them.
    sealed record Place(string Category, string Tab, string Dungeon, int Stars, string Level, int Order);
    /// <summary>A level as the game's windows name it («Cueva de Krao · Difícil», «Arkanis Fragmentado · Fase 1», «Draupnir ·
    /// Exploración»), or null when it is not one of them. The own room's line in the overlay uses it too.</summary>
    public static string? PlaceName(uint content) => Places.TryGetValue(content, out var p) ? $"{p.Dungeon} · {(p.Level.Length > 0 ? p.Level : p.Tab)}" : null;
    static readonly Dictionary<uint, Place> Places = Build();
    static Dictionary<uint, Place> Build()
    {
        var places = new Dictionary<uint, Place>();
        // Conquista lists them by stars; Exploración, without stars, in another order (Krao, Urugugu, Fuego, Draupnir…).
        (string Name, int Stars, int Exploration, uint Easy, uint Normal, uint Hard)[] expedition =
        [
            ("Cueva de Krao", 1, 0, 600002, 600001, 600004), ("Draupnir", 1, 3, 600031, 600032, 600033),
            ("Cañón de Urugugu", 2, 1, 600011, 600012, 600013), ("Isla Aérea de Vakron", 2, 4, 600071, 600072, 600073),
            ("Templo de Fuego", 3, 2, 600021, 600022, 600023), ("Caverna del Cuerno", 3, 5, 600091, 600092, 600093),
        ];
        for (int i = 0; i < expedition.Length; i++)
        {
            var (name, stars, exploration, easy, normal, hard) = expedition[i];
            places[easy] = new("Expedición", "Exploración", name, 0, "", 20 + exploration);
            places[normal] = new("Expedición", "Conquista", name, stars, "Normal", i);
            places[hard] = new("Expedición", "Conquista", name, stars, "Difícil", i);
        }
        (string Name, uint Easy, uint[] Phases)[] transcendence =
        [
            ("Arkanis Fragmentado", 600061, [600063, 600065, 600068, 610062]),
            ("Base de Investigación de Deus", 600051, [600053, 600055, 600058, 610052]),
        ];
        for (int i = 0; i < transcendence.Length; i++)
        {
            var (name, easy, phases) = transcendence[i];
            places[easy] = new("Trascendencia", "", name, 1, "Exploración", 40 + i);
            for (int f = 0; f < phases.Length; f++) places[phases[f]] = new("Trascendencia", "", name, 1, $"Fase {f + 1}", 40 + i);
        }
        return places;
    }
    // One member, as in the own room's roster up to the gear score; after it the list differs (2026-10-23): one server or the
    // same twice, sometimes after a stray byte (MemberOne 0D, LeaderFour E9), then a tag up to 0x10 and the combat power. The
    // server there is the member's own; without one, the one before the name.
    static bool TryMember(ReadOnlySpan<byte> f, int i, uint leader, out RoomMember member, out int next)
    {
        member = null!; next = i;
        int slot = f[i + 1];
        if (slot is < 1 or > 20 || f[i + 6] != 0 || f[i + 7] != 0) return false;
        uint key = BinaryPrimitives.ReadUInt32LittleEndian(f[(i + 2)..]);
        int before = BinaryPrimitives.ReadUInt16LittleEndian(f[(i + 8)..]), nameLength = f[i + 10];
        if (key == 0 || before is < 1000 or > 9999 || nameLength is < 1 or > 36 || i + 11 + nameLength + 12 > f.Length) return false;
        if (RoomRoster.Utf8(f.Slice(i + 11, nameLength)) is not { } name) return false;
        int p = i + 11 + nameLength;
        int classCode = BinaryPrimitives.ReadInt32LittleEndian(f[p..]), level = BinaryPrimitives.ReadInt32LittleEndian(f[(p + 4)..]), gear = BinaryPrimitives.ReadInt32LittleEndian(f[(p + 8)..]);
        if (level is < 1 or > 100 || gear is < 0 or > 20000 || classCode is < 0 or > 255) return false;
        int own = before, q = p + 12;
        long power = 0;
        foreach (int skip in (int[])[0, 1])
        {
            if (q + skip + 2 > f.Length) continue;
            int server = BinaryPrimitives.ReadUInt16LittleEndian(f[(q + skip)..]);
            if (server is < 1000 or > 9999) continue;
            int t = q + skip + 2;
            if (t + 2 <= f.Length && BinaryPrimitives.ReadUInt16LittleEndian(f[t..]) == server) t += 2;
            if (t + 5 > f.Length || f[t] > 0x10) continue;
            own = server; power = BinaryPrimitives.ReadUInt32LittleEndian(f[(t + 1)..]); q = t + 5; break;
        }
        member = new(slot, key, name, own, RoomRoster.ServerName(own) ?? own.ToString(), level, gear, power, key == leader, (f[i] & 0x02) != 0, classCode);
        next = q;
        return true;
    }
}
