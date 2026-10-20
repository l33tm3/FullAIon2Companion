using System.Buffers.Binary;
using System.IO;
using System.Text.Json;

namespace FullAIon2Companion;

/// <summary>A hidden cube the game announced: where (world units, 100 = 1 m), when, and whether it is still there.</summary>
/// GoneAt: opened (0x3536 with 04 03, seen 2026-10-12 23:23:30 opening one); LeftRangeAt: the server stopped sending it
/// because the player moved away (01 01) - it is still there. SeenFrom: metres from the player when it was announced.
public sealed record CubeSighting(DateTime At, string Map, float X, float Y, float Z, int Entity, int EnvId, DateTime? GoneAt = null, double? SeenFrom = null, DateTime? LeftRangeAt = null, bool Missing = false);

/// <summary>An environment object present now (0x3436 until its 0x3536): gather node, Od, trace or cube.</summary>
public sealed record EnvObject(int Entity, int EnvId, string Kind, string Name, float X, float Y, float Z);

/// <summary>Where to go: the live cube (or the nearest possible spot) from the player - metres, bearing on the north-up map, height.</summary>
public sealed record CubeView(CubeSighting? Active, double? Distance, double? Bearing, double? Height, int SpotsNear, double? NearestSpot,
    double? NearestBearing, string? Map, IReadOnlyList<CubeSighting> History, float? NearestX = null, float? NearestY = null);

/// <summary>
/// Hidden cubes (TeamMate's "Cubo oculto cerca"). A cube is rare, shared by the server, and appears on one of a few thousand
/// fixed spots (2,864 in our atlas markers "Cubo oculto", data/minimap-markers). The server announces it like any environment
/// object, gather nodes and chests included:
/// <list type="bullet">
/// <item>0x3436 EnvSpawn <c>entity varint | type u8 | 00 | env object id u32 | x, y, z</c> (checked 2026-10-13 on 156 objects
/// in three recordings: ids 1.1M-1.3M, positions where the player was);</item>
/// <item>0x3536 EnvRemove <c>entity varint | 04 03 00</c>: opened or gone.</item>
/// </list>
/// The hidden-cube object ids come from the public meter's parser (IsHiddenCube). An unknown object appearing on a cube spot
/// is only written to cube-candidates.log, to extend the list if the game adds cubes.
/// </summary>
public sealed class CubeWatch
{
    const int Spawn = 0x3436, Remove = 0x3536;
    const float CandidateRadius = 800, Cell = 2000;
    static readonly HashSet<int> CubeIds = [1200036, 1200037, 1200038, 1200039, 1200040, 1200041, 1200667, 1200707, 1201502, 1201503, 1201504, 1201505,
        1300081, 1300082, 1300083, 1300754, 1301199, 1301200, 1301242, 1301243, 1301244, 1301245, 1100056, 1100094];
    readonly object gate = new();
    readonly Dictionary<string, (Dictionary<(int, int), List<(float X, float Y)>> Grid, List<(float X, float Y)> All)> spots = [];
    readonly List<CubeSighting> history;
    readonly HashSet<int> candidatesLogged = [];
    readonly Dictionary<int, EnvObject> live = [];
    readonly Dictionary<int, DateTime> nearSince = [];
    // Gather objects of the client data (assets/aion2/maps/gather.json, built from MapData.dat + EnvObjData): Od, ores,
    // gems, herbs, food, wood, cotton and Empyrean traces, with their names in the game's languages.
    static readonly Dictionary<int, (string Kind, Dictionary<string, string> Names)> gather = LoadGather();
    static Dictionary<int, (string, Dictionary<string, string>)> LoadGather()
    {
        var map = new Dictionary<int, (string, Dictionary<string, string>)>();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "maps", "gather.json")));
            foreach (var o in doc.RootElement.GetProperty("objects").EnumerateObject())
                if (int.TryParse(o.Name, out int id))
                    map[id] = (o.Value.GetProperty("kind").GetString() ?? "", o.Value.GetProperty("names").EnumerateObject().ToDictionary(n => n.Name, n => n.Value.GetString() ?? ""));
        }
        catch { }
        return map;
    }
    /// <summary>Kind (Cube, Od, Ore, Gemstone, Herb, Food, Wood, Cotton, Fragment) and name in the interface language, or null.</summary>
    public static (string Kind, string Name)? Classify(int envId)
    {
        if (IsCube(envId)) return ("Cube", L.Language == "en" ? "Hidden Cube" : L.Language == "fr" ? "Cube caché" : "Cubo oculto");
        if (!gather.TryGetValue(envId, out var g)) return null;
        string name = g.Names.TryGetValue(L.Language, out var n) && n.Length > 0 ? n : g.Names.TryGetValue("es", out var es) ? es : g.Kind;
        return (g.Kind, name);
    }
    /// <summary>The environment objects the server shows around the player now.</summary>
    public IReadOnlyList<EnvObject> Live { get { lock (gate) return live.Values.ToList(); } }
    /// <summary>A map load or connection change: the objects of the previous place are gone.</summary>
    public void ForgetLive() { lock (gate) live.Clear(); }
    DateTime at;
    static string HistoryPath => Path.Combine(Program.DataDirectory, "cube-history.json");
    static string CandidatePath => Path.Combine(Program.DataDirectory, "cube-candidates.log");
    /// <summary>The atlas map the player is on (set by the capture engine).</summary>
    public Func<string?>? MapNow { get; set; }
    /// <summary>The player's position now (world units), to keep how far away each cube was announced.</summary>
    public Func<(float X, float Y)?>? OwnNow { get; set; }
    /// <summary>A cube was announced (live alert).</summary>
    public event Action<CubeSighting>? Found;
    /// <summary>False in replays: no files written.</summary>
    public bool Persist { get; set; } = true;

    public CubeWatch()
    {
        try { history = File.Exists(HistoryPath) ? JsonSerializer.Deserialize<List<CubeSighting>>(File.ReadAllText(HistoryPath)) ?? [] : []; } catch { history = []; }
    }
    public static bool IsCube(int envId) => CubeIds.Contains(envId);

    (Dictionary<(int, int), List<(float X, float Y)>> Grid, List<(float X, float Y)> All)? SpotsOf(string? mapId)
    {
        if (mapId is null || MinimapData.Map(mapId) is not { } map) return null;
        if (spots.TryGetValue(mapId, out var cached)) return cached;
        var grid = new Dictionary<(int, int), List<(float, float)>>(); var all = new List<(float, float)>();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "minimap-markers", mapId + ".json")));
            foreach (var c in doc.RootElement.GetProperty("categories").EnumerateArray())
            {
                if (c.GetProperty("filter").GetString() != "cubos") continue;
                foreach (var m in c.GetProperty("markers").EnumerateArray())
                {
                    // Atlas pixel -> world (the inverse of MinimapData.Project).
                    float x = (float)((m[0].GetDouble() - map.Bx) / map.Ax), y = (float)((m[1].GetDouble() - map.By) / map.Ay);
                    all.Add((x, y));
                    var key = ((int)Math.Floor(x / Cell), (int)Math.Floor(y / Cell));
                    if (!grid.TryGetValue(key, out var list)) grid[key] = list = [];
                    list.Add((x, y));
                }
            }
        }
        catch { }
        return spots[mapId] = (grid, all);
    }
    static double NearestIn(Dictionary<(int, int), List<(float X, float Y)>> grid, float x, float y)
    {
        double best = double.MaxValue;
        int cx = (int)Math.Floor(x / Cell), cy = (int)Math.Floor(y / Cell);
        for (int i = -1; i <= 1; i++)
            for (int j = -1; j <= 1; j++)
                if (grid.TryGetValue((cx + i, cy + j), out var list))
                    foreach (var s in list) best = Math.Min(best, Math.Sqrt((s.X - x) * (s.X - x) + (s.Y - y) * (s.Y - y)));
        return best;
    }

    public void Add(ReadOnlySpan<byte> frame, DateTime timestamp) { at = timestamp; Frames.Walk(frame, Parse); }
    DateTime usedUpAt = DateTime.MinValue;
    /// <summary>A node, a chest or a cube was used up (0x3536 04 03) in the seconds before: the items arriving now came
    /// from it (a cube's, 0.1 s after it, 2026-10-12 23:23:30), not from a creature.</summary>
    public bool UsedUpWithin(DateTime when, double seconds) { lock (gate) return (when - usedUpAt).TotalSeconds is >= 0 and var s && s <= seconds; }
    void Parse(ReadOnlySpan<byte> f)
    {
        if (f.Length < 5) return;
        int opcode = BinaryPrimitives.ReadUInt16BigEndian(f);
        if (opcode is not (Spawn or Remove)) return;
        int p = 2;
        if (!Varint(f, ref p, out long entity) || entity <= 0) return;
        // 04 03 00: the object was destroyed (a cube opened, a node gathered); 01 01 00: it left the player's range.
        if (opcode == Remove) { bool destroyed = p < f.Length && f[p] == 4; lock (gate) { live.Remove((int)entity); if (destroyed) usedUpAt = at; } Gone((int)entity, destroyed); return; }
        if (f.Length < p + 18) return;
        int envId = BinaryPrimitives.ReadInt32LittleEndian(f[(p + 2)..]);
        float x = BinaryPrimitives.ReadSingleLittleEndian(f[(p + 6)..]), y = BinaryPrimitives.ReadSingleLittleEndian(f[(p + 10)..]), z = BinaryPrimitives.ReadSingleLittleEndian(f[(p + 14)..]);
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z)) return;
        if (Classify(envId) is { } kind) lock (gate) { live[(int)entity] = new((int)entity, envId, kind.Kind, kind.Name, x, y, z); if (live.Count > 3000) live.Clear(); }
        string mapId = MapNow?.Invoke() ?? "?";
        if (!IsCube(envId))
        {
            if (gather.ContainsKey(envId)) return;
            // An object the list does not know standing on a cube spot: logged once per id, to extend the list later.
            if (!Persist || SpotsOf(mapId) is not { } s || NearestIn(s.Grid, x, y) > CandidateRadius) return;
            lock (gate) if (!candidatesLogged.Add(envId)) return;
            try { File.AppendAllText(CandidatePath, $"{at:yyyy-MM-dd HH:mm:ss.fff}\tenv={envId}\tmap={mapId}\tentity={entity}\t{x:0}\t{y:0}\t{z:0}\t{Convert.ToHexString(f)}\n"); } catch { }
            return;
        }
        double? from = OwnNow?.Invoke() is { } me ? Math.Round(Math.Sqrt((me.X - x) * (me.X - x) + (me.Y - y) * (me.Y - y)) / 100, 1) : null;
        var sighting = new CubeSighting(at, mapId, x, y, z, (int)entity, envId, null, from);
        lock (gate)
        {
            // The same cube sent again (coming back into range): keep the first sighting.
            if (history.Any(h => h.GoneAt is null && Math.Abs(h.X - x) < 100 && Math.Abs(h.Y - y) < 100 && (at - h.At).TotalHours < 3))
            {
                int i = history.FindLastIndex(h => h.GoneAt is null && Math.Abs(h.X - x) < 100 && Math.Abs(h.Y - y) < 100);
                if (i >= 0) { history[i] = history[i] with { Entity = (int)entity, LeftRangeAt = null }; Save(); }
                return;
            }
            history.Add(sighting);
            if (history.Count > 100) history.RemoveRange(0, history.Count - 100);
            Save();
        }
        Found?.Invoke(sighting);
    }
    void Gone(int entity, bool destroyed)
    {
        lock (gate)
        {
            int i = history.FindLastIndex(h => h.Entity == entity && h.GoneAt is null);
            if (i < 0) return;
            history[i] = destroyed ? history[i] with { GoneAt = at } : history[i] with { LeftRangeAt = at };
            Save();
        }
    }
    public void ClearHistory() { lock (gate) { history.Clear(); Save(); } }
    void Save() { if (Persist) try { File.WriteAllText(HistoryPath, JsonSerializer.Serialize(history)); } catch { } }

    /// <summary>The cube to go to (announced in the last 30 min and not gone) and the possible spots around the player.</summary>
    public CubeView View((float X, float Y)? own, float? ownZ, DateTime now, bool exact = false)
    {
        string? mapId = MapNow?.Invoke();
        lock (gate)
        {
            // A cube that left the player's range may be opened by someone else meanwhile, and nothing says so. Back within
            // 80 m of it (50 m with an approximate position: the server sends objects up to ~100 m) and the server does not
            // send it again within 10 s: it is not there any more
            // (2026-10-13 12:32, Altgard: 28 m from a cube seen at 12:24, 8 objects sent around, the cube not).
            if (own is { } me)
                for (int i = 0; i < history.Count; i++)
                {
                    var h = history[i];
                    if (h.GoneAt is not null || h.LeftRangeAt is not DateTime left || (now - left).TotalSeconds < 10) continue;
                    if (Math.Sqrt((h.X - me.X) * (h.X - me.X) + (h.Y - me.Y) * (h.Y - me.Y)) > (exact ? 8000 : 5000)) { nearSince.Remove(h.Entity); continue; }
                    if (nearSince.TryGetValue(h.Entity, out var since) ? (now - since).TotalSeconds < 10 : (nearSince[h.Entity] = now) == now) continue;
                    history[i] = h with { GoneAt = now, Missing = true }; nearSince.Remove(h.Entity); Save();
                }
            var active = history.LastOrDefault(h => h.GoneAt is null && (now - h.At).TotalMinutes < 30 && (mapId is null || h.Map == mapId || h.Map == "?"));
            double? dist = null, bearing = null, height = null, nearest = null, nearestBearing = null; int near = 0; float? nx = null, ny = null;
            var map = MinimapData.Map(mapId);
            if (own is { } o)
            {
                if (active is not null)
                {
                    dist = Math.Sqrt(Math.Pow(active.X - o.X, 2) + Math.Pow(active.Y - o.Y, 2)) / 100;
                    if (map is not null) bearing = Bearing(map, active.X - o.X, active.Y - o.Y);
                    if (ownZ is float mz) height = (active.Z - mz) / 100;
                }
                if (map is not null && SpotsOf(mapId) is { } s)
                {
                    double best = double.MaxValue; (float X, float Y) bestSpot = default;
                    foreach (var p in s.All)
                    {
                        double d = Math.Sqrt((p.X - o.X) * (p.X - o.X) + (p.Y - o.Y) * (p.Y - o.Y));
                        if (d < 10_000) near++;
                        if (d < best) { best = d; bestSpot = p; }
                    }
                    if (best < double.MaxValue) { nearest = best / 100; nearestBearing = Bearing(map, bestSpot.X - o.X, bestSpot.Y - o.Y); nx = bestSpot.X; ny = bestSpot.Y; }
                }
            }
            return new(active, dist, bearing, height, near, nearest, nearestBearing, mapId, history.AsEnumerable().Reverse().Take(30).ToList(), nx, ny);
        }
    }
    // Degrees clockwise from north (up) on the atlas, where the game's world axes are drawn.
    static double Bearing(MinimapMap map, double dx, double dy) => (Math.Atan2(map.Ax * dx, -map.Ay * dy) * 180 / Math.PI + 360) % 360;
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
