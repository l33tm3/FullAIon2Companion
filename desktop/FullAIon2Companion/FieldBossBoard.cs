using System.IO;
using System.Text.Json;
using AionDPS.Aion2;

namespace FullAIon2Companion;

/// <summary>One field boss as the panel shows it: map, name, portrait, its state and the time that matters.
/// Estimated: the time comes from a death the app saw and the respawn learned before, not from the game's list.</summary>
public sealed record FieldBossRow(int Npc, string Map, string Name, string? Icon, string Phase, DateTime? At, DateTime? Seen, bool Starred, bool Estimated = false, string? Source = null);
public sealed record FieldBossMapView(string Key, string Name, IReadOnlyList<FieldBossRow> Bosses);

/// <summary>
/// The field bosses page (like TeamMate's "Field bosses"): every map of field_bosses.json with its bosses, what the game's
/// own list said last (opcode 0x0191, sent while the field boss list of the in-game map is open: alive with its position,
/// or dead with the exact time it comes back; verified live 2026-10-12 22:58 on Altgard, 24 bosses) and the Abyss bosses'
/// fixed times. Starred bosses get a corner notification some minutes before they come back and when they appear.
/// <para>The list only comes while it is open, and a boss it called alive stays so until the next one. So the bosses seen
/// around the player count too: a field boss appearing (0x4136) is alive now; its entity leaving dead (0x4236 reason 3) is a
/// death at that moment. A death seen and the game's next list (its respawn time) teach the respawn of that boss
/// (field-boss-respawn.json); later deaths get an estimated return.</para>
/// </summary>
public sealed class FieldBossBoard
{
    readonly AppSettings settings;
    readonly HashSet<(int Npc, DateTime At, bool Up)> announced = [];
    readonly object gate = new();
    sealed record Seen(bool Alive, DateTime At);
    readonly Dictionary<int, Seen> seen;
    readonly Dictionary<int, double> respawnMinutes;
    static string SeenPath => Path.Combine(Program.DataDirectory, "field-boss-seen.json");
    static string RespawnPath => Path.Combine(Program.DataDirectory, "field-boss-respawn.json");
    public static readonly HashSet<int> Npcs = Aion2FieldBosses.Maps.SelectMany(m => m.Bosses).Select(b => b.Npc).ToHashSet();

    public FieldBossBoard(AppSettings settings)
    {
        this.settings = settings; Aion2FieldBosses.EnablePersistence();
        try { seen = File.Exists(SeenPath) ? JsonSerializer.Deserialize<Dictionary<int, Seen>>(File.ReadAllText(SeenPath)) ?? [] : []; } catch { seen = []; }
        try { respawnMinutes = File.Exists(RespawnPath) ? JsonSerializer.Deserialize<Dictionary<int, double>>(File.ReadAllText(RespawnPath)) ?? [] : []; } catch { respawnMinutes = []; }
        Aion2FieldBosses.Changed += LearnFromList;
    }

    /// <summary>A field boss seen around the player (capture thread): appeared (alive) or left dead.</summary>
    public void Observe(int npc, bool alive, DateTime at)
    {
        if (!Npcs.Contains(npc)) return;
        lock (gate)
        {
            if (seen.TryGetValue(npc, out var last) && last.Alive == alive && Math.Abs((at - last.At).TotalMinutes) < 2) return;
            seen[npc] = new(alive, at);
            try { File.WriteAllText(SeenPath, JsonSerializer.Serialize(seen)); } catch { }
        }
    }
    // The game's list came: a dead boss's return time minus the death the app saw is its respawn.
    void LearnFromList()
    {
        var now = DateTime.Now;
        lock (gate)
        {
            bool changed = false;
            foreach (var b in Aion2FieldBosses.Maps.SelectMany(m => m.Bosses))
            {
                if (!seen.TryGetValue(b.Npc, out var s) || s.Alive) continue;
                var st = Aion2FieldBosses.StatusOf(b, now);
                if (st.Phase != BossPhase.Respawn || st.At is not DateTime back) continue;
                double minutes = (back - s.At).TotalMinutes;
                if (minutes is < 5 or > 24 * 60) continue;
                minutes = Math.Round(minutes);
                if (respawnMinutes.TryGetValue(b.Npc, out var known) && Math.Abs(known - minutes) < 1) continue;
                respawnMinutes[b.Npc] = minutes; changed = true;
            }
            if (changed) try { File.WriteAllText(RespawnPath, JsonSerializer.Serialize(respawnMinutes)); } catch { }
        }
    }
    /// <summary>The state to show: the game's list, unless the app saw the boss later (appear or die).</summary>
    (BossStatus Status, bool Estimated, string Source) Merge(FieldBoss b, DateTime now)
    {
        var st = Aion2FieldBosses.StatusOf(b, now);
        if (b.Rule is not null) return (st, false, "fixed");
        Seen? s; double? respawn;
        lock (gate) { seen.TryGetValue(b.Npc, out s); respawn = respawnMinutes.TryGetValue(b.Npc, out var r) ? r : null; }
        if (s is null || st.SeenAt is DateTime listAt && listAt >= s.At) return (st, false, "list");
        if (s.Alive) return (new BossStatus(BossPhase.Now, s.At, s.At), false, "seen");
        if (respawn is double minutes)
        {
            var back = s.At.AddMinutes(minutes);
            return (new BossStatus(back > now ? BossPhase.Respawn : BossPhase.Now, back, s.At), true, "seen");
        }
        return (new BossStatus(BossPhase.Unknown, s.At, s.At), false, "dead");
    }

    public IReadOnlyList<FieldBossMapView> View()
    {
        var now = DateTime.Now;
        string lang = L.Language is "pt" or "en" or "fr" ? L.Language : "es";
        return Aion2FieldBosses.Maps.Select(m => new FieldBossMapView(m.Key, m.NameIn(lang), m.Bosses.Select(b =>
        {
            var (st, estimated, source) = Merge(b, now);
            string? icon = Catalog.Npc(b.Npc) is { } r && r.TryGetProperty("icon", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString() : null;
            return new FieldBossRow(b.Npc, m.Key, Aion2FieldBosses.DisplayName(m, b, lang), icon, source == "dead" ? "Dead" : st.Phase.ToString(), st.At, st.SeenAt,
                settings.FieldBossStars.Contains(b.Npc), estimated, source);
        }).OrderBy(b => b.Phase == "Now" ? 0 : b.At is null ? 2 : 1).ThenBy(b => b.At ?? DateTime.MaxValue).ToList())).ToList();
    }

    /// <summary>The next field boss to come back: starred ones first, else any whose time is known.</summary>
    public (string Name, DateTime At, bool Starred)? Next()
    {
        var now = DateTime.Now;
        var rows = View().SelectMany(m => m.Bosses).Where(b => b.Phase is "Respawn" or "Fixed" && b.At > now).ToList();
        var pick = rows.Where(b => b.Starred).MinBy(b => b.At) ?? rows.MinBy(b => b.At);
        return pick is null ? null : (pick.Name, pick.At!.Value, pick.Starred);
    }

    /// <summary>Corner notifications for starred bosses: N minutes before they come back, and when they appear.</summary>
    public void Tick(OverlayWindow overlay, EventTimers timers)
    {
        if (!settings.FieldBossAlerts || settings.FieldBossStars.Count == 0) return;
        var now = DateTime.Now;
        foreach (var m in Aion2FieldBosses.Maps)
            foreach (var b in m.Bosses.Where(x => settings.FieldBossStars.Contains(x.Npc)))
            {
                var (st, _, _) = Merge(b, now);
                if (st.At is not DateTime at) continue;
                string name = Aion2FieldBosses.DisplayName(m, b, L.Language is "pt" or "en" or "fr" ? L.Language : "es");
                bool soon = st.Phase is BossPhase.Respawn or BossPhase.Fixed && at > now && at - now <= TimeSpan.FromMinutes(settings.FieldBossLeadMinutes);
                bool up = st.Phase == BossPhase.Now && now - at < TimeSpan.FromMinutes(2);
                if (soon && announced.Add((b.Npc, at, false))) { overlay.Toast(name, "#E9C77E", at, false); if (settings.Sounds) timers.Play(); }
                if (up && announced.Add((b.Npc, at, true))) { overlay.Toast(name, "#5FE0A0", at, true); if (settings.Sounds) timers.Play(); }
            }
        if (announced.Count > 500) announced.Clear();
    }
}
