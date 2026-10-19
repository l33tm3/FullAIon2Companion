using System.IO;
using System.Text.Json;

namespace FullAIon2Companion;

/// <summary>
/// The player's own pins and routes on the Mapa de Atreia (ui/atlas.js), per map (the atlas ids of minimap-markers: "1" to
/// "10"), in atlas pixels (the map's 4,096), kept on this computer (atlas-marks.json) and drawn on the overlay's minimap too,
/// which uses the same pixels.
/// </summary>
public sealed class AtlasMarks
{
    public sealed record Pin(double X, double Y, string Name);
    public sealed record Route(string Name, double[][] Points);
    public sealed class Map
    {
        public List<Pin> Pins { get; set; } = [];
        public List<Route> Routes { get; set; } = [];
    }
    const int MaxPins = 200, MaxRoutes = 50, MaxPoints = 400, MaxName = 40;
    readonly object gate = new();
    Dictionary<string, Map> maps = [];
    string? path;
    /// <summary>Raises with every change: the panel gets the marks again when it does.</summary>
    public int Version { get; private set; }

    public void Open(string file)
    {
        path = file;
        try { maps = JsonSerializer.Deserialize<Dictionary<string, Map>>(File.ReadAllText(file)) ?? []; } catch { maps = []; }
        foreach (var m in maps.Values) { m.Pins ??= []; m.Routes ??= []; }
    }
    void Save()
    {
        if (path is null) return;
        string json; lock (gate) json = JsonSerializer.Serialize(maps);
        try { File.WriteAllText(path + ".tmp", json); File.Move(path + ".tmp", path, true); } catch { }
    }

    public Map? Of(string map) { lock (gate) return maps.GetValueOrDefault(map); }

    /// <summary>Every map's marks as the panel draws them.</summary>
    public object View()
    {
        lock (gate)
            return new
            {
                version = Version,
                maps = maps.ToDictionary(m => m.Key, m => new
                {
                    pins = m.Value.Pins.Select(p => new { x = p.X, y = p.Y, name = p.Name }).ToList(),
                    routes = m.Value.Routes.Select(r => new { name = r.Name, points = r.Points }).ToList(),
                }),
            };
    }

    /// <summary>
    /// A map's marks as the panel has them now (they replace the old ones): a map of the app, at most 200 pins and 50 routes
    /// of 2 to 400 points, all inside the atlas, names of up to 40 characters.
    /// </summary>
    public (int Pins, int Routes) Set(string map, JsonElement pins, JsonElement routes)
    {
        if (map.Length is 0 or > 3 || !map.All(char.IsAsciiDigit) || !File.Exists(Path.Combine(AppContext.BaseDirectory, "data", "minimap-markers", map + ".json")))
            throw new InvalidOperationException("Mapa desconocido.");
        static bool Inside(double v) => double.IsFinite(v) && v is >= -400 and <= 4500;
        // Only numbers count (a string or a null in the panel's list is left out, not an error).
        static bool Number(JsonElement e, out double v) { v = 0; return e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out v); }
        static string Name(JsonElement e, string fallback) => e.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String && n.GetString()!.Trim() is { Length: > 0 } t ? t[..Math.Min(MaxName, t.Length)] : fallback;
        var next = new Map();
        foreach (var p in pins.ValueKind == JsonValueKind.Array ? pins.EnumerateArray().Take(MaxPins) : [])
            if (p.ValueKind == JsonValueKind.Object && p.TryGetProperty("x", out var x) && Number(x, out double px) && p.TryGetProperty("y", out var y) && Number(y, out double py) && Inside(px) && Inside(py))
                next.Pins.Add(new(Math.Round(px, 1), Math.Round(py, 1), Name(p, "Pin " + (next.Pins.Count + 1))));
        foreach (var r in routes.ValueKind == JsonValueKind.Array ? routes.EnumerateArray().Take(MaxRoutes) : [])
        {
            if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("points", out var list) || list.ValueKind != JsonValueKind.Array) continue;
            var points = list.EnumerateArray().Take(MaxPoints)
                .Where(q => q.ValueKind == JsonValueKind.Array && q.GetArrayLength() >= 2 && Number(q[0], out double a) && Number(q[1], out double b) && Inside(a) && Inside(b))
                .Select(q => new[] { Math.Round(q[0].GetDouble(), 1), Math.Round(q[1].GetDouble(), 1) }).ToArray();
            if (points.Length >= 2) next.Routes.Add(new(Name(r, "Ruta " + (next.Routes.Count + 1)), points));
        }
        lock (gate)
        {
            if (next.Pins.Count == 0 && next.Routes.Count == 0) maps.Remove(map); else maps[map] = next;
            Version++;
        }
        Save();
        return (next.Pins.Count, next.Routes.Count);
    }
}
