using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FullAIon2Companion;

/// <summary>
/// Pictures for the website's tour (2026-10-19, the user: «faltan más: el minimapa, la caza de bichos, consultar el PvP de
/// otro personaje, las alertas de los jefes con sonidos…»): the app's own windows drawn out of sight, with real data. The
/// minimap comes from a recording replayed up to its busiest moment, the PvP card from the cached official profile of the
/// user's character (Darkni, the only real name shown), the creatures and loot from the catalog. Other players are left out.
/// <c>--tour-shots &lt;recording&gt; &lt;folder&gt;</c>; the data folder is a temporary one (Program sets it).
/// </summary>
static class TourShots
{
    static readonly Brush Backdrop = Frozen(new LinearGradientBrush(Color.FromRgb(58, 63, 67), Color.FromRgb(22, 25, 27), 60));
    static Brush Frozen(Brush b) { b.Freeze(); return b; }
    static readonly JsonSerializerOptions Web = new() { PropertyNameCaseInsensitive = true };

    public static int Run(string recording, string folder, string realData)
    {
        Directory.CreateDirectory(folder);
        var lines = new List<string>();
        // The profile cache of the real data folder, read only: the class icons and the PvP card need it.
        string cache = Path.Combine(realData, "player-cache.json");
        if (File.Exists(cache)) File.Copy(cache, Path.Combine(Program.DataDirectory, "player-cache.json"), true);

        lines.Add(Minimap(recording, folder));
        lines.Add(PvpCard(cache, folder));
        lines.Add(BossAlert(folder));
        lines.Add(Assistant(folder));
        lines.Add(Hunting(folder));
        File.WriteAllLines(Path.Combine(folder, "tour-shots.txt"), lines);
        Console.WriteLine(string.Join(Environment.NewLine, lines));
        return lines.All(l => l.StartsWith("ok")) ? 0 : 1;
    }

    // Lets the windows load what they load in the background (remote icons, timers) for a while.
    static void Pump(int ms)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
    static Window OutOfSight(Window w) { w.Left = -8000; w.Top = -8000; w.Opacity = 0; w.ShowActivated = false; return w; }
    // A window's content, taken out to be placed on the picture (at the size it had in its window).
    static FrameworkElement Take(Window w, bool keepSize = false)
    {
        var content = (FrameworkElement)w.Content;
        if (keepSize) { content.Width = content.ActualWidth; content.Height = content.ActualHeight; }
        w.Content = null;
        return content;
    }
    static void Save(UIElement child, string file, double padding = 22)
    {
        var stage = new Border { Padding = new Thickness(padding), Background = Backdrop, Child = child };
        stage.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); stage.Arrange(new Rect(stage.DesiredSize)); stage.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(stage.ActualWidth * 2), (int)Math.Ceiling(stage.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        bmp.Render(stage);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bmp));
        using var f = File.Create(file); png.Save(f);
    }
    static CaptureSnapshot Snapshot(TargetInfo? target, IReadOnlyList<ActorRow> actors, double dps, long damage, PetHunt? pet = null, IReadOnlyList<CastRow>? casts = null) =>
        new("Captura activa · Ethernet", 1000, 900, 500, 0, "", 325, "Darkni", damage, 0, dps, 75, 7, 0, target, [], actors, 0, 0, 0, false,
            Casts: casts, LocalServerId: 2401, PingMs: 57, Pet: pet, World: 20);

    /// <summary>The minimap at the moment of the recording with the most creatures around an exact position.</summary>
    static string Minimap(string recording, string folder)
    {
        DateTime at = DateTime.MinValue, next = DateTime.MinValue; CaptureSnapshot? best = null; int bestCount = -1;
        using (var engine = new CaptureEngine { ReplayClock = () => at })
            foreach (var line in File.ReadLines(recording))
            {
                var s = JsonSerializer.Deserialize<SegmentLog>(line)!; at = s.Timestamp;
                engine.Ingest(new(s.Timestamp, s.Source, s.Destination, s.Sequence, Convert.FromBase64String(s.Payload), s.FromServer));
                if (at < next) continue;
                next = at.AddSeconds(2);
                var snap = engine.Snapshot();
                int count = snap.Position is { Exact: true } && MinimapData.Vote((snap.Mobs ?? []).Select(m => m.Npc)).Map is not null ? snap.Mobs?.Count ?? 0 : -1;
                if (count > bestCount) { bestCount = count; best = snap; }
            }
        if (best is null || bestCount < 0) return "FALLA minimapa: la grabación no trae una posición exacta";
        // Nobody else on it: no troop, party or room.
        best = best with { Troop = null, Party = null, Room = null };
        var settings = new AppSettings { MinimapWidth = 440, MinimapHeight = 440, MinimapLeft = -8000, MinimapTop = -8000 };
        var map = (MinimapWindow)OutOfSight(new MinimapWindow(settings));
        map.Show(); map.Render(best); Pump(400); map.Render(best); Pump(600);
        var content = Take(map, keepSize: true); map.Close();
        L.Apply(content);
        Save(content, Path.Combine(folder, "minimap.png"));
        return $"ok minimapa: {best.Mobs?.Count ?? 0} criaturas, mundo {best.World}";
    }

    /// <summary>The card of another player as the overlay shows it when targeted: Darkni, compared with another Gladiator.</summary>
    static string PvpCard(string cache, string folder)
    {
        if (!File.Exists(cache)) return "FALLA ficha PvP: no hay caché de perfiles";
        var entries = JsonSerializer.Deserialize<Dictionary<string, PlayerProfiles.CacheEntry>>(File.ReadAllText(cache), Web) ?? [];
        var target = entries.FirstOrDefault(e => e.Key.EndsWith("|darkni", StringComparison.Ordinal)).Value?.Card;
        if (target is null) return "FALLA ficha PvP: Darkni no está en la caché";
        // «You»: a player of about the same strength, only for the comparison (the card shows no name of theirs).
        var mine = entries.Values.Select(e => e.Card).Where(c => c.Name != target.Name && c.Gear.Count > 8 && c.Stats is not null)
            .OrderBy(c => Math.Abs(c.CombatPower - target.CombatPower * .93)).FirstOrDefault();
        var card = new PlayerWindow();
        OutOfSight(card);
        card.Load("la|2401|darkni", target.Name, new ProfileState { Stage = "full", Card = target }, mine);
        card.Show(); Pump(5000); card.UpdateLayout();
        // At the size it asks for: the window clips its last line.
        var content = Take(card); content.Width = 340; card.Close();
        L.Apply(content);
        Save(content, Path.Combine(folder, "pvp-card.png"));
        return $"ok ficha PvP: {target.Name} ({target.ClassName}, {target.CombatPower:N0}) frente a {mine?.ClassName ?? "nadie"}";
    }

    /// <summary>A field boss starting a skill: the big warning in the middle of the screen, the strip in the overlay and the boss card.</summary>
    static string BossAlert(string folder)
    {
        var settings = new AppSettings { OverlayVisible = false, OverlayStyle = "pulido", CastAlertPlace = "both", BossPacePlace = "overlay" };
        var overlay = new OverlayWindow(settings) { Preview = true };
        var players = new List<ActorRow> { new(325, "Darkni", 2_140_000, 24_310, 0) };
        var kelpina = new TargetInfo(9001, 2400335, "Kelpina de onda azul", null, 61_400_000, 98_000_000, true, 1_980_000, 31, 19);
        var cast = new CastRow(9001, "Kelpina de onda azul", 7401, "Spirit Wave", 0.3, true, false, 2500, 2400335);
        var snap = Snapshot(kelpina, players, 24_310, 2_140_000, casts: [cast]);
        overlay.Update(snap, null, players); overlay.Update(snap, null, players);
        var sides = overlay.PreviewSides(kelpina);
        var body = Take(overlay); overlay.Close();
        var alert = new CenterAlert();
        OutOfSight(alert);
        alert.Flash("Spirit Wave", "Kelpina de onda azul  ·  2.5 s"); alert.UpdateLayout();
        var warning = Take(alert); alert.Close();
        warning.HorizontalAlignment = HorizontalAlignment.Center; warning.Margin = new Thickness(0, 0, 0, 16);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        body.VerticalAlignment = VerticalAlignment.Top; row.Children.Add(body);
        if (sides.Boss is { } boss) { boss.Width = 360; boss.VerticalAlignment = VerticalAlignment.Top; boss.Margin = new Thickness(14, 0, 0, 0); row.Children.Add(boss); }
        var set = new StackPanel(); set.Children.Add(warning); set.Children.Add(row);
        L.Apply(set);
        Save(set, Path.Combine(folder, "boss-alert.png"));
        return "ok aviso del jefe: Kelpina de onda azul, Spirit Wave";
    }

    /// <summary>The combat assistant of a Gladiator: buffs over cooldowns, and the card of the buff under the mouse.</summary>
    static string Assistant(string folder)
    {
        TimerTile Tile(int id, double left, double total, bool buff) { var s = CombatAssistant.Skill(id); return new(id, s?.Name ?? id.ToString(), s?.Icon, left, total, buff); }
        var buffs = new List<TimerTile> { Tile(11250000, 12.3, 20, true), Tile(11400000, 14.1, 20, true), Tile(11130000, 16.4, 20, true), Tile(11100000, 9.5, 20, true) };
        var cooldowns = new List<TimerTile> { Tile(11390000, 6.9, 12, false), Tile(11380000, 48, 90, false), Tile(11700000, 49, 60, false), Tile(11250000, 95, 120, false) };
        var bar = (AssistantWindow)OutOfSight(new AssistantWindow(new AppSettings { AssistantVisible = true, AssistantLeft = -8000, AssistantTop = -8000, AssistantScale = 1.4 }));
        bar.Update(buffs, cooldowns, true); bar.UpdateLayout(); Pump(200);
        var tiles = Take(bar, keepSize: true); bar.Close();
        var card = SkillCardWindow.Preview(buffs[0], 20, 1.15);
        card.HorizontalAlignment = HorizontalAlignment.Left;
        tiles.HorizontalAlignment = HorizontalAlignment.Left; tiles.Margin = new Thickness(60, 0, 0, 0);
        var set = new StackPanel(); set.Children.Add(card); set.Children.Add(tiles);
        L.Apply(set);
        Save(set, Path.Combine(folder, "assistant.png"));
        return $"ok asistente: {buffs.Count} buffos y {cooldowns.Count} recargas, ficha de {buffs[0].Name}";
    }

    /// <summary>Hunting a pet's souls: a Kerubar targeted, the pet card in the overlay and the loot it gives.</summary>
    static string Hunting(string folder)
    {
        var settings = new AppSettings { OverlayVisible = false, OverlayStyle = "pulido" };
        var overlay = new OverlayWindow(settings) { Preview = true };
        var players = new List<ActorRow> { new(325, "Darkni", 312_000, 18_400, 0) };
        var kerubar = new TargetInfo(9101, 2400801, "Kerubar", null, 21_300, 52_000, true, FightSeconds: 6);
        string race = Catalog.Npc(2400801) is { } npc && npc.TryGetProperty("creatureName", out var c) ? c.GetString() ?? "Cogni" : "Cogni";
        var pet = new PetStatus(1162, "Kerubar", 1, 9, 25, 16, 38, 9, .24, 66, race, ["Kerubar"]);
        var klaw = new PetStatus(1005, "Klaw", 2, 41, 75, 34, 0, 0, null, null, "Cognis", []);
        var hunt = new PetHunt(pet, [], "Kerubar", DateTime.Now.AddMinutes(-2), Target: pet, Pinned: [pet, klaw], SessionSouls: 9);
        var snap = Snapshot(kerubar, players, 18_400, 312_000, pet: hunt);
        overlay.Update(snap, null, players); overlay.Update(snap, null, players);
        var sides = overlay.PreviewSides(kerubar);
        var body = Take(overlay); overlay.Close();
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        body.VerticalAlignment = VerticalAlignment.Top; row.Children.Add(body);
        if (sides.Drops is { } drops) { drops.Width = 300; drops.VerticalAlignment = VerticalAlignment.Top; drops.Margin = new Thickness(14, 0, 0, 0); row.Children.Add(drops); }
        L.Apply(row);
        Save(row, Path.Combine(folder, "hunting.png"));
        return $"ok caza: Kerubar ({race}), alma 9/25, botín {(sides.Drops is null ? "sin tarjeta" : "con tarjeta")}";
    }
}
