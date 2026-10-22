using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Text.Json;
using System.Windows.Input;

namespace FullAIon2Companion;

/// <summary>The targeted player's official profile beside the overlay (the slot the drops panel uses for creatures).</summary>
public sealed class PlayerWindow : Window
{
    static readonly Dictionary<string, string> GradeColors = new() { ["Epic"] = "#FFA568", ["Unique"] = "#ECC057", ["Legend"] = "#77B4FF", ["Rare"] = "#6ADA84", ["Special"] = "#69DFE0", ["Common"] = "#AAB4BE" };
    // Combat gear first, in the order of the game's character window; accessories and runes after; wings last.
    static readonly string[] SlotOrder = ["MainHand", "SubHand", "Helmet", "Torso", "Shoulder", "Gloves", "Pants", "Boots", "Cape", "Belt",
        "Necklace", "Earring1", "Earring2", "Ring1", "Ring2", "Bracelet1", "Bracelet2", "Amulet", "Rune1", "Rune2", "Wing",
        "Arcana1", "Arcana2", "Arcana3", "Arcana4", "Arcana5"];
    static readonly Brush Muted = Paint("#95A4B6"), Gold = Paint("#E9C77E"), Teal = Paint("#71DED6"), Dim = Paint("#5F7480"), Cell = Paint("#141F28");
    static readonly Dictionary<string, ImageSource?> images = [];
    readonly Image portrait = new() { Width = 46, Height = 46, Stretch = Stretch.UniformToFill };
    readonly TextBlock name = Label("", 14, Brushes.White), line = Label("", 10.5, Muted), title = Label("", 10.5, Gold), status = Label("", 10.5, Muted);
    readonly TextBlock power = Label("…", 20, Teal), item = Label("…", 13, Gold), weapon = Label("", 11, Brushes.White), ranks = Label("", 10.5, Gold), footer = Label("", 9.5, Dim);
    readonly UniformGrid gear = new() { Columns = 7, Margin = new Thickness(0, 6, 0, 0) };
    string? shownKey, shownStage;
    PlayerCard? shownCard;
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    public PlayerWindow()
    {
        Title = "FullAIon2Companion · Jugador"; Width = 300; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowActivated = false; ShowInTaskbar = false;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        name.FontWeight = FontWeights.SemiBold; power.FontWeight = FontWeights.SemiBold;
        var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        who.Children.Add(name); who.Children.Add(line);
        var frame = new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1), BorderBrush = Gold, Margin = new Thickness(0, 0, 10, 0), Child = new Border { CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = portrait } };
        var head = new DockPanel { Margin = new Thickness(0, 4, 0, 6) };
        DockPanel.SetDock(frame, Dock.Left); head.Children.Add(frame); head.Children.Add(who);
        var numbers = new StackPanel { Orientation = Orientation.Horizontal };
        numbers.Children.Add(power); numbers.Children.Add(Label("  PC     ", 11, Muted)); numbers.Children.Add(item); numbers.Children.Add(Label("  PE", 11, Muted));
        ranks.TextWrapping = TextWrapping.Wrap; footer.TextWrapping = TextWrapping.Wrap; status.TextWrapping = TextWrapping.Wrap;
        footer.Margin = new Thickness(0, 6, 0, 0); weapon.Margin = new Thickness(0, 4, 0, 0);
        var panel = new StackPanel();
        verdict.Child = verdictText;
        var top = new Grid();
        top.Children.Add(Label("JUGADOR · PERFIL OFICIAL", 10, Gold)); top.Children.Add(verdict);
        foreach (var e in new UIElement[] { top, head, numbers, matchup, title, ranks, weapon, gear, status, footer }) panel.Children.Add(e);
        Content = new Border { Background = new SolidColorBrush(Color.FromArgb(209, 13, 20, 27)), BorderBrush = new SolidColorBrush(Color.FromArgb(200, 51, 87, 91)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 9, 12, 9), Child = panel };
        // Takes the mouse (item details on hover) but never the keyboard focus: no WS_EX_TRANSPARENT, still NOACTIVATE.
        SourceInitialized += (_, _) => { var hwnd = new WindowInteropHelper(this).Handle; SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x08000000 | 0x80); };
    }
    public void ApplyAppearance(byte alpha, double k)
    {
        var border = (Border)Content;
        border.Background = new SolidColorBrush(Color.FromArgb(alpha, 13, 20, 27));
        border.LayoutTransform = new ScaleTransform(k, k); Width = 300 * k;
    }
    static Brush Paint(string color) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); b.Freeze(); return b; }
    static TextBlock Label(string text, double size, Brush color) => new() { Text = text, FontSize = size, Foreground = color, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    // Remote icons are downloaded once per session and reused across players (most wear the same few sets).
    static ImageSource? Remote(string? url)
    {
        if (string.IsNullOrEmpty(url) || !url.StartsWith("https://", StringComparison.Ordinal)) return null;
        if (images.TryGetValue(url, out var cached)) return cached;
        ImageSource? image = null;
        try { var b = new BitmapImage(); b.BeginInit(); b.UriSource = new Uri(url); b.DecodePixelWidth = 64; b.EndInit(); image = b; } catch { }
        return images[url] = image;
    }
    /// <summary>Renders the lookup as far as it has got: searching, basic (class, level, portrait), or the full card.</summary>
    readonly Border verdict = new() { CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 1), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed };
    readonly TextBlock verdictText = new() { FontSize = 10, FontWeight = FontWeights.Bold }, matchup = new() { FontSize = 10.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
    PlayerCard? shownMine;
    public void Load(string key, string playerName, ProfileState state, PlayerCard? mine = null)
    {
        if (key == shownKey && state.Stage == shownStage && ReferenceEquals(state.Card, shownCard) && ReferenceEquals(mine, shownMine)) return;
        shownKey = key; shownStage = state.Stage; shownCard = state.Card; shownMine = mine;
        ShowVerdict(state.Card, mine);
        if (state.Card is { } p)
        {
            portrait.Source = Remote(p.Portrait);
            name.Text = p.Name;
            line.Text = $"{L.T(p.ClassName)} · Nv. {p.Level}{(p.Legion is null ? "" : " · " + p.Legion)} · {p.Server}";
            power.Text = p.CombatPower >= 1000 ? $"{p.CombatPower / 1000.0:0.0}K" : p.CombatPower.ToString("N0");
            item.Text = p.ItemLevel?.ToString("N0") ?? "—";
            title.Text = p.Title ?? ""; title.Visibility = p.Title is null ? Visibility.Collapsed : Visibility.Visible;
            // PvP ranking when NCSOFT publishes it; else the place in the server's combat-power ranking (top 50).
            int? powerRank = ServerRankings.RankOf(p.Name, p.ServerId, p.Region);
            var parts = new List<string>();
            if (p.Rankings.Count > 0) parts.Add("JcJ: " + string.Join(" · ", p.Rankings));
            else parts.Add("JcJ: sin clasificación publicada por NCSOFT");
            if (powerRank is int place) parts.Add($"#{place} en poder de combate de {p.Server}");
            ranks.Text = string.Join("   ·   ", parts);
            ranks.Foreground = p.Rankings.Count > 0 || powerRank is not null ? Gold : Muted;
            ranks.Visibility = Visibility.Visible;
            var main = p.Gear.FirstOrDefault(g => g.Slot == "MainHand");
            weapon.Text = main is null ? "" : $"{main.Name}{(main.Enchant > 0 ? $" +{main.Enchant}" : "")}";
            weapon.Foreground = main is null ? Muted : Paint(GradeColors.GetValueOrDefault(main.Grade, "#DCE1E9"));
            gear.Children.Clear();
            foreach (var g in p.Gear.Where(g => Array.IndexOf(SlotOrder, g.Slot) >= 0).OrderBy(g => Array.IndexOf(SlotOrder, g.Slot))) gear.Children.Add(Tile(p, g));
            status.Visibility = state.Stale ? Visibility.Visible : Visibility.Collapsed;
            status.Text = "Actualizando…";
            footer.Text = $"NCSOFT · {(DateTimeOffset.Now - p.FetchedAt).TotalMinutes switch { < 1 => "ahora", var m and < 60 => $"hace {m:0} min", var m => $"hace {m / 60:0} h" }}";
            return;
        }
        // No card yet: whatever the search already gave, and where the lookup is.
        portrait.Source = Remote(state.Portrait);
        name.Text = playerName;
        line.Text = state.Level > 0 ? $"Nv. {state.Level} · {state.Server}" : "";
        power.Text = item.Text = "…"; title.Visibility = ranks.Visibility = Visibility.Collapsed; weapon.Text = ""; gear.Children.Clear();
        status.Visibility = Visibility.Visible;
        status.Text = state.Stage switch
        {
            "search" => "Buscando en NCSOFT…",
            "basic" => "Cargando equipo y poder de combate…",
            "missing" => "No aparece en la búsqueda oficial de su servidor (nombre recién cambiado u otro servidor).",
            _ => "No se pudo consultar: " + state.Error,
        };
        footer.Text = "";
    }
    // Who wins, the way the website's comparison weighs it: the combat power gap, and in PvP the four attributes it
    // compares for that mode (STR, AGI, DEX, WIS). An estimate from official data - class matchups and play still decide.
    static readonly string[] PvpStats = ["STR", "AGI", "DEX", "WIS"];
    void ShowVerdict(PlayerCard? them, PlayerCard? me)
    {
        if (them is null || me is null || me.Name.Equals(them.Name, StringComparison.OrdinalIgnoreCase) || me.CombatPower <= 0 || them.CombatPower <= 0)
        { verdict.Visibility = matchup.Visibility = Visibility.Collapsed; return; }
        double ratio = (double)me.CombatPower / them.CombatPower;
        (string text, string color) = ratio >= 1.10 ? ("LO GANAS", "#3FBF7F") : ratio <= 0.90 ? ("TE GANA", "#E0453A") : ("PAREJO", "#E9C77E");
        verdictText.Text = text; verdictText.Foreground = Brushes.White;
        verdict.Background = Paint(color); verdict.Visibility = Visibility.Visible;
        long gap = me.CombatPower - them.CombatPower;
        string ahead = "", behind = "";
        if (me.Stats is { } a && them.Stats is { } b)
        {
            ahead = string.Join(", ", PvpStats.Where(t => a.ContainsKey(t) && b.ContainsKey(t) && a[t].Value > b[t].Value).Select(t => a[t].Name));
            behind = string.Join(", ", PvpStats.Where(t => a.ContainsKey(t) && b.ContainsKey(t) && a[t].Value < b[t].Value).Select(t => a[t].Name));
        }
        matchup.Text = $"Tu PC {Short(me.CombatPower)} vs {Short(them.CombatPower)} ({(gap >= 0 ? "+" : "")}{(ratio - 1) * 100:0}%)"
            + (ahead.Length > 0 ? $" · ventaja en {ahead}" : "") + (behind.Length > 0 ? $" · desventaja en {behind}" : "");
        matchup.Foreground = Paint(color); matchup.Visibility = Visibility.Visible;
    }
    static string Short(long v) => v >= 1000 ? $"{v / 1000.0:0.0}K" : v.ToString("N0");
    // The hover card, always above the item (below it would cover the middle of the game).
    readonly Popup popup = new() { Placement = PlacementMode.Top, AllowsTransparency = true, StaysOpen = true, VerticalOffset = -6 };
    GearRow? detailFor;
    async void ShowDetail(FrameworkElement tile, PlayerCard card, GearRow g)
    {
        detailFor = g;
        // A popup is not in the windows L.ApplyAll walks: its card is translated here.
        popup.PlacementTarget = tile; popup.Child = DetailCard(g, null, loading: true); L.Apply(popup.Child); popup.IsOpen = true;
        JsonElement? detail = null;
        try { detail = await PlayerProfiles.ItemDetail(card, g); } catch { }
        if (detailFor != g) return;
        popup.Child = DetailCard(g, detail, loading: false); L.Apply(popup.Child);
    }
    internal static UIElement DetailCard(GearRow g, JsonElement? d, bool loading)
    {
        Brush grade = Paint(GradeColors.GetValueOrDefault(g.Grade, "#DCE1E9"));
        static string S(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind is JsonValueKind.String or JsonValueKind.Number ? v.ToString() : "";
        var stack = new StackPanel { Width = 290 };

        // Header: icon in its grade frame, name, kind line, enchant bar.
        var icon = new Border { Width = 50, Height = 50, CornerRadius = new CornerRadius(7), BorderBrush = grade, BorderThickness = new Thickness(1.5), Background = Cell, Margin = new Thickness(0, 0, 10, 0),
            Child = new Image { Source = Remote(d is { } h ? S(h, "icon") : g.Icon) ?? Remote(g.Icon), Margin = new Thickness(3) } };
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = g.Name, Foreground = grade, FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (d is { } k) titles.Children.Add(new TextBlock { Text = $"{S(k, "gradeName")} · {S(k, "categoryName")} · Nv. {S(k, "level")}", Foreground = Muted, FontSize = 10.5, Margin = new Thickness(0, 2, 0, 0) });
        int max = d is { } m && int.TryParse(S(m, "maxEnchantLevel"), out int mx) ? mx : 0;
        if (g.Enchant > 0 || max > 0)
        {
            var bar = new Grid { Height = 5, Margin = new Thickness(0, 5, 0, 0) };
            bar.Children.Add(new Border { Background = Paint("#22313C"), CornerRadius = new CornerRadius(3) });
            if (max > 0) bar.Children.Add(new Border { Background = Teal, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = 160.0 * Math.Min(1, (double)g.Enchant / max) });
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 0), Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
            var label = new TextBlock { Text = max > 0 ? $"+{g.Enchant} / {max}" : $"+{g.Enchant}", Foreground = Teal, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Bottom };
            DockPanel.SetDock(label, Dock.Right); row.Children.Add(label); row.Children.Add(bar);
            titles.Children.Add(row);
        }
        var header = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left); header.Children.Add(icon); header.Children.Add(titles);
        stack.Children.Add(header);

        if (d is not { } x)
        {
            stack.Children.Add(new TextBlock { Text = loading ? "Cargando detalle oficial…" : "Detalle no disponible todavía: el perfil se está actualizando.", Foreground = Muted, FontSize = 10.5, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap });
        }
        else
        {
            void Section(string title)
            {
                stack.Children.Add(new Border { Height = 1, Background = Paint("#2B3C48"), Margin = new Thickness(0, 8, 0, 5) });
                stack.Children.Add(new TextBlock { Text = title, Foreground = Gold, FontSize = 9.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
            }
            void Stat(string name, string value, Brush nameColor, Brush valueColor, ImageSource? image = null, string? extra = null)
            {
                var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                if (extra is { Length: > 0 } && extra != "0")
                {
                    var bonus = new TextBlock { Text = $"  +{extra}", Foreground = Paint("#6ADA84"), FontSize = 11 };
                    DockPanel.SetDock(bonus, Dock.Right); row.Children.Add(bonus);
                }
                var val = new TextBlock { Text = value, Foreground = valueColor, FontSize = 11, FontWeight = FontWeights.SemiBold };
                DockPanel.SetDock(val, Dock.Right); row.Children.Add(val);
                if (image is not null) { var img = new Image { Source = image, Width = 15, Height = 15, Margin = new Thickness(0, 0, 6, 0) }; DockPanel.SetDock(img, Dock.Left); row.Children.Add(img); }
                row.Children.Add(new TextBlock { Text = name, Foreground = nameColor, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
                stack.Children.Add(row);
            }
            if (x.TryGetProperty("mainStats", out var main) && main.GetArrayLength() > 0)
            {
                Section("ESTADÍSTICAS");
                foreach (var st in main.EnumerateArray()) Stat(S(st, "name"), S(st, "value"), Paint("#C3CFD9"), Brushes.White, extra: S(st, "extra"));
            }
            if (x.TryGetProperty("subStats", out var sub) && sub.GetArrayLength() > 0)
            {
                Section("ADICIONALES");
                foreach (var st in sub.EnumerateArray()) Stat(S(st, "name"), "+" + S(st, "value"), Paint("#C3CFD9"), Teal);
            }
            if (x.TryGetProperty("magicStoneStat", out var stones) && stones.GetArrayLength() > 0)
            {
                Section($"PIEDRAS DE MANÁ · {stones.GetArrayLength()}/{S(x, "magicStoneSlotCount")}");
                foreach (var st in stones.EnumerateArray())
                {
                    var c = Paint(GradeColors.GetValueOrDefault(S(st, "grade"), "#C3CFD9"));
                    Stat(S(st, "name"), S(st, "value"), c, c, Remote(S(st, "icon")));
                }
            }
            // An arcana: its four skills (with the levels they add) and its set, the effects lit up from the pieces worn.
            if (x.TryGetProperty("subSkills", out var skills) && skills.ValueKind == JsonValueKind.Array && skills.GetArrayLength() > 0)
            {
                Section("HABILIDADES");
                foreach (var skill in skills.EnumerateArray()) Stat(S(skill, "name"), "Nv. +" + S(skill, "level"), Paint("#C3CFD9"), Teal, Remote(S(skill, "icon")));
            }
            if (x.TryGetProperty("set", out var set) && set.ValueKind == JsonValueKind.Object && set.TryGetProperty("bonuses", out var bonuses) && bonuses.ValueKind == JsonValueKind.Array)
            {
                int worn = int.TryParse(S(set, "equippedCount"), out int w) ? w : 0;
                Section($"SET · {S(set, "name")} · {worn}/5");
                foreach (var b in bonuses.EnumerateArray())
                {
                    bool on = int.TryParse(S(b, "degree"), out int degree) && worn >= degree;
                    string text = b.TryGetProperty("descriptions", out var t) && t.ValueKind == JsonValueKind.Array ? string.Join(" ", t.EnumerateArray().Select(e => e.GetString())) : "";
                    stack.Children.Add(new TextBlock { Text = $"{degree} · {text}", Foreground = on ? Paint("#6ADA84") : Dim, FontSize = 10.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 2) });
                }
            }
            if (x.TryGetProperty("godStoneStat", out var gods) && gods.GetArrayLength() > 0)
            {
                Section("PIEDRA SAGRADA");
                foreach (var st in gods.EnumerateArray())
                {
                    var c = Paint(GradeColors.GetValueOrDefault(S(st, "grade"), "#C3CFD9"));
                    Stat(S(st, "name"), "", c, c, Remote(S(st, "icon")));
                    stack.Children.Add(new TextBlock { Text = S(st, "desc"), Foreground = Muted, FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(21, 0, 0, 2) });
                }
            }
        }
        return new Border { Background = Paint("#F50B1117"), BorderBrush = grade, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(12, 10, 12, 11), Child = stack,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = .55, Color = Colors.Black } };
    }
    // One gear piece: icon in a frame of its grade colour, enchant level in the corner; hover for the official detail.
    UIElement Tile(PlayerCard card, GearRow g)
    {
        var grid = new Grid { Width = 36, Height = 36, Margin = new Thickness(1.5), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        grid.MouseEnter += (_, _) => ShowDetail(grid, card, g);
        grid.MouseLeave += (_, _) => { if (detailFor == g) { popup.IsOpen = false; detailFor = null; } };
        grid.Children.Add(new Border { Background = Cell, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1.5), BorderBrush = Paint(GradeColors.GetValueOrDefault(g.Grade, "#55626A")),
            Child = new Image { Source = Remote(g.Icon), Margin = new Thickness(2) } });
        if (g.Enchant > 0)
            grid.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(220, 8, 12, 16)), CornerRadius = new CornerRadius(3), Padding = new Thickness(2, 0, 2, 0),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 1, 1),
                Child = new TextBlock { Text = "+" + g.Enchant, FontSize = 9.5, FontWeight = FontWeights.Bold, Foreground = Teal } });
        return grid;
    }
}
