using System.Diagnostics;
using System.IO;
using System.Text.Json;
using AionDPS.Aion2;

namespace FullAIon2Companion;

/// <summary>
/// Player names by entity id, kept on disk for the running game session. The game names a player only when it comes
/// into range (or the zone is entered); restarting this app next to someone left them as "Objetivo sin identificar",
/// and the own character as "identidad pendiente". Entity ids hold for the whole game session, so the names are
/// restored when the app starts - but only into the same AION 2 process: a new game session hands the ids out again.
/// </summary>
public sealed class PlayerMemory
{
    public sealed record Entry(string Name, int? Server);
    public sealed record Saved(string Session, Dictionary<int, Entry> Players);
    static string FilePath => Path.Combine(Program.DataDirectory, "players-session.json");
    readonly Dictionary<int, Entry> players = [];
    string? session;
    DateTime lastSave = DateTime.MinValue;

    /// <summary>The AION 2 process start time identifies the game session.</summary>
    /// <summary>The game session plus the connection: entity ids are only valid within both.</summary>
    public static string? SessionKey(int localPort) => GameSession() is string game ? $"{game}:{localPort}" : null;
    static string? GameSession()
    {
        foreach (var p in Process.GetProcessesByName("AION2"))
            using (p) { try { if (p.MainWindowHandle != IntPtr.Zero) return $"{p.Id}:{p.StartTime.ToUniversalTime():O}"; } catch { } }
        return null;
    }
    /// <summary>Puts the names of this game session back into the directory; fresh announcements still win.</summary>
    public void Restore(Aion2EntityDirectory entities, int localPort)
    {
        // Per connection, not only per game process: after a relog or reconnection the ids are handed out again.
        session = GameSession() is string game ? $"{game}:{localPort}" : null;
        players.Clear(); // a new connection starts a new set of ids
        try
        {
            if (session is null || !File.Exists(FilePath) || JsonSerializer.Deserialize<Saved>(File.ReadAllText(FilePath)) is not { } saved || saved.Session != session) return;
            foreach (var (id, entry) in saved.Players)
            {
                players[id] = entry;
                entities.Register(id, entry.Name);
                if (entry.Server is int server) entities.NoteServerOfName(entry.Name, server);
            }
        }
        catch { }
    }
    /// <summary>Records the players the directory knows now (called from the snapshot; written every 30 s).</summary>
    public void Remember(Aion2EntityDirectory entities, IReadOnlyDictionary<int, string> names)
    {
        if (session is null) return;
        foreach (var (id, name) in names)
            if (id > 0 && entities.NpcIdOf(id) is null && name.Length > 1)
                players[id] = new(name, entities.ServerIdOf(id) ?? (players.TryGetValue(id, out var old) && old.Name == name ? old.Server : null));
        if (DateTime.Now - lastSave < TimeSpan.FromSeconds(30)) return;
        lastSave = DateTime.Now;
        // A player gets a new entity id on every map load: only the id the name was registered under last is kept. All of
        // them were before 2026-10-18 (14 ids for the own character), and restored they all carried the name again.
        foreach (int stale in players.Where(p => entities.IdOfName(p.Value.Name) is int now && now != p.Key).Select(p => p.Key).ToList())
            players.Remove(stale);
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(new Saved(session, players))); } catch { }
    }
}
