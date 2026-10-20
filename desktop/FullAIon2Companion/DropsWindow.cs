using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace FullAIon2Companion;

/// <summary>The target's loot table from the local catalog, beside the overlay: objects (souls, materials, chests) first,
/// then the gear, where the armour and the weapons of one series and grade share a row («Equipo de tormenta», «Armas de
/// tormenta»): hovering it shows its pieces, a click pins them. A monster without a table shows what it gave the player
/// (<see cref="LootBook"/>). Takes the mouse like the player card in the same place, never the keyboard focus. Item names
/// and series follow the interface language (<see cref="InventoryWatch.NameOf"/>, <see cref="LocalSeries"/>).</summary>
public sealed class DropsWindow : Window
{
    const int Shown = 16;
    internal static readonly Dictionary<string, (int Rank, string Color)> Grades = new()
    {
        ["Epic"] = (0, "#FFA568"), ["Unique"] = (1, "#ECC057"), ["Legend"] = (2, "#77B4FF"), ["Rare"] = (3, "#6ADA84"), ["Special"] = (4, "#69DFE0"), ["Common"] = (5, "#DCE1E9"),
    };
    static readonly Brush Muted = Paint("#95A4B6"), Gold = Paint("#E9C77E"), Teal = Paint("#71DED6"), Dim = Paint("#5F7480");
    // Pieces whose series adjective agrees with «Equipo» (masculine singular) or, made plural, with «Armas».
    static readonly HashSet<string> Masculine = ["Yelmo", "Peto"], Feminine = ["Daga", "Espada larga", "Maza", "Arma de puño"];
    // The same in French and Portuguese, by the Spanish category: «Armure» agrees with the breastplate and the cloak (Cuirasse,
    // Cape), «Equipamento» with the helmet and the breastplate (Elmo, Peitoral), «Armes» and «Armas» with the dagger, the
    // longsword and the mace (Dague, Épée, Masse; Adaga, Espada Longa, Maça; the fist weapon is masculine there).
    static readonly HashSet<string> FeminineArmour = ["Peto", "Capa"], FeminineWeapons = ["Daga", "Espada larga", "Maza"];
    readonly TextBlock title = Label("", 12.5, Brushes.White), subtitle = Label("", 10.5, Muted), footer = Label("", 10, Dim);
    readonly StackPanel rows = new();
    // The pieces of a group, to the side of the panel.
    readonly Popup popup = new() { Placement = PlacementMode.Right, AllowsTransparency = true, StaysOpen = true };
    readonly List<Group> groups = [];
    Group? pinned, showing;
    double scale = 1;
    int? shownNpc;
    string shownLanguage = "";
    bool shownResult, shownLearned;
    int shownVersion;
    sealed record Loot(JsonElement Item, int Rank, string Color, double? Chance);
    sealed record Group(string Title, int Rank, string Color, List<Loot> Items);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    public DropsWindow(double opacity)
    {
        Title = "FullAIon2Companion · Botín"; Width = 300; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowActivated = false; ShowInTaskbar = false;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        title.FontWeight = FontWeights.SemiBold;
        var head = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        head.Children.Add(Label("BOTÍN DEL OBJETIVO", 10, Gold));
        head.Children.Add(title); head.Children.Add(subtitle);
        footer.TextWrapping = TextWrapping.Wrap; footer.Margin = new Thickness(0, 6, 0, 0);
        var panel = new StackPanel();
        panel.Children.Add(head); panel.Children.Add(rows); panel.Children.Add(footer);
        byte alpha = (byte)Math.Clamp(opacity * 255, 90, 250);
        Content = new Border { Background = new SolidColorBrush(Color.FromArgb(alpha, 13, 20, 27)), BorderBrush = new SolidColorBrush(Color.FromArgb(200, 51, 87, 91)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 9, 12, 9), Child = panel };
        SourceInitialized += (_, _) => { var hwnd = new WindowInteropHelper(this).Handle; SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x08000000 | 0x80); };
        IsVisibleChanged += (_, _) => { if (!IsVisible) { pinned = null; HideDetail(); } };
    }
    public void ApplyAppearance(byte alpha, double k)
    {
        var border = (Border)Content;
        border.Background = new SolidColorBrush(Color.FromArgb(alpha, 13, 20, 27));
        border.LayoutTransform = new ScaleTransform(k, k); Width = 300 * k; scale = k;
    }
    static Brush Paint(string color) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); b.Freeze(); return b; }
    static TextBlock Label(string text, double size, Brush color) => new() { Text = text, FontSize = size, Foreground = color, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    static string Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
    static int Id(JsonElement item) => item.TryGetProperty("id", out var v) && v.TryGetInt32(out int n) ? n : 0;
    // A loot table's item in the interface language (the table's own Spanish name in Spanish).
    static string ItemName(Loot d) => InventoryWatch.NameOf(Id(d.Item), Text(d.Item, "name"));
    static double? Chance(JsonElement e) => e.TryGetProperty("chance", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetDouble() : null;
    static string Num(double p) => p >= 1 ? $"{p:0.#}" : $"{p:0.##}";
    /// <summary>True when the panel has something for the NPC: its catalog loot table or, for a monster without one, what
    /// it gave the player (that card follows each new kill).</summary>
    public bool Load(int? npc)
    {
        if (npc == shownNpc && L.Language == shownLanguage && !(shownLearned && (LootBook.Live?.Version ?? 0) != shownVersion)) return shownResult;
        shownNpc = npc; shownLanguage = L.Language; shownLearned = false; rows.Children.Clear(); groups.Clear(); pinned = null; HideDetail();
        return shownResult = Build(npc);
    }
    bool Build(int? npc)
    {
        if (npc is not int id || Catalog.Npc(id) is not { } record) return false;
        // No loot table of its own: the same creature's variant of nearest level, said so in the subtitle.
        int? borrowed = null;
        if (!(record.TryGetProperty("drops", out var drops) && drops.ValueKind == JsonValueKind.Array && drops.GetArrayLength() > 0)
            && Catalog.DropsSibling(id) is int sibling && Catalog.Npc(sibling) is { } other && other.TryGetProperty("drops", out drops) && drops.ValueKind == JsonValueKind.Array)
            borrowed = sibling;
        if (drops.ValueKind != JsonValueKind.Array || drops.GetArrayLength() == 0) return Learned(id, record);
        // Best grade first, then the likeliest: what is worth staying for is on top.
        var items = drops.EnumerateArray().Select(d => Grades.TryGetValue(Text(d, "grade"), out var g) ? new Loot(d, g.Rank, g.Color, Chance(d)) : new Loot(d, 9, "#DCE1E9", Chance(d)))
            .OrderBy(x => x.Rank).ThenByDescending(x => x.Chance ?? -1).ToList();
        title.Text = Catalog.NpcName(id);
        subtitle.Text = $"{items.Count} objetos · {items.Count(x => x.Rank <= 1)} únicos o mejores"
            + (borrowed is int from && Catalog.Npc(from) is { } fromRecord && fromRecord.TryGetProperty("level", out var lv) ? $" · botín de su variante Nv. {lv}" : "");
        var lines = Lines(items);
        groups.AddRange(lines.OfType<Group>());
        // Objects (souls, materials, chests) first, then the gear: a list cut short loses gear, never a soul.
        bool? gearPart = null;
        foreach (var line in lines.Take(Shown))
        {
            bool gear = IsGear(line);
            if (gear != gearPart) { rows.Children.Add(Section(gear ? "EQUIPO" : "OBJETOS", first: gearPart is null)); gearPart = gear; }
            rows.Children.Add(line is Group group ? GroupRow(group) : ItemRow((Loot)line));
        }
        footer.Text = lines.Count > Shown ? $"+{lines.Count - Shown} más · la lista completa está en el panel ⚙ → Objetivo → Ficha"
            : groups.Count > 0 ? "Probabilidades del catálogo, no de esta captura. Pasa el mouse por un grupo para ver sus objetos." : "Probabilidades del catálogo, no de esta captura.";
        return items.Count > 0;
    }
    // A monster the catalog has no loot table for (three of every four): what it gave the player when killed on this
    // computer (LootBook); «3/40» = in 3 of 40 kills.
    bool Learned(int id, JsonElement record)
    {
        if (!(record.TryGetProperty("isMonster", out var monster) && monster.ValueKind == JsonValueKind.True)) return false;
        shownLearned = true; shownVersion = LootBook.Live?.Version ?? 0;
        var (kills, seen) = LootBook.Live is { } book ? book.Of(id) : (0, (IReadOnlyList<LootBook.Seen>)[]);
        title.Text = Catalog.NpcName(id);
        subtitle.Text = kills > 0 ? $"Sin botín en el catálogo · {kills} muertes tuyas" : "Sin botín en el catálogo";
        if (seen.Count > 0) rows.Children.Add(Section("LO QUE TE HA DADO", first: true));
        foreach (var s in seen.Take(Shown)) rows.Children.Add(SeenRow(s, kills));
        footer.Text = seen.Count > 0 ? "Lo que vio la app al matarla en este equipo: 3/40 es en 3 de 40 muertes."
            : kills > 0 ? "Al matarla con la app abierta no te ha dado ningún objeto." : "Lo que te dé al matarla con la app abierta aparecerá aquí.";
        return true;
    }
    static UIElement SeenRow(LootBook.Seen s, int kills)
    {
        var info = InventoryWatch.Info(s.Item);
        var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1), Height = 24 };
        var icon = new Image { Width = 22, Height = 22, Margin = new Thickness(0, 0, 7, 0), Source = OverlayWindow.Icon(info.Icon) };
        DockPanel.SetDock(icon, Dock.Left); row.Children.Add(icon);
        var odds = Label($"{s.Times}/{kills}", 10.5, Teal); odds.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(odds, Dock.Right); row.Children.Add(odds);
        // Several of one item over the kills: how many in all.
        row.Children.Add(Label(s.Quantity > s.Times ? $"{info.Name} ×{s.Quantity}" : info.Name, 11, Paint(Grades.TryGetValue(info.Grade, out var g) ? g.Color : "#DCE1E9")));
        return row;
    }
    static TextBlock Section(string text, bool first) => new() { Text = text, FontSize = 9, FontWeight = FontWeights.SemiBold, Foreground = Gold, Opacity = .75, Margin = new Thickness(0, first ? 0 : 6, 0, 2) };
    static string? KindOf(JsonElement item) => (item.TryGetProperty("id", out var v) && v.TryGetInt64(out long n) ? n / 10_000_000 : 0) switch { 21 => "Equipo", 11 => "Armas", _ => null };
    static bool IsGear(object line) => line is Group || KindOf(((Loot)line).Item) is not null;
    // Armour (ids 21…) and weapons (11…) of one series: same grade and the same last five digits of the id (tier and
    // series: 210550008 Guantes de tormenta, 110150008 Espadón de tormenta), apart when their «de …» differs
    // («Guantes del orden» and «Botas de orden» stay together: the catalog mixes «de» and «del» within a set).
    static List<object> Lines(List<Loot> items)
    {
        var buckets = new List<(string? Kind, List<(Loot Loot, string Tail)> Members)>();
        var index = new Dictionary<(string, int, long, string), int>();
        foreach (var d in items)
        {
            long id = d.Item.TryGetProperty("id", out var v) && v.TryGetInt64(out long n) ? n : 0;
            if (KindOf(d.Item) is not string kind) { buckets.Add((null, [(d, "")])); continue; }
            string tail = Tail(Text(d.Item, "name"), Text(d.Item, "category"));
            var key = (kind, d.Rank, id % 100_000, IsOf(tail) ? tail[(tail.IndexOf(' ') + 1)..].ToLowerInvariant() : "");
            if (index.TryGetValue(key, out int at)) buckets[at].Members.Add((d, tail));
            else { index[key] = buckets.Count; buckets.Add((kind, [(d, tail)])); }
        }
        // A series of one piece stays a plain row; objects before gear, each with the best grade first, then the likeliest.
        return buckets.Select(b => b.Kind is string kind && b.Members.Count > 1
                ? (object)new Group(SeriesName(kind, b.Members), b.Members[0].Loot.Rank, b.Members[0].Loot.Color, b.Members.Select(m => m.Loot).ToList())
                : b.Members[0].Loot)
            .OrderBy(IsGear)
            .ThenBy(x => x is Group g ? g.Rank : ((Loot)x).Rank)
            .ThenByDescending(x => x is Group g ? g.Items[0].Chance ?? -1 : ((Loot)x).Chance ?? -1).ToList();
    }
    static string SeriesName(string kind, List<(Loot Loot, string Tail)> members)
    {
        if (L.Language != "es" && LocalSeries(kind, members.Select(m => m.Loot).ToList()) is string local) return local;
        string first = members[0].Tail, usual = members.GroupBy(m => m.Tail).OrderByDescending(g => g.Count()).First().Key;
        if (IsOf(usual)) return $"{kind} {usual}";
        // An adjective agrees: «Equipo olvidado» from the helmet, «Armas olvidadas» from the dagger.
        string? Of(HashSet<string> categories) => members.Where(m => categories.Contains(Text(m.Loot.Item, "category"))).Select(m => m.Tail).FirstOrDefault();
        return kind == "Equipo" ? $"Equipo {Of(Masculine) ?? first}" : $"Armas {Plural(Of(Feminine) ?? first)}";
    }
    static bool IsOf(string tail) => tail.StartsWith("de ", StringComparison.OrdinalIgnoreCase) || tail.StartsWith("del ", StringComparison.OrdinalIgnoreCase);
    // The series in the interface language, from its pieces' names there (the group itself is the Spanish one). English puts
    // it first («Tempest Gloves», «Tempest Helm» → «Tempest armor»); Portuguese and French after the piece, mostly as a «de …»
    // («Luvas da Tempestade» → «Equipamento da Tempestade», «Gants de la tempête» → «Armure de la tempête»), else as an
    // adjective that agrees, taken from a piece of the head word's gender («Cuirasse oubliée» → «Armure oubliée», «Dague
    // oubliée» → «Armes oubliées»). A piece without a Portuguese or French name puts the series in English, as its pieces
    // show. Null when the names share nothing: the Spanish title stays, for the patterns of i18n/patterns.tsv.
    static string? LocalSeries(string kind, List<Loot> pieces)
    {
        string language = pieces.All(p => InventoryWatch.OwnName(Id(p.Item), L.Language) is not null) ? L.Language : "en";
        var names = pieces.Select(p => InventoryWatch.OwnName(Id(p.Item), language)).OfType<string>().ToList();
        if (names.Count < pieces.Count || names.Distinct().Count() < 2) return null;
        var words = names.Select(n => n.Split(' ')).ToList();
        if (language == "en")
        {
            int same = 0;
            while (words.All(w => same < w.Length - 1 && w[same] == words[0][same])) same++;
            return same > 0 ? $"{string.Join(' ', words[0][..same])} {(kind == "Equipo" ? "armor" : "weapons")}" : null;
        }
        string head = (language, kind) switch { ("pt", "Equipo") => "Equipamento", ("pt", _) => "Armas", (_, "Equipo") => "Armure", _ => "Armes" };
        // The «de …» most pieces share: one piece named apart does not undo the series.
        var usual = words.Select(OfPart).OfType<string>().GroupBy(t => t).OrderByDescending(g => g.Count()).FirstOrDefault();
        if (usual is not null && usual.Count() * 2 >= names.Count) return $"{head} {usual.Key}";
        var agree = kind == "Equipo" ? language == "pt" ? Masculine : FeminineArmour : FeminineWeapons;
        if (pieces.FirstOrDefault(p => agree.Contains(Text(p.Item, "category"))) is not { } piece
            || InventoryWatch.OwnName(Id(piece.Item), language)!.Split(' ') is not { Length: > 1 } named) return null;
        string adjective = named[^1];
        return $"{head} {(kind == "Armas" && !adjective.EndsWith('s') && !adjective.EndsWith('x') ? adjective + "s" : adjective)}";
    }
    // A Portuguese or French name's «de …» part, after the piece: «Espada Longa da Sombra» → «da Sombra», «Garde-bras d'illusion» → «d'illusion».
    static string? OfPart(string[] words)
    {
        for (int i = 1; i < words.Length; i++)
            if (words[i] is "de" or "da" or "do" or "das" or "dos" or "du" or "des" || words[i].StartsWith("d'") || words[i].StartsWith("d’"))
                return string.Join(' ', words[i..]);
        return null;
    }
    // The piece's name without its kind: «Guantes de tormenta» → «de tormenta», «Puñal de sabiduría» (a dagger) → «de sabiduría».
    static string Tail(string name, string category)
    {
        if (category.Length > 0 && name.StartsWith(category, StringComparison.OrdinalIgnoreCase)) return name[category.Length..].Trim();
        foreach (var of in new[] { " del ", " de " }) { int at = name.IndexOf(of, StringComparison.Ordinal); if (at > 0) return name[(at + 1)..]; }
        int space = name.IndexOf(' ');
        return space > 0 ? name[(space + 1)..] : name;
    }
    static string Plural(string phrase)
    {
        int space = phrase.IndexOf(' ');
        string word = space < 0 ? phrase : phrase[..space], rest = space < 0 ? "" : phrase[space..];
        return word.Length == 0 ? phrase : word + ("aeiouáéíóú".Contains(char.ToLowerInvariant(word[^1])) ? "s" : "es") + rest;
    }
    static string Odds(Group g)
    {
        var known = g.Items.Select(i => i.Chance).OfType<double>().ToList();
        if (known.Count == 0) return "—";
        string low = Num(known.Min()), high = Num(known.Max());
        return low == high ? $"{high}% c/u" : $"{low}–{high}%";
    }
    // Any piece of the group in one kill, each piece taken as its own roll.
    static double Any(Group g) => 100 * (1 - g.Items.Aggregate(1.0, (left, d) => left * (1 - (d.Chance ?? 0) / 100)));
    static UIElement ItemRow(Loot d)
    {
        var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1), Height = 24 };
        var icon = new Image { Width = 22, Height = 22, Margin = new Thickness(0, 0, 7, 0), Source = OverlayWindow.Icon(Text(d.Item, "icon")) };
        DockPanel.SetDock(icon, Dock.Left); row.Children.Add(icon);
        var odds = Label(d.Chance is double p ? Num(p) + "%" : "—", 10.5, Teal);
        odds.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(odds, Dock.Right); row.Children.Add(odds);
        row.Children.Add(Label(ItemName(d), 11, Paint(d.Color)));
        return row;
    }
    UIElement GroupRow(Group g)
    {
        var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1), Height = 24, Background = Brushes.Transparent, Cursor = Cursors.Hand };
        // The likeliest piece's icon, with the number of pieces in its corner.
        var icon = new Grid { Width = 22, Height = 22, Margin = new Thickness(0, 0, 7, 0) };
        icon.Children.Add(new Image { Source = OverlayWindow.Icon(Text(g.Items[0].Item, "icon")) });
        icon.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(225, 8, 12, 16)), CornerRadius = new CornerRadius(3), Padding = new Thickness(2, 0, 2, 0), Margin = new Thickness(0, 0, -3, -2),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Child = new TextBlock { Text = g.Items.Count.ToString(), FontSize = 8.5, FontWeight = FontWeights.Bold, Foreground = Brushes.White } });
        DockPanel.SetDock(icon, Dock.Left); row.Children.Add(icon);
        var more = Label("›", 14, Dim); more.Margin = new Thickness(6, -3, 0, 0);
        DockPanel.SetDock(more, Dock.Right); row.Children.Add(more);
        var odds = Label(Odds(g), 10.5, Teal); odds.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(odds, Dock.Right); row.Children.Add(odds);
        var name = Label(g.Title, 11, Paint(g.Color)); name.FontWeight = FontWeights.SemiBold;
        row.Children.Add(name);
        row.MouseEnter += (_, _) => { if (pinned is null) ShowDetail(row, g); };
        row.MouseLeave += (_, _) => { if (pinned is null && showing == g) HideDetail(); };
        row.MouseLeftButtonDown += (_, e) => { e.Handled = true; pinned = pinned == g ? null : g; if (pinned is null) HideDetail(); else ShowDetail(row, g); };
        return row;
    }
    void ShowDetail(FrameworkElement row, Group g)
    {
        showing = g;
        // Past the panel's padding and border, level with the row.
        popup.PlacementTarget = row; popup.HorizontalOffset = 13 * scale + 6; popup.VerticalOffset = -8 * scale;
        popup.Child = Detail(g, pinned == g, scale);
        popup.IsOpen = true;
    }
    void HideDetail() { popup.IsOpen = false; showing = null; }
    // Its texts go through L.T here: a popup is not in the windows L.ApplyAll walks.
    static FrameworkElement Detail(Group g, bool pinned, double k)
    {
        var grade = Paint(g.Color);
        var stack = new StackPanel { Width = 250 };
        stack.Children.Add(new TextBlock { Text = L.T(g.Title), Foreground = grade, FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(new TextBlock { Text = L.T($"{g.Items.Count} objetos · ~{Num(Any(g))}% de que caiga alguno"), Foreground = Muted, FontSize = 10.5, Margin = new Thickness(0, 2, 0, 6) });
        foreach (var d in g.Items)
        {
            var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1), Height = 22 };
            var icon = new Image { Width = 20, Height = 20, Margin = new Thickness(0, 0, 7, 0), Source = OverlayWindow.Icon(Text(d.Item, "icon")) };
            DockPanel.SetDock(icon, Dock.Left); row.Children.Add(icon);
            var odds = Label(d.Chance is double p ? Num(p) + "%" : "—", 10.5, Teal); odds.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(odds, Dock.Right); row.Children.Add(odds);
            row.Children.Add(Label(ItemName(d), 11, grade));
            stack.Children.Add(row);
        }
        stack.Children.Add(new TextBlock { Text = L.T(pinned ? "Fijado · clic en el grupo para cerrarlo" : "Clic en el grupo para dejarlo fijo"), Foreground = Dim, FontSize = 9.5, Margin = new Thickness(0, 6, 0, 0) });
        return new Border { Background = Paint("#F50B1117"), BorderBrush = grade, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(12, 10, 12, 10), Child = stack,
            LayoutTransform = new ScaleTransform(k, k), Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = .55, Color = Colors.Black } };
    }
    /// <summary>Test hooks (--drops-test): the pieces of the n-th group of the creature shown, and the state of the detail.</summary>
    internal FrameworkElement? DetailOf(int index) => index < groups.Count ? Detail(groups[index], false, 1) : null;
    internal bool DetailOpen => popup.IsOpen;
    internal bool DetailPinned => pinned is not null;
}
