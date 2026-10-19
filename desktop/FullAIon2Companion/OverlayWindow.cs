using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace FullAIon2Companion;
public sealed partial class OverlayWindow : Window
{
    const double BarWidth = 312;
    // The style's colours (OverlayStyles.cs): not frozen, so a new palette recolours everything that uses them at once.
    static readonly LinearGradientBrush HpGreen = new(Color.FromRgb(0x3F, 0xB8, 0xAF), Color.FromRgb(0x8F, 0xE8, 0xC8), 0);
    static readonly SolidColorBrush Teal = Live("#71DED6"), Muted = Live("#95A4B6"), Dim = Live("#5F7480"), Gold = Live("#E9C77E"),
        Bright = Live("#D5DEE6"), RowText = Live("#C3CFD9"), IdleTeal = Live("#4E9A95"), Frame = Live("#C833575B"), MoveFrame = Live("#71DED6");
    static readonly Brush Alert = Paint("#FF8A65"), Track = Paint("#22313C"), Self = Paint("#3FB8AF"), Other = Paint("#3A5568"), Hp = Paint("#C9302C"), HpMid = Paint("#D9822B"), HpLow = Paint("#E8B43A"), HpLag = Paint("#F2E3C6");
    static SolidColorBrush Live(string color) => new((Color)ColorConverter.ConvertFromString(color));
    readonly AppSettings settings;
    readonly Ellipse stateDot = new() { Width = 7, Height = 7, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock state = Label("EN ESPERA", 10, Dim), clock = Label("0:00", 10, Muted);
    readonly TextBlock target = Label("Esperando objetivo", 14, Bright), targetMeta = Label("", 10.5, Muted);
    readonly Image targetIcon = new() { Width = 36, Height = 36, Stretch = Stretch.UniformToFill };
    // The creature's race (Cognis, Feras, Naturas, Varians) as the user's badges, right of the name.
    readonly Image targetBadge = new() { Height = 22, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Visibility = Visibility.Collapsed };
    internal static string? BadgeOf(string? category) => category?.ToLowerInvariant() switch { "cognis" or "intelecto" or "intellect" or "cogni" => "/assets/pet-badges/cognis.png", "feras" or "salvaje" or "feral" or "fera" => "/assets/pet-badges/feras.png", "naturas" or "naturaleza" or "nature" or "natura" => "/assets/pet-badges/naturas.png", "varians" or "transformación" or "transformacion" or "trans" or "varian" => "/assets/pet-badges/varians.png", _ => null };
    readonly Border targetFrame = new() { Width = 38, Height = 38, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 9, 0), Visibility = Visibility.Collapsed };
    static readonly Dictionary<string, ImageSource?> icons = [];
    int? currentNpc;
    (int Caster, int Skill, DateTime At) lastAlert;
    readonly Border root;
    /// <summary>A boss mechanic just started (raised once per cast) - the main window plays the warning sound.</summary>
    /// <summary>A cast that should sound (by the caster kinds chosen in Configuración → Avisos).</summary>
    public event Action<CastRow>? BossCast;
    /// <summary>The user clicked the target in move mode: open its catalog record.</summary>
    public event Action<int>? OpenNpc;
    public int? CurrentNpc => currentNpc;
    /// <summary>The gear on the control strip: show or hide the companion panel.</summary>
    public event Action? SettingsRequested;
    /// <summary>Whether the account's plan grants a feature (companionPvp, companionMinimap); missing means no.</summary>
    public Func<string, bool> Feature { get; set; } = _ => false;
    /// <summary>The minimap is being opened or a player became the target: the license is checked again at once.</summary>
    public event Action? PlusRequested;
    string? lastPlayerTarget;
    /// <summary>The breakdown window for a player (0 = the local player).</summary>
    public event Action<int>? BreakdownRequested;
    readonly OverlayDock dock = new();
    readonly DropsWindow drops;
    readonly CenterAlert centerAlert = new();
    readonly PlayerWindow player = new();
    readonly MinimapWindow minimap;
    bool arranging;
    readonly Grid hpBar = new() { Height = 18, Margin = new Thickness(0, 5, 0, 8) };
    readonly Border hpFill = new() { Background = Hp, CornerRadius = new CornerRadius(9), HorizontalAlignment = HorizontalAlignment.Left };
    // Boss pace, as the game's own analyzer: group DPS and time to kill; with a learned enrage, the countdown, the health
    // the boss would have left then (0 % = it dies first) and a tick on the bar where the health will be at enrage.
    readonly Border enrageTick = new() { Width = 2, Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 1, 0, 1), Visibility = Visibility.Collapsed };
    readonly BossPacePanel bossPace = new(withTitle: false) { Margin = new Thickness(0, -2, 0, 8), Visibility = Visibility.Collapsed };
    readonly BossPaceWindow bossWindow;
    readonly EventToast toast;
    readonly Border hpLag = new() { Background = HpLag, CornerRadius = new CornerRadius(9), HorizontalAlignment = HorizontalAlignment.Left, Opacity = .55 };
    readonly TextBlock hpText = Label("", 11, Brushes.White), hpPct = Label("", 11, Brushes.White);
    int hpTarget;
    readonly Border castBanner = new() { CornerRadius = new CornerRadius(5), Padding = new Thickness(9, 4, 9, 4), Margin = new Thickness(0, 0, 0, 6), Background = new SolidColorBrush(Color.FromArgb(225, 178, 52, 28)) };
    readonly TextBlock castName = Label("", 15, Brushes.White), castFrom = Label("", 10.5, Paint("#FFE2D6"));
    readonly TextBlock castLog = Label("", 10.5, Muted);
    bool castBlink;
    double hpLagRatio;
    readonly TextBlock dps = Label("—", 28, Teal), dpsUnit = Label(" DPS", 12, Muted);
    readonly TextBlock dmgLine = Label("", 10.5, Muted), healLine = Label("", 10.5, Muted), killLine = Label("", 10.5, Muted);
    // Footer, as other meters have it: own class and name on the left; Od energy and the ping (bars + figure) on the right.
    readonly Image selfIcon = new() { Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0) }, odIcon = new() { Width = 17, Height = 17, Margin = new Thickness(0, 0, 4, 0) };
    readonly TextBlock selfName = Label("", 11, Teal), odText = Label("", 11, Paint("#D5DEE6")), pingFooter = Label("", 10.5, Muted);
    readonly System.Windows.Shapes.Rectangle[] bars = Enumerable.Range(0, 4).Select(i => new System.Windows.Shapes.Rectangle { Width = 3, Height = 4 + i * 2.5, RadiusX = .8, RadiusY = .8, Margin = new Thickness(0, 0, 1.5, 0), VerticalAlignment = VerticalAlignment.Bottom }).ToArray();
    readonly StackPanel odGroup = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0) };
    readonly TextBlock detail = Label("Esperando identidad · captura pasiva", 11, Muted);
    readonly StackPanel party = new() { Margin = new Thickness(0, 4, 0, 0) };
    readonly TextBlock timer = Label("", 11, Gold), hint = Label("", 10, Dim);
    long lastDamage = -1, lastPackets = -1;
    readonly TextBlock petLine = Label("", 11, Paint("#9FE0C8"));
    // The pet card: shown while the target is a creature that gives pet souls.
    readonly Border petCard = new() { Background = Paint("#3317343A"), BorderBrush = Paint("#552E6F69"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(9, 6, 9, 7), Margin = new Thickness(0, 0, 0, 7), Visibility = Visibility.Collapsed };
    readonly Image petRace = new() { Height = 18, Stretch = Stretch.Uniform, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    readonly TextBlock petTitle = Label("", 12, Brushes.White), petBadge = Label("", 10.5, Paint("#0D141B")), petCategory = Label("", 10, Paint("#8FB9B4")),
        petCount = Label("", 11, Paint("#D5DEE6")), petDetail = Label("", 10.5, Paint("#95A4B6"));
    readonly Border petBadgeBox = new() { CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 0, 5, 1), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly ColumnDefinition petFill = new() { Width = new GridLength(0, GridUnitType.Star) }, petEmpty = new() { Width = new GridLength(1, GridUnitType.Star) };
    readonly Border petBar = new() { CornerRadius = new CornerRadius(3), Background = Paint("#5FD3B4") };
    // Pinned pets: one card each under the overlay (portrait, level, progress bar, souls left), Mascotas tab → Fijar.
    readonly StackPanel pinCards = new() { Margin = new Thickness(0, 4, 0, 0) };
    readonly StackPanel contentStack = new();
    // Countdown chips under the header (TeamMate's Field bosses / Rifts / Shugo buttons): the next field boss, rift and Shugo.
    readonly WrapPanel headerTimers = new() { Visibility = Visibility.Collapsed };
    public void SetHeaderTimers(IReadOnlyList<(string Icon, string Name, string Color, DateTime At)> items)
    {
        var now = DateTime.Now;
        lastTimers = items;
        var shown = settings.OverlayShowHeaderTimers ? items.Where(i => i.At > now.AddMinutes(-2)).ToList() : [];
        while (headerTimers.Children.Count > shown.Count + 1) headerTimers.Children.RemoveAt(headerTimers.Children.Count - 1);
        for (int i = 0; i < shown.Count; i++)
        {
            if (i + 1 >= headerTimers.Children.Count)
                headerTimers.Children.Add(new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(7, 1, 8, 2), Margin = new Thickness(0, 0, 5, 3), BorderThickness = new Thickness(1), Child = new TextBlock { FontSize = 10.5 } });
            var (icon, name, color, at) = shown[i];
            var chip = (Border)headerTimers.Children[i + 1]; var text = (TextBlock)chip.Child;
            var left = at - now;
            string clock = left <= TimeSpan.Zero ? "ahora" : left.TotalHours >= 1 ? $"{(int)left.TotalHours}:{left.Minutes:00}:{left.Seconds:00}" : $"{left.Minutes}:{left.Seconds:00}";
            text.Text = $"{icon} {name} {clock}";
            var brush = Paint(color);
            text.Foreground = left <= TimeSpan.FromMinutes(5) ? Brushes.White : brush;
            chip.BorderBrush = brush; chip.Background = left <= TimeSpan.FromMinutes(5) ? Paint("#55" + color.TrimStart('#')) : Paint("#33131E27");
            chip.ToolTip = $"{name} · {at:HH:mm}";
        }
        ShowHeaderRow();
    }
    // The counters row shows while it has a chip: the ranking or a countdown.
    void ShowHeaderRow() => headerTimers.Visibility = headerTimers.Children.Count > 1 || rankPill.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
    // "Cubo oculto cerca": a bar over the overlay while a hidden cube the server announced is still there.
    readonly Border cubeBar = new() { Margin = new Thickness(0, 0, 0, 4), Padding = new Thickness(10, 6, 10, 6), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1.5), Visibility = Visibility.Collapsed };
    readonly TextBlock cubeArrow = new() { Text = "➤", FontSize = 22, Foreground = Paint("#C9A6FF"), VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Point(.5, .5), Margin = new Thickness(0, 0, 10, 0) };
    readonly TextBlock cubeTitle = Label("CUBO OCULTO CERCA", 11.5, Paint("#E4D4FF")), cubeDetail = Label("", 11, Muted), cubeDistance = Label("", 17, Brushes.White);
    void BuildCubeBar()
    {
        cubeBar.Background = Paint("#E61C1430"); cubeBar.BorderBrush = Paint("#B98CFF");
        cubeTitle.FontWeight = FontWeights.Bold; cubeDistance.FontWeight = FontWeights.Bold; cubeDistance.Margin = new Thickness(10, 0, 0, 0);
        var text = cubeTextStack = new StackPanel(); text.Children.Add(cubeTitle); text.Children.Add(cubeDetail);
        var row = new DockPanel(); DockPanel.SetDock(cubeArrow, Dock.Left); row.Children.Add(cubeArrow);
        DockPanel.SetDock(cubeDistance, Dock.Right); row.Children.Add(cubeDistance); row.Children.Add(text);
        cubeBar.Child = row;
    }
    /// <summary>Shows the live hidden cube: arrow on the north-up map, distance, height and how long ago it appeared.</summary>
    public void UpdateCube(CubeView v)
    {
        // The style says where it goes: the bar over the overlay, a chip (Módulos, Aion) or a row inside (Según el momento).
        lastCube = v;
        bool active = settings.CubeAlerts && v.Active is not null && (IsVisible || Preview);
        cubeBar.Visibility = active && (layout is "clasico" or "pulido" || layout == "momento" && !momentoCalm) ? Visibility.Visible : Visibility.Collapsed;
        cubePill.Visibility = active && layout is "modulos" or "medallon" ? Visibility.Visible : Visibility.Collapsed;
        cubeInline.Visibility = active && layout == "momento" && momentoCalm ? Visibility.Visible : Visibility.Collapsed;
        if (!active || v.Active is not { } c) return;
        cubeDistance.Text = v.Distance is double d ? $"{d:0} m" : "";
        cubeArrow.Visibility = v.Bearing is null ? Visibility.Hidden : Visibility.Visible;
        cubeArrow.RenderTransform = new RotateTransform((v.Bearing ?? 0) - 90);
        var ago = DateTime.Now - c.At;
        string height = v.Height is double h ? Math.Abs(h) < 2 ? "a tu altura" : h > 0 ? $"{h:0} m arriba" : $"{-h:0} m abajo" : "";
        string shortly = c.LeftRangeAt is not null ? "· fuera de alcance" : height.Length > 0 ? "· " + height : $"· hace {(int)ago.TotalMinutes}:{ago.Seconds:00}";
        cubeDetail.Text = layout != "clasico" ? shortly
            : string.Join(" · ", new[] { c.LeftRangeAt is null ? "" : "fuera de alcance, puede que ya no esté", height, $"hace {(int)ago.TotalMinutes}:{ago.Seconds:00}" }.Where(x => x.Length > 0));
        cubePillText.Inlines.Clear();
        cubePillText.Inlines.Add(new System.Windows.Documents.Run("Cubo ") { Foreground = Paint("#E4D4FF") });
        cubePillText.Inlines.Add(new System.Windows.Documents.Run(cubeDistance.Text) { Foreground = Brushes.White, FontWeight = FontWeights.SemiBold });
        cubeInlineText.Inlines.Clear();
        cubeInlineText.Inlines.Add(new System.Windows.Documents.Run("Cubo oculto") { Foreground = Paint("#E4D4FF") });
        cubeInlineText.Inlines.Add(new System.Windows.Documents.Run(" " + shortly) { Foreground = Muted });
        cubeInlineDistance.Text = cubeDistance.Text;
    }
    // The resize grip (move mode only): dragging it scales the overlay, like TeamMate's corner handle.
    readonly System.Windows.Controls.Primitives.Thumb grip = new() { Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 2, 2), Cursor = Cursors.SizeNWSE, Visibility = Visibility.Collapsed,
        ToolTip = "Arrastra para cambiar el tamaño del overlay" };
    void BuildGrip()
    {
        var mark = new FrameworkElementFactory(typeof(TextBlock));
        mark.SetValue(TextBlock.TextProperty, "◢"); mark.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(MoveFrame.Color)); mark.SetValue(TextBlock.FontSizeProperty, 13.0); // a template freezes what it holds: the grip gets a copy, not the style's brush
        mark.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Right); mark.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Bottom);
        grip.Template = new ControlTemplate(typeof(System.Windows.Controls.Primitives.Thumb)) { VisualTree = mark };
        grip.DragDelta += (_, e) => { settings.OverlayScale = Math.Clamp(settings.OverlayScale + e.HorizontalChange / 344, .7, 1.8); ApplyAppearance(); };
        grip.DragCompleted += (_, _) => settings.Save();
    }
    readonly TextBlock farmTitle = Label("", 9.5, Teal), farmSummary = Label("", 10, Muted);
    // The farm session (outside boss fights): time, kills, souls and their hourly rates.
    readonly Grid farmBlock = new() { Margin = new Thickness(0, 6, 0, 2), Visibility = Visibility.Collapsed };
    readonly TextBlock[] farmValues = Enumerable.Range(0, 8).Select(_ => Label("", 11, Brushes.White)).ToArray();
    /// <summary>Kinah earned this session (InventoryWatch), set by the main window.</summary>
    public Func<long>? SessionKinah { get; set; }
    /// <summary>The session start and kills (CaptureEngine.Session), set by the main window.</summary>
    public Func<(DateTime Start, int Kills)>? Session { get; set; }
    /// <summary>The session's loot at Market prices and how many of its items have none (Mercado tab), set by the main window.</summary>
    public Func<(long Value, int Unpriced)>? SessionMarket { get; set; }
    /// <summary>The Market price of one unit of an item on the player's server (theirs or the source's), set by the main window.</summary>
    public Func<int, long?>? MarketPrice { get; set; }
    /// <summary>The last items gained, oldest first (InventoryWatch.Recent), set by the main window.</summary>
    public Func<int, IReadOnlyList<LootEntry>>? RecentLoot { get; set; }
    /// <summary>The Abyss ranking of the character (class, all classes), set by the main window.</summary>
    public Func<(RankingWatch.Entry? Class, RankingWatch.Entry? All)>? AbyssRank { get; set; }
    // The Abyss ranking as one more chip of the counters row: the game's medal, the place in the class and in all classes
    // («#31 ▲1 · #309 ▼5») as the game last said them, and a card on hover. On 2026-10-18 it was a line under the loot, then a
    // medal by the clock; the user chose this from drawings of four places.
    readonly Border rankPill = new() { CornerRadius = new CornerRadius(9), Padding = new Thickness(5, 1, 8, 2), Margin = new Thickness(0, 0, 5, 3), BorderThickness = new Thickness(1), Visibility = Visibility.Collapsed };
    readonly TextBlock rankText = new() { FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center };
    string rankKey = "";
    // The loot HUD: the items just gained (the last 90 s, up to three) with what they are worth on the Market.
    readonly StackPanel lootFeed = new() { Margin = new Thickness(0, 2, 0, 2), Visibility = Visibility.Collapsed };
    string lootKey = "";
    void BuildFarmBlock()
    {
        farmBlock.ColumnDefinitions.Add(new ColumnDefinition()); farmBlock.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        farmTitle.FontWeight = FontWeights.SemiBold;
        var title = new DockPanel { Margin = new Thickness(0, 0, 0, 3), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = "Plegar o desplegar (en modo mover)" };
        DockPanel.SetDock(farmSummary, Dock.Right); title.Children.Add(farmSummary); title.Children.Add(farmTitle);
        title.PreviewMouseLeftButtonDown += (_, e) => { if (ClickThrough) return; e.Handled = true; settings.FarmCollapsed = !settings.FarmCollapsed; settings.Save(); };
        Grid.SetColumnSpan(title, 2);
        farmBlock.RowDefinitions.Add(new RowDefinition()); farmBlock.Children.Add(title);
        string[] names = ["Tiempo de sesión", "Muertes", "Almas", "Muertes / h", "Almas / h", "Kinah", "Kinah / h", "Botín · Mercado"];
        for (int i = 0; i < names.Length; i++)
        {
            farmBlock.RowDefinitions.Add(new RowDefinition());
            var row = new Border { Background = Paint(i % 2 == 0 ? "#22131E27" : "#00000000"), CornerRadius = new CornerRadius(3) }; Grid.SetRow(row, i + 1); Grid.SetColumnSpan(row, 2);
            var name = Label(names[i], 11, Muted); name.Margin = new Thickness(6, 2, 0, 2); Grid.SetRow(name, i + 1);
            farmValues[i].Margin = new Thickness(0, 2, 6, 2); farmValues[i].FontWeight = FontWeights.SemiBold; Grid.SetRow(farmValues[i], i + 1); Grid.SetColumn(farmValues[i], 1);
            farmBlock.Children.Add(row); farmBlock.Children.Add(name); farmBlock.Children.Add(farmValues[i]);
        }
    }
    /// <summary>The farm objective (set by the main window): kind, progress and the end at this pace.</summary>
    public Func<ObjectiveView?>? Objective { get; set; }
    readonly Border objectiveCard = new() { Margin = new Thickness(0, 6, 0, 2), Padding = new Thickness(8, 6, 8, 6), CornerRadius = new CornerRadius(6), Visibility = Visibility.Collapsed };
    readonly TextBlock objectiveTitle = Label("", 11, Brushes.White), objectivePct = Label("", 11, Teal), objectiveLine = Label("", 10.5, Muted);
    readonly ColumnDefinition objectiveFill = new(), objectiveEmpty = new();
    readonly Border objectiveBar = new() { CornerRadius = new CornerRadius(3) };
    void BuildObjective()
    {
        objectiveCard.Background = Paint("#3313302A"); objectiveCard.BorderBrush = Paint("#2E5A50"); objectiveCard.BorderThickness = new Thickness(1);
        objectiveTitle.FontWeight = FontWeights.SemiBold; objectivePct.FontWeight = FontWeights.Bold;
        var top = new DockPanel(); DockPanel.SetDock(objectivePct, Dock.Right); top.Children.Add(objectivePct); top.Children.Add(objectiveTitle);
        var bar = new Grid { Height = 6, Margin = new Thickness(0, 5, 0, 4) };
        bar.ColumnDefinitions.Add(objectiveFill); bar.ColumnDefinitions.Add(objectiveEmpty);
        var track = new Border { Background = Paint("#22313C"), CornerRadius = new CornerRadius(3) }; Grid.SetColumnSpan(track, 2);
        bar.Children.Add(track); bar.Children.Add(objectiveBar);
        var stack = new StackPanel(); stack.Children.Add(top); stack.Children.Add(bar); stack.Children.Add(objectiveLine);
        objectiveCard.Child = stack;
    }
    static string Span(double seconds) { var t = TimeSpan.FromSeconds(Math.Max(0, seconds)); return t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes:00} m" : $"{t.Minutes} m {t.Seconds:00} s"; }
    void UpdateObjective(CaptureSnapshot s)
    {
        bool bossFight = s.Target is { NpcId: int npc } && Catalog.IsElite(npc) && s.Target.GroupDps is not null;
        if (Objective?.Invoke() is not { } o || bossFight) { objectiveCard.Visibility = Visibility.Collapsed; return; }
        objectiveCard.Visibility = Visibility.Visible;
        string unit = o.Kind switch { "kinah" => "kinah", "loot" => "kinah en botín", _ => "muertes" };
        objectiveTitle.Text = $"OBJETIVO · {o.Progress:N0} / {o.Target:N0} {unit}";
        objectivePct.Text = $"{o.Percent:0} %";
        double r = Math.Clamp(o.Percent / 100, 0, 1);
        objectiveFill.Width = new GridLength(r, GridUnitType.Star); objectiveEmpty.Width = new GridLength(1 - r, GridUnitType.Star);
        objectiveBar.Background = o.Done ? Gold : o.OnPace == false ? Paint("#FF8A65") : HpGreen;
        objectiveLine.Text = o.Done ? $"¡Objetivo cumplido en {Span(o.Elapsed)}!"
            : o.ChallengeLeft is double left ? (left <= 0 ? "Reto terminado: se acabó el tiempo" : $"Reto · quedan {Span(left)} · " + (o.EtaSeconds is double e ? (o.OnPace == true ? $"vas a tiempo ({Span(e)})" : $"a este ritmo faltan {Span(e)}") : "sin ritmo aún"))
            : o.EtaSeconds is double eta ? $"A este ritmo, {Span(eta)} restantes" : "Esperando el primer progreso";
    }
    void UpdateFarmBlock(CaptureSnapshot s)
    {
        bool bossFight = s.Target is { NpcId: int npc } && Catalog.IsElite(npc) && s.Target.GroupDps is not null;
        if (!settings.OverlayShowFarm || Session is null || bossFight) { farmBlock.Visibility = Visibility.Collapsed; return; }
        var (start, kills) = Session();
        int souls = s.Pet?.SessionSouls ?? 0;
        var span = DateTime.Now - start; double hours = Math.Max(span.TotalHours, 1 / 60.0);
        farmBlock.Visibility = Visibility.Visible;
        farmValues[0].Text = span.TotalHours >= 1 ? $"{(int)span.TotalHours}h {span.Minutes:00}m" : $"{span.Minutes}m {span.Seconds:00}s";
        farmValues[1].Text = kills.ToString("N0"); farmValues[2].Text = souls.ToString("N0");
        farmValues[3].Text = (kills / hours).ToString("N0"); farmValues[4].Text = (souls / hours).ToString("N0");
        long kinah = SessionKinah?.Invoke() ?? 0;
        farmValues[5].Text = kinah.ToString("N0"); farmValues[6].Text = (kinah / hours).ToString("N0");
        var (market, unpriced) = SessionMarket?.Invoke() ?? (0, 0);
        farmValues[7].Text = market > 0 ? market.ToString("N0") : "—";
        farmValues[7].ToolTip = unpriced > 0 ? $"{unpriced} objetos del botín sin precio: ponlo en la pestaña Mercado del panel" : null;
        bool folded = settings.FarmCollapsed;
        farmTitle.Text = folded ? "SESIÓN DE FARMEO ▸" : "SESIÓN DE FARMEO ▾";
        farmSummary.Text = folded ? $"{farmValues[0].Text} · {kills:N0} muertes · {souls:N0} almas · {kinah:N0} kinah" : "";
        foreach (UIElement child in farmBlock.Children) if (Grid.GetRow(child) > 0) child.Visibility = folded ? Visibility.Collapsed : Visibility.Visible;
        for (int i = 1; i < farmBlock.RowDefinitions.Count; i++) farmBlock.RowDefinitions[i].Height = folded ? new GridLength(0) : GridLength.Auto;
    }
    void UpdateLootFeed(CaptureSnapshot s)
    {
        bool bossFight = s.Target is { NpcId: int npc } && Catalog.IsElite(npc) && s.Target.GroupDps is not null;
        var recent = settings.OverlayShowLootPrices && !bossFight && RecentLoot?.Invoke(3) is { } last
            ? last.Where(e => (DateTime.Now - e.At).Duration() < TimeSpan.FromSeconds(90)).Reverse().ToList() : [];
        lootFeed.Visibility = recent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        string key = string.Join("|", recent.Select(e => $"{e.At.Ticks}:{e.Item}:{e.Quantity}:{MarketPrice?.Invoke(e.Item)}"));
        if (key == lootKey) return;
        lootKey = key; lootFeed.Children.Clear();
        foreach (var e in recent)
        {
            long? unit = MarketPrice?.Invoke(e.Item);
            var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1), LastChildFill = true };
            // The Market value, else the NPC's buying price (dim), else whether it can be sold at all.
            bool bound = InventoryWatch.Bound(e.Item);
            var price = Label(unit is long u ? (u * e.Quantity).ToString("N0") : e.Value > 0 ? $"PNJ {e.Value:N0}" : bound ? "vinculado" : "sin precio", 10.5, unit is null ? Dim : Gold);
            price.FontWeight = FontWeights.SemiBold; price.Margin = new Thickness(6, 0, 0, 0);
            price.ToolTip = unit is long each ? $"{each:N0} kinah cada uno en el Mercado" : bound ? "Vinculado: no se vende en el Mercado" : "Sin precio de Mercado: ponlo en la pestaña Mercado del panel";
            DockPanel.SetDock(price, Dock.Right); row.Children.Add(price);
            if (Icon(e.Icon) is { } image)
            {
                var picture = new Image { Source = image, Width = 16, Height = 16, Margin = new Thickness(0, 0, 5, 0) };
                DockPanel.SetDock(picture, Dock.Left); row.Children.Add(picture);
            }
            row.Children.Add(Label(e.Quantity > 1 ? $"{e.Name} ×{e.Quantity:N0}" : e.Name, 10.5, Paint(DropsWindow.Grades.TryGetValue(e.Grade, out var g) ? g.Color : "#DCE1E9")));
            lootFeed.Children.Add(row);
        }
    }
    /// <summary>For --rank-badge-test: the header with the badge filled in, and the badge's tooltip.</summary>
    internal (FrameworkElement Header, string? Tip) RankBadgeForTest()
    {
        state.Text = "EN COMBATE"; clock.Text = "0:42"; rankKey = "";
        UpdateRankLine();
        return ((FrameworkElement)rankPill.Parent, ((rankPill.ToolTip as ToolTip)?.Content as Panel) is { } card ? string.Join("\n", Texts(card)) : null);
    }
    // The texts of a card, in order (for the test).
    static IEnumerable<string> Texts(DependencyObject node)
    {
        if (node is TextBlock t) { yield return string.Concat(t.Inlines.OfType<System.Windows.Documents.Run>().Select(r => r.Text)) is { Length: > 0 } runs ? runs : t.Text; yield break; }
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) foreach (var text in Texts(child)) yield return text;
    }
    void UpdateRankLine()
    {
        var (mine, all) = settings.OverlayShowRanking && AbyssRank?.Invoke() is { } r ? r : (null, null);
        var shown = mine ?? all;
        rankPill.Visibility = shown is null ? Visibility.Collapsed : Visibility.Visible;
        ShowHeaderRow();
        // The list seen last (opening one tab of the game's window sends only that list): its points and its renewal. The game
        // renews it every hour, and after that the card asks to open the ranking again.
        var latest = all is not null && (mine is null || all.SeenAt > mine.SeenAt) ? all : shown;
        bool current = latest is not null && DateTime.Now < latest.Snapshot;
        string key = $"{L.Language}:{mine?.Rank}:{mine?.Change}:{all?.Rank}:{all?.Change}:{current}";
        if (shown is null || latest is null || key == rankKey) return;
        rankKey = key; rankText.Inlines.Clear();
        void Add(string text, Brush color, bool strong = false) => rankText.Inlines.Add(new System.Windows.Documents.Run(text) { Foreground = color, FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal });
        Add($"#{shown.Rank:N0}", Brushes.White, true);
        if (shown.Change != 0) Add(Arrow(shown.Change), ArrowColor(shown.Change));
        if (mine is not null && all is not null) { Add($"  · #{all.Rank:N0}", Muted); if (all.Change != 0) Add(Arrow(all.Change), ArrowColor(all.Change)); }
        rankPill.ToolTip = RankCard(mine, all, latest, current);
    }
    static string Arrow(int change) => change > 0 ? $" ▲{change}" : change < 0 ? $" ▼{-change}" : "";
    static Brush ArrowColor(int change) => change > 0 ? HpGreen : Paint("#FF8A65");
    // The ranking chip's card: a row per list, the points and the kills, and when it was seen and when it renews.
    static ToolTip RankCard(RankingWatch.Entry? mine, RankingWatch.Entry? all, RankingWatch.Entry latest, bool current)
    {
        var rows = new Grid();
        rows.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); rows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        void Row(string label, params System.Windows.Documents.Run[] value)
        {
            int at = rows.RowDefinitions.Count; rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var name = Label(L.T(label), 10.5, Muted); name.Margin = new Thickness(0, 1, 16, 1); Grid.SetRow(name, at); rows.Children.Add(name);
            var figures = new TextBlock { FontSize = 10.5, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            foreach (var run in value) figures.Inlines.Add(run);
            Grid.SetRow(figures, at); Grid.SetColumn(figures, 1); rows.Children.Add(figures);
        }
        System.Windows.Documents.Run Strong(string text) => new(text) { FontWeight = FontWeights.SemiBold };
        System.Windows.Documents.Run Change(int change) => new(Arrow(change)) { Foreground = ArrowColor(change) };
        System.Windows.Documents.Run Rest(RankingWatch.Entry e) => new("  " + L.T($"de {e.Total:N0} · top {Math.Max(0.1, e.Rank * 100.0 / Math.Max(1, e.Total)):0.0} %")) { Foreground = Muted };
        if (mine is not null) Row("Tu clase", Strong($"#{mine.Rank:N0}"), Change(mine.Change), Rest(mine));
        if (all is not null) Row("General", Strong($"#{all.Rank:N0}"), Change(all.Change), Rest(all));
        Row("Puntos", Strong($"{latest.Points:N0}"));
        int? Count(string field) => latest.Detail.ValueKind == System.Text.Json.JsonValueKind.Object && latest.Detail.TryGetProperty(field, out var v) && v.TryGetInt32(out int n) ? n : null;
        if (Count("kill_count") is int kills && Count("death_count") is int deaths && Count("assist_count") is int assists)
            Row("Bajas · muertes · asist.", new System.Windows.Documents.Run($"{kills:N0} · {deaths:N0} · {assists:N0}"));
        var title = Label(L.T("Clasificación del Abismo"), 11, Gold); title.FontWeight = FontWeights.SemiBold; title.Margin = new Thickness(0, 0, 0, 4);
        string when = !current ? $"Hay datos nuevos desde las {latest.Snapshot:HH:mm}: abre la clasificación en el juego"
            : mine is not null && all is not null && mine.Snapshot != all.Snapshot ? $"Clase vista {mine.SeenAt:HH:mm} · general {all.SeenAt:HH:mm} · se renueva {latest.Snapshot:HH:mm}"
            : $"Vista {latest.SeenAt:HH:mm} · se renueva {latest.Snapshot:HH:mm}";
        var foot = Label(L.T(when), 10, current ? Muted : Paint("#FF8A65")); foot.TextWrapping = TextWrapping.Wrap; foot.MaxWidth = 300; foot.Margin = new Thickness(0, 5, 0, 0);
        var card = new StackPanel(); card.Children.Add(title); card.Children.Add(rows);
        card.Children.Add(new Border { Height = 1, Background = Paint("#1F2C31"), Margin = new Thickness(0, 5, 0, 0) }); card.Children.Add(foot);
        return new ToolTip { Content = card, Background = Paint("#F20D141B"), BorderBrush = Paint("#33575B"), BorderThickness = new Thickness(1), Padding = new Thickness(10, 7, 10, 7), Foreground = Brushes.White };
    }

    string pinKey = "";
    static readonly Dictionary<int, string> petIcons = LoadPetIcons();
    static Dictionary<int, string> LoadPetIcons()
    {
        try { return System.Text.Json.JsonSerializer.Deserialize<Dictionary<int, string>>(System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "data", "pet-icons.json"))) ?? []; }
        catch { return []; }
    }
    void UpdatePinCards(IReadOnlyList<PetStatus>? pins)
    {
        pins ??= [];
        string key = string.Join("|", pins.Select(p => $"{p.PetId}:{p.Level}:{p.Progress}:{p.Source}"));
        // Inside the overlay in Módulos, Mínimo, Aion and Según el momento out of a fight; cards with a round portrait and a race chip but in Clásico.
        bool cards = layout is "clasico" or "pulido" || layout == "momento" && !momentoCalm, modern = layout != "clasico";
        pinCards.Visibility = pins.Count > 0 && settings.OverlayShowPet && cards ? Visibility.Visible : Visibility.Collapsed;
        key = layout + "|" + key;
        if (key == pinKey) return;
        pinKey = key; pinCards.Children.Clear();
        byte alpha = (byte)Math.Clamp(settings.OverlayOpacity * 255, 90, 250);
        foreach (var p in pins)
        {
            bool max = p.Threshold == 0, locked = p.Level == 0;
            double ratio = max ? 1 : Math.Clamp(p.Progress / (double)p.Threshold, 0, 1);
            var raceOf = BossPacePanel.RaceOf(p.Source);
            var portrait = modern
                ? new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(19), BorderThickness = new Thickness(1.5), Margin = new Thickness(0, 0, 10, 0), BorderBrush = raceOf is { } rc ? Paint(rc.Color) : Frame,
                    Background = Icon(petIcons.GetValueOrDefault(p.PetId)) is { } face ? new ImageBrush(face) { Stretch = Stretch.UniformToFill } : Paint("#22313C") }
                : new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(6), Background = Paint("#22313C"), Margin = new Thickness(0, 0, 10, 0),
                    Child = new Image { Source = Icon(petIcons.GetValueOrDefault(p.PetId)), Stretch = Stretch.UniformToFill } };
            var name = Label(p.Name, 13, Brushes.White); name.FontWeight = FontWeights.SemiBold;
            var nameRow = new DockPanel();
            if (!modern && Icon(BadgeOf(p.Source)) is { } race) { var badge = new Image { Source = race, Height = 17, Stretch = Stretch.Uniform, Margin = new Thickness(8, 0, 0, 0), ToolTip = p.Source }; DockPanel.SetDock(badge, Dock.Right); nameRow.Children.Add(badge); }
            nameRow.Children.Add(name);
            var sub = Label(max ? "Nv. 3 · máximo" : locked ? $"Sin obtener · {p.Progress}/{p.Threshold} almas" : $"Nv. {p.Level} · {p.Progress}/{p.Threshold}", 10.5, Muted);
            var bar = new Grid { Height = 5, Margin = new Thickness(0, 5, 0, 0) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ratio, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - ratio, GridUnitType.Star) });
            var track = new Border { Background = Paint("#22313C"), CornerRadius = new CornerRadius(3) }; Grid.SetColumnSpan(track, 2);
            bar.Children.Add(track); bar.Children.Add(new Border { Background = max ? Gold : Teal, CornerRadius = new CornerRadius(3) });
            UIElement subRow = sub;
            if (modern && raceOf is { } raceInfo)
            {
                var chip = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), BorderBrush = Paint("#2E4A4F"), Padding = new Thickness(5, 0, 6, 1), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                    Child = Row(new Image { Source = Crystal(raceInfo.Name), Height = 13, Margin = new Thickness(0, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center }, Label(raceInfo.Name, 10, Paint(raceInfo.Color))) };
                subRow = Row(sub, chip);
            }
            var text = new StackPanel(); text.Children.Add(nameRow); text.Children.Add(subRow); text.Children.Add(bar);
            var left = Label(max ? "MÁX." : p.Remaining == 1 ? "falta 1" : $"faltan {p.Remaining}", 11, max ? Gold : Teal);
            left.FontWeight = FontWeights.SemiBold; left.Margin = new Thickness(10, 0, 0, 0);
            var row = new DockPanel(); DockPanel.SetDock(portrait, Dock.Left); row.Children.Add(portrait);
            DockPanel.SetDock(left, Dock.Right); row.Children.Add(left); row.Children.Add(text);
            pinCards.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(alpha, 19, 30, 38)), BorderBrush = Frame, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7), Padding = new Thickness(8, 7, 10, 7), Margin = new Thickness(0, 0, 0, 4), Child = row });
        }
    }
    DateTime lastPacket = DateTime.MinValue;
    readonly TextBlock noticeText = Label("", 10.5, Gold);
    readonly Border notice = new() { BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
    DateTime gameChecked = DateTime.MinValue;
    bool gameRunning;
    DateTime lastChange = DateTime.MinValue;
    public bool Requested { get => settings.OverlayVisible; set { settings.OverlayVisible = value; settings.Save(); } }
    public bool ClickThrough { get; private set; } = true;
    /// <summary>The companion panel is open over the game: the overlay must not climb back above it.</summary>
    public bool PanelOpen { get; set; }
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    IntPtr gameWindow, attachedTo = (IntPtr)(-1);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    public OverlayWindow(AppSettings settings)
    {
        this.settings = settings;
        Title="FullAIon2Companion · Overlay"; Width=344; SizeToContent=SizeToContent.Height;
        // Default sits below the game's top-left party/raid frames (four groups reach ~645px) instead of covering them.
        Left=settings.OverlayLeft ?? 16; Top=settings.OverlayTop ?? 690;
        WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize; AllowsTransparency=true;
        Background=Brushes.Transparent; Topmost=true; ShowActivated=false; ShowInTaskbar=false;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        DockPanel.SetDock(clock, Dock.Right); header.Children.Add(clock);
        var rankRow = new StackPanel { Orientation = Orientation.Horizontal };
        rankRow.Children.Add(new Image { Source = Icon("/assets/game/Icon_Exploit_Medal_lv4_01.webp"), Width = 14, Height = 14, Margin = new Thickness(0, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center });
        rankRow.Children.Add(rankText);
        rankPill.Child = rankRow; rankPill.BorderBrush = Paint("#8C7440"); rankPill.Background = Paint("#33131E27");
        headerTimers.Children.Add(rankPill);
        header.Children.Add(Row(stateDot, state));
        headerTimers.Margin = new Thickness(0, 0, 0, 4);
        hpBar.Children.Add(new Border { Background = Paint("#1A2A33"), CornerRadius = new CornerRadius(9), BorderBrush = Paint("#2E4A4F"), BorderThickness = new Thickness(1) });
        hpBar.Children.Add(hpLag); hpBar.Children.Add(hpFill); hpBar.Children.Add(enrageTick);
        bossWindow = new BossPaceWindow(settings);
        assistant = new AssistantWindow(settings);
        // The hover card of the assistant: the level of each skill from the player's official profile, and the overlay's style.
        assistant.LevelOf = skill => ownCard?.SkillLevels is { } levels && levels.TryGetValue(skill, out int level) ? level : null;
        assistant.Theme = w => ThemeWindow(w);
        assistant.Shown += () => { assistant.Topmost = !settings.OverlayAttachToGame; Restack(GetForegroundWindow()); };
        toast = new EventToast(settings);
        toast.Shown += () => { toast.Topmost = !settings.OverlayAttachToGame; Restack(GetForegroundWindow()); };
        bossWindow.Shown += () => { bossWindow.Topmost = !settings.OverlayAttachToGame; Restack(GetForegroundWindow()); };
        var hpLabels = new DockPanel { Margin = new Thickness(7, 0, 7, 0) };
        foreach (var label in new[] { hpText, hpPct })
        {
            label.FontWeight = FontWeights.SemiBold;
            label.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 1, Opacity = .9, Color = Colors.Black };
        }
        DockPanel.SetDock(hpPct, Dock.Right); hpLabels.Children.Add(hpPct); hpLabels.Children.Add(hpText);
        hpBar.Children.Add(hpLabels);
        castName.FontWeight = FontWeights.Bold;
        var castRow = new DockPanel(); DockPanel.SetDock(castFrom, Dock.Right); castRow.Children.Add(castFrom); castRow.Children.Add(castName);
        castBanner.Child = castRow; castBanner.Visibility = Visibility.Collapsed;
        castLog.Margin = new Thickness(0, -3, 0, 6); castLog.Visibility = Visibility.Collapsed;
        hint.TextWrapping = TextWrapping.Wrap; hint.Foreground = MoveFrame; hint.Margin = new Thickness(0, 6, 0, 0);
        // DPS block: the figure large on the left, damage / healing / kills stacked on the right.
        foreach (var t in new[] { dmgLine, healLine, killLine }) { t.HorizontalAlignment = HorizontalAlignment.Right; dpsNumbers.Children.Add(t); }
        DockPanel.SetDock(dpsNumbers, Dock.Right); dpsBlock.Children.Add(dpsNumbers);
        dpsRow = Row(dps, dpsUnit); dpsUnit.VerticalAlignment = VerticalAlignment.Bottom; dpsUnit.Margin = new Thickness(0, 0, 0, 6); dps.FontWeight = FontWeights.SemiBold;
        dpsBlock.Children.Add(dpsRow);
        party.Margin = new Thickness(0, 6, 0, 0);
        BuildPetCard();BuildFarmBlock();BuildObjective();
        targetRow = TargetRow(); // the style places the parts (OverlayStyles.ArrangeLayout)
        detail.Visibility = Visibility.Collapsed;
        odIcon.Source = Icon("/assets/game/Icon_Item_Odenergy_A_001.webp");
        odGroup.Children.Add(odIcon); odGroup.Children.Add(odText);
        var signal = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 1) };
        foreach (var b in bars) signal.Children.Add(b);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(odGroup); right.Children.Add(signal); right.Children.Add(pingFooter);
        DockPanel.SetDock(right, Dock.Right); footer.Children.Add(right);
        selfRow = Row(selfIcon, selfName); footer.Children.Add(selfRow);
        selfName.FontWeight = FontWeights.SemiBold;
        bodyBox.Child = bodyPanel; rootLayout.Children.Add(bodyBox);
        footerBox.Background = Paint("#590A0F14"); footerBox.BorderBrush = Frame; footerBox.BorderThickness = new Thickness(0, 1, 0, 0); footerBox.CornerRadius = new CornerRadius(0, 0, 7, 7); footerBox.Padding = new Thickness(12, 6, 12, 6); footerBox.Child = footer;
        rootLayout.Children.Add(footerBox);
        notice.BorderBrush = Frame; noticeText.TextWrapping = TextWrapping.Wrap; notice.Child = noticeText;
        byte alpha = (byte)Math.Clamp(settings.OverlayOpacity * 255, 90, 250);
        var withGrip=new Grid();withGrip.Children.Add(patternLayer);withGrip.Children.Add(rootLayout);withGrip.Children.Add(topBar);withGrip.Children.Add(grip);
        root=new Border{Background=new SolidColorBrush(Color.FromArgb(alpha,13,20,27)),BorderBrush=Frame,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(8),Child=withGrip};
        BuildGrip();
        BuildCubeBar();contentStack.Children.Add(cubeBar);rootHost.Children.Add(glowLayer);rootHost.Children.Add(root);contentStack.Children.Add(rootHost);contentStack.Children.Add(pinCards);Content=contentStack;
        SourceInitialized+=(_,_)=>ApplyStyle();
        MouseLeftButtonDown+=(_,e)=>{if(ClickThrough||e.ButtonState!=MouseButtonState.Pressed)return;DragMove();SavePosition();};
        drops=new(settings.OverlayOpacity);
        dock.SetDropsOn(settings.OverlayDrops);
        dock.DropsClicked+=ToggleDrops;
        dock.SettingsClicked+=()=>SettingsRequested?.Invoke();
        dock.BreakdownClicked+=()=>BreakdownRequested?.Invoke(0);
        dock.NewsClicked+=()=>NewsRequested?.Invoke();
        dock.SetTroopOn(settings.OverlayTroopAll);
        dock.TroopClicked+=()=>{settings.OverlayTroopAll=!settings.OverlayTroopAll;settings.Save();dock.SetTroopOn(settings.OverlayTroopAll);};
        // With the overlay unlocked (Ctrl+Alt+M), a click on the own name opens the breakdown, as in the game's analyzer.
        selfName.Cursor=Cursors.Hand;selfName.MouseLeftButtonDown+=(_,e)=>{if(ClickThrough)return;e.Handled=true;BreakdownRequested?.Invoke(0);};
        dock.DragFinished+=SavePosition;
        // The strip and the drops panel ride along: overlay | strip | drops, top edges aligned.
        LocationChanged+=(_,_)=>Arrange(fromDock:false);
        dock.LocationChanged+=(_,_)=>Arrange(fromDock:true);
        minimap=new(settings);minimap.Closed_+=ToggleMinimap;dock.MinimapClicked+=ToggleMinimap;dock.SetMinimapOn(settings.MinimapVisible);
        Closed+=(_,_)=>{dock.Close();drops.Close();player.Close();minimap.Close();centerAlert.Close();bossWindow.Close();toast.Close();assistant.Close();};
        centerAlert.Shown+=()=>{centerAlert.Topmost=!settings.OverlayAttachToGame;Restack(GetForegroundWindow());};
        HookForeground();
        Closed+=(_,_)=>{if(foregroundHook!=IntPtr.Zero)UnhookWinEvent(foregroundHook);};
        BuildStyleParts();
        ApplyOverlayStyle(); // places the parts and applies the appearance
    }
    /// <summary>Opacity and scale from Configuración > Pantalla, applied live to the overlay, its strip and the drops panel.</summary>
    public void ApplyAppearance()
    {
        double k=Math.Clamp(settings.OverlayScale,.7,1.8);
        byte alpha=(byte)Math.Clamp(settings.OverlayOpacity*255,60,250);
        var back=C(palette.Back);byte a=(byte)(alpha*palette.Glass);
        // A second colour makes the background a vertical gradient (Gamer Girl, Synthwave, Steampunk...).
        root.Background=look.Back2 is {} second?new LinearGradientBrush(Color.FromArgb(a,back.R,back.G,back.B),Color.FromArgb(a,C(second).R,C(second).G,C(second).B),90):new SolidColorBrush(Color.FromArgb(a,back.R,back.G,back.B));
        contentStack.LayoutTransform=new ScaleTransform(k,k);Width=LayoutWidth*k;
        dock.ApplyScale(k);drops.ApplyAppearance(alpha,k);player.ApplyAppearance(alpha,k);
        Arrange(fromDock:false);
    }
    // "Solo sobre el juego". Two things Windows does not allow from this process: making the game the owner window
    // (AION 2 runs with more privileges; WPF puts its hidden owner back) and raising a normal window above the
    // foreground one. What works, and what other game overlays do: topmost while the game (or this app) is in front;
    // when another app comes forward, the overlay is slotted right below that app - no longer topmost, so the app
    // covers it, yet still above the game and everything else.
    const uint NoMoveSizeActivate = 0x0001 | 0x0002 | 0x0010;
    Window[] Family => [this, dock, drops, player, minimap, centerAlert, bossWindow, toast, assistant, assistant.Grip];
    readonly AssistantWindow assistant;
    PlayerCard? ownCard;
    /// <summary>The combat assistant bar: buffs and cooldowns, shown while the overlay is.</summary>
    public void UpdateAssistant(IReadOnlyList<TimerTile> buffs, IReadOnlyList<TimerTile> cooldowns) => assistant.Update(buffs, cooldowns, settings.AssistantVisible && IsVisible);
    public Action? AssistantBeep { set => assistant.Beep = value; }
    public void AssistantLook() => assistant.ApplyScale();
    /// <summary>The strip's megaphone: the news window, and its unread dot.</summary>
    public event Action? NewsRequested;
    public void SetNewsUnread(bool unread, string? tip = null) => dock.SetNewsUnread(unread, tip);
    public bool AssistantMoving { get => assistant.Moving; set => assistant.SetMoving(value); }
    public void AssistantReset() => assistant.ResetPlace();
    /// <summary>An event notification in the chosen corner of the game window.</summary>
    public void Toast(string name, string color, DateTimeOffset at, bool starting) => toast.Notify(name, color, at, starting);
    public void MinimapLook() => minimap.ApplyLook();
    /// <summary>The environment objects present now, for the minimap.</summary>
    public Func<IReadOnlyList<EnvObject>>? EnvObjects { set => minimap.EnvObjects = value; }
    public Func<CubeView>? CubeGuide { set => minimap.Cubes = value; }
    /// <summary>The player's own pins and routes of a map (AtlasMarks), drawn on the minimap.</summary>
    public Func<string, AtlasMarks.Map?>? OwnMarks { set => minimap.Marks = value; }
    public void MinimapReset() => minimap.ResetPlace();
    /// <summary>An administration notice in the same corner (plain text).</summary>
    public void Notice(string title, string body, bool warning) => toast.Message(title, body, warning);
    void Restack(IntPtr foreground)
    {
        if (!settings.OverlayAttachToGame || foreground == IntPtr.Zero) return;
        // The companion panel counts as another app: the overlay goes below it, not over it.
        bool ours = foreground == gameWindow || Family.Any(w => new WindowInteropHelper(w).Handle == foreground);
        foreach (var w in Family)
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd != IntPtr.Zero && w.IsVisible) SetWindowPos(hwnd, ours ? (IntPtr)(-1) : foreground, 0, 0, 0, 0, NoMoveSizeActivate);
        }
    }
    /// <summary>Applies "Solo sobre el juego" after the setting changes or the game restarts.</summary>
    public void Reattach()
    {
        bool attach = settings.OverlayAttachToGame;
        attachedTo = attach ? gameWindow : IntPtr.Zero;
        // WPF's Topmost stays off while attached: the stacking is managed by Restack, and WPF re-applying it would undo that.
        foreach (var w in Family) w.Topmost = !attach;
        Restack(GetForegroundWindow());
    }
    /// <summary>The companion panel: above everything while open when "Solo sobre el juego" is off, else like the overlay.</summary>
    public void AttachPanel(Window panel) => panel.Topmost = !settings.OverlayAttachToGame;
    // EVENT_SYSTEM_FOREGROUND: restack the moment any window comes to the front.
    delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int obj, int child, uint thread, uint time);
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
    WinEventProc? foregroundProc;
    IntPtr foregroundHook;
    void HookForeground()
    {
        foregroundProc = (_, _, hwnd, _, _, _, _) => Restack(hwnd);
        foregroundHook = SetWinEventHook(3, 3, IntPtr.Zero, foregroundProc, 0, 0, 0);
    }
    /// <summary>"Probar aviso": a sample warning where the settings put it, with the sound.</summary>
    public void TestCastAlert()
    {
        if (settings.CastAlertPlace is "center" or "both") centerAlert.Flash("Spirit Wave", "Kelpina de onda azul");
        BossCast?.Invoke(new CastRow(0, "Kelpina de onda azul", 1502850, "Spirit Wave", 0, true, true));
    }
    void SavePosition(){settings.OverlayLeft=Math.Round(Left);settings.OverlayTop=Math.Round(Top);settings.Save();}
    void Arrange(bool fromDock)
    {
        if(arranging)return;arranging=true;
        if(fromDock){Left=dock.Left-Width-4;Top=dock.Top;}else{dock.Left=Left+Width+4;dock.Top=Top;}
        drops.Left=player.Left=dock.Left+dock.Width+4;drops.Top=player.Top=Top;
        arranging=false;
    }
    public void ToggleMinimap(){settings.MinimapVisible=!settings.MinimapVisible;if(settings.MinimapVisible)PlusRequested?.Invoke();settings.Save();dock.SetMinimapOn(settings.MinimapVisible);}
    public void ToggleDrops(){settings.OverlayDrops=!settings.OverlayDrops;if(settings.OverlayDrops)PlusRequested?.Invoke();settings.Save();dock.SetDropsOn(settings.OverlayDrops);}
    static Brush Paint(string color) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); b.Freeze(); return b; }
    void BuildPetCard()
    {
        var top = new DockPanel { LastChildFill = true };
        petBadgeBox.Child = petBadge; DockPanel.SetDock(petBadgeBox, Dock.Left); top.Children.Add(petBadgeBox);
        DockPanel.SetDock(petCategory, Dock.Right); petCategory.Margin = new Thickness(8, 0, 0, 0); top.Children.Add(petCategory);
        DockPanel.SetDock(petRace, Dock.Right); top.Children.Add(petRace);
        top.Children.Add(petTitle);
        var bar = new Grid { Height = 7, Margin = new Thickness(0, 6, 0, 4) };
        bar.ColumnDefinitions.Add(petFill); bar.ColumnDefinitions.Add(petEmpty);
        var track = new Border { Background = Paint("#22313C"), CornerRadius = new CornerRadius(3) }; Grid.SetColumnSpan(track, 2);
        bar.Children.Add(track); bar.Children.Add(petBar);
        var bottom = new DockPanel(); DockPanel.SetDock(petCount, Dock.Left); bottom.Children.Add(petCount);
        petDetail.HorizontalAlignment = HorizontalAlignment.Right; petDetail.Margin = new Thickness(8, 0, 0, 0); bottom.Children.Add(petDetail);
        var stack = new StackPanel(); stack.Children.Add(top); stack.Children.Add(bar); stack.Children.Add(bottom);
        petCard.Child = stack;
    }
    /// <summary>The pet card of the target: level badge, progress bar, souls so far and still to go, today's hunt.</summary>
    void UpdatePetCard(PetStatus? p)
    {
        if (p is null || !settings.OverlayShowPet) { petCard.Visibility = Visibility.Collapsed; return; }
        petCard.Visibility = Visibility.Visible;
        bool max = p.Threshold == 0, locked = p.Level == 0;
        petTitle.Text = "🐾 " + p.Name;
        petBadge.Text = max ? "Nv. 3 · MÁX." : locked ? "Sin desbloquear" : $"Nv. {p.Level}";
        petBadgeBox.Background = max ? Gold : locked ? Paint("#95A4B6") : Paint("#5FD3B4");
        petCategory.Text = p.Source == "?" ? "esperando la lista del juego" : p.Source;
        petRace.Source = Icon(BadgeOf(p.Source)); petRace.ToolTip = p.Source;
        bool raced = petRace.Source is not null; petRace.Visibility = raced ? Visibility.Visible : Visibility.Collapsed; petCategory.Visibility = raced ? Visibility.Collapsed : Visibility.Visible;
        double ratio = max ? 1 : Math.Clamp(p.Progress / (double)p.Threshold, 0, 1);
        petFill.Width = new GridLength(ratio, GridUnitType.Star); petEmpty.Width = new GridLength(1 - ratio, GridUnitType.Star);
        petBar.Background = max ? Gold : Paint("#5FD3B4");
        petCount.Text = max ? "Nivel máximo alcanzado" : $"{p.Progress} / {p.Threshold} almas · faltan {p.Remaining}" + (locked ? " para desbloquear" : $" para Nv. {p.Level + 1}");
        petDetail.Text = p.Souls > 0 || p.Kills > 0
            ? $"hoy {p.Souls} {(p.Souls == 1 ? "alma" : "almas")} · {p.Kills} muertes" + (p.KillsLeft is int k ? $" · ~{k} más" : "")
            : "hoy sin almas aún";
    }
    static TextBlock Label(string text, double size, Brush color) => new() { Text = text, FontSize = size, Foreground = color, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    static StackPanel Row(params UIElement[] items) { var row = new StackPanel { Orientation = Orientation.Horizontal }; foreach (var i in items) row.Children.Add(i); return row; }
    static string Short(double v) => v >= 1_000_000 ? $"{v / 1_000_000:0.0}M" : v >= 10_000 ? $"{v / 1_000:0.#}k" : v.ToString("N0");
    static string Clock(double seconds) { var t = TimeSpan.FromSeconds(Math.Max(0, seconds)); return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss"); }
    // Internal asset keys (e.g. "Etiquetaskill_FX") leak through when the client targets an effect entity.
    static bool Readable(string? name) => !string.IsNullOrWhiteSpace(name) && !Regex.IsMatch(name, @"^([A-Za-z0-9]+(_[A-Za-z0-9]+)+|Entidad \d+|Criatura \d+)$");
    void ApplyStyle(){var hwnd=new WindowInteropHelper(this).Handle;if(hwnd==IntPtr.Zero)return;int style=GetWindowLong(hwnd,-20);style|=0x08000000|0x80;style=ClickThrough ? style|0x20 : style&~0x20;SetWindowLong(hwnd,-20,style);}
    public void SetClickThrough(bool value)
    {
        ClickThrough=value;ApplyStyle();
        // Move mode must be unmistakable: bright frame, thicker border, instructions at the bottom.
        root.BorderBrush=value?Frame:MoveFrame;root.BorderThickness=new Thickness(look.Border+(value?0:1));
        hint.Text=value?"":"MODO MOVER · arrastra el panel · clic en el objetivo abre su ficha · Ctrl+Alt+M para fijar";
        hint.Visibility=value?Visibility.Collapsed:Visibility.Visible;
        grip.Visibility=value?Visibility.Collapsed:Visibility.Visible;
        assistant.SetClickThrough(value);
    }
    internal new static ImageSource? Icon(string? path)
    {
        if (path is null) return null;
        if (icons.TryGetValue(path, out var cached)) return cached;
        ImageSource? image = null;
        try
        {
            string file = System.IO.Path.Combine(AppContext.BaseDirectory, path.TrimStart('/').Replace('/', System.IO.Path.DirectorySeparatorChar));
            if (System.IO.File.Exists(file))
            {
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.UriSource = new Uri(file); bitmap.DecodePixelWidth = path.Contains("pet-badges") ? 180 : 76; bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.EndInit(); bitmap.Freeze();
                image = bitmap;
            }
        }
        catch { }
        return icons[path] = image;
    }
    public void Update(CaptureSnapshot s, TimerRow? e, IReadOnlyList<ActorRow> players)
    {
        var now = DateTime.Now;
        if (s.Damage != lastDamage) { lastDamage = s.Damage; lastChange = now; }
        bool fighting = s.Damage > 0 && (now - lastChange).TotalSeconds < 6;
        if (s.Packets != lastPackets) { lastPackets = s.Packets; lastPacket = now; }
        // The game streams constantly while a character is in the world; ten silent seconds means nothing is captured.
        bool idle = !s.Status.StartsWith("Captura activa", StringComparison.Ordinal) || (now - lastPacket).TotalSeconds > 10;
        stateDot.Fill = idle ? Gold : s.Paused ? Gold : fighting ? Teal : Dim;
        state.Text = idle ? "INACTIVO" : s.Paused ? "EN PAUSA" : fighting ? "EN COMBATE" : s.LocalId > 0 ? "EN ESPERA" : "IDENTIDAD PENDIENTE";
        state.Foreground = idle || s.Paused ? Gold : fighting ? Teal : Muted;
        clock.Text = s.Duration > 0 ? Clock(s.Duration) : "";
        // The game's own ping (its 10 s ping/answer pair), same figure as the in-game display.
        UpdateFooter(s);

        var t = s.Target;
        target.Text = t is null ? "Sin objetivo · selecciona una criatura" : Readable(t.Name) ? t.Name : t.NpcId is int npc ? $"Objetivo sin ficha · {npc}" : "Objetivo sin identificar";
        target.Foreground = t is not null && Readable(t.Name) ? Bright : Muted;
        // Level and rank from the local catalog; elite ranks stand out so a field boss is obvious at a glance.
        var record = t?.NpcId is int id ? Catalog.Npc(id) : null;
        string? grade = record is { } r && r.TryGetProperty("grade", out var g) ? g.GetString() : null;
        // Ordinary creatures are scaled to the player's level by the game client (Kailin joven: base 20 in the spawn
        // frame and the catalog, shown as 45 in the game) - only elite levels, which are fixed, are worth showing.
        string gradeName = record is { } rg && rg.TryGetProperty("gradeName", out var gn) && gn.ValueKind == System.Text.Json.JsonValueKind.String ? gn.GetString() ?? "" : "";
        bool eliteGrade = record is { } re && re.TryGetProperty("grade", out var gr) && gr.GetString() is "HeroMonster" or "LegendMonster" or "UniqueMonster" or "BossMonster";
        string meta = record is { } rec && eliteGrade && rec.TryGetProperty("level", out var lv) && lv.ValueKind == System.Text.Json.JsonValueKind.Number
            ? $"Nv. {lv.GetInt32()} · {gradeName}" : gradeName;
        // "Combate · 0:38" like TeamMate's "Fight · 00:38", while there is a fight on this target.
        double? fightFor = t?.FightSeconds ?? (fighting && s.Duration > 0 ? s.Duration : null);
        targetMeta.Text = fightFor is double fs && t is not null ? $"Combate · {(int)fs / 60}:{(int)fs % 60:00}" + (meta.Length > 0 ? " · " + meta : "") : meta;
        bool elite = grade is "HeroMonster" or "LegendMonster" or "UniqueMonster" or "BossMonster";
        targetMeta.Foreground = elite ? Gold : Muted;
        currentNpc = record is null ? null : t!.NpcId;
        // A creature that gives pet souls shows its pet's portrait (the same as the pet list, the pinned cards and the
        // game's soul notice); others their catalog portrait.
        targetIcon.Source = s.Pet?.Target is { } targetPet ? Icon(petIcons.GetValueOrDefault(targetPet.PetId)) : null;
        if (targetIcon.Source is null) targetIcon.Source = record is { } withIcon && withIcon.TryGetProperty("icon", out var ic) && ic.ValueKind == System.Text.Json.JsonValueKind.String ? Icon(ic.GetString()) : null;
        // Monster portraits of the web catalog are not shipped: the portrait of the pet whose soul it drops stands in.
        if (targetIcon.Source is null && record is { } withDrops && withDrops.TryGetProperty("drops", out var dropList) && dropList.ValueKind == System.Text.Json.JsonValueKind.Array)
            foreach (var d in dropList.EnumerateArray())
                if (d.ValueKind == System.Text.Json.JsonValueKind.Object && d.TryGetProperty("category", out var dc) && dc.GetString() == "Mascota" && d.TryGetProperty("icon", out var di) && Icon(di.GetString()) is { } soul) { targetIcon.Source = soul; break; }
        // ...or the pet portrait of the creature's pet (the same picture as the pet list and the pinned cards).
        if (targetIcon.Source is null && s.Pet?.Target is { } petOfTarget) targetIcon.Source = Icon(petIcons.GetValueOrDefault(petOfTarget.PetId));
        targetFrame.Visibility = targetIcon.Source is null ? Visibility.Collapsed : Visibility.Visible;
        targetFrame.BorderBrush = elite ? Gold : Frame;
        string? category = record is { } rc && (rc.TryGetProperty("creature", out var cat) || rc.TryGetProperty("category", out cat)) && cat.ValueKind == System.Text.Json.JsonValueKind.String ? cat.GetString() : null;
        targetBadge.Source = Icon(BadgeOf(category));
        targetBadge.Visibility = targetBadge.Source is null ? Visibility.Collapsed : Visibility.Visible;
        targetBadge.ToolTip = category;
        bool hp = t?.Hp is long && t.HighestObservedHp > 0;
        hpBar.Visibility = hp ? Visibility.Visible : Visibility.Collapsed;
        if (!hp) { bossPace.Visibility = Visibility.Collapsed; enrageTick.Visibility = Visibility.Collapsed; }
        bossWindow.ShowBars = settings.BossPaceBars;
        bossWindow.Update(hp ? t : null, settings.BossPacePlace is "window" or "both");
        if (hp)
        {
            long cur = t!.Hp!.Value, max = t.HighestObservedHp!.Value;
            double ratio = Math.Clamp((double)cur / max, 0, 1);
            // A pale trailing segment shows the chunk just lost and catches up over a few ticks.
            if (t.EntityId != hpTarget || ratio > hpLagRatio) { hpTarget = t.EntityId; hpLagRatio = ratio; }
            else hpLagRatio = Math.Max(ratio, hpLagRatio - Math.Max(.004, (hpLagRatio - ratio) * .35));
            hpFill.Width = HpWidth * ratio; hpLag.Width = HpWidth * hpLagRatio;
            UpdatePace(t, ratio);
            hpFill.Background = ratio > .2 ? HpGreen : HpLow;
            hpText.Text = $"{cur:N0} / {max:N0}"; hpText.HorizontalAlignment = HorizontalAlignment.Center; hpText.TextAlignment = TextAlignment.Center;
            // Without a maximum from an earlier fight, the highest HP seen may be below the real one: the share is an upper bound.
            hpPct.Text = t.MaxHpKnown || t.NpcId is not int boss || !Catalog.IsElite(boss) ? $"{ratio * 100:0.0}%" : $"≤ {ratio * 100:0.0}%";
        }

        UpdateCasts(s.Casts ?? [], s.World);
        // What the competition shows as "teleport to be detected": the one thing that makes the numbers unreliable now.
        string? warning = idle ? "Buscando tráfico del juego… entra con tu personaje en AION 2"
            : s.LocalId <= 0 ? "Teletranspórtate a algún lugar para detectar tu personaje"
            : t is not null && !Readable(t.Name) ? "Objetivo sin identificar: el juego no lo anunció (ya estaba cerca). Aléjate hasta perderlo de vista y vuelve, o elige otro"
            : hp && !t!.MaxHpKnown && elite && t.Hp < t.HighestObservedHp ? "Vida máxima sin confirmar · el % es un máximo hasta verlo con vida llena"
            : s.IdentityInferred ? "Personaje deducido · teletranspórtate para confirmarlo"
            : null;
        noticeText.Text = warning is null ? "" : "⌖  " + warning;
        notice.Visibility = warning is null ? Visibility.Collapsed : Visibility.Visible;

        dps.Text = s.LocalId > 0 ? s.Dps.ToString("N0") : "—";
        dps.Foreground = fighting || s.LocalId <= 0 ? Teal : IdleTeal;
        dmgLine.Text = s.LocalId > 0 ? $"{Short(s.Damage)} daño" : "Esperando tu personaje";
        healLine.Text = s.Healing > 0 ? $"{Short(s.Healing)} curación" : "";
        healLine.Visibility = s.Healing > 0 ? Visibility.Visible : Visibility.Collapsed;
        killLine.Text = s.ConfirmedKills == 1 ? "1 criatura" : $"{s.ConfirmedKills} criaturas";

        party.Children.Clear();
        var troop = s.Troop;
        bool wholeTroop = troop is { IsTroop: true } && settings.OverlayTroopAll;
        var shown = troop?.Members.Where(m => wholeTroop || m.Group == troop.OwnGroup).ToList();
        string troopRegion = PlayerProfiles.RegionOf(settings.RegionOffset);
        // The instance room knows class and PC of everyone in it, players of other servers included (Elymas [Zik], 2026-10-13).
        RoomMember? RoomOf(string name) => s.Room?.Members.FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        string? ClassOf(string name) => shown?.FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.ClassKey ?? RoomOf(name)?.ClassKey
            ?? (PlayerProfiles.Get(name, s.LocalServerId ?? settings.LastServerId, troopRegion).Card is { } c && ClassKeys.TryGetValue(c.ClassName, out var k) ? k : null);
        var top = players.Where(p => p.Damage > 0).Take(wholeTroop ? 12 : 6).ToList();
        long best = top.Count == 0 ? 1 : Math.Max(1, top[0].Damage), sum = Math.Max(1, players.Sum(p => p.Damage));
        long? PowerOf(string name) => shown?.FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { CombatPower: > 0 } tm ? tm.CombatPower
            : RoomOf(name) is { CombatPower: > 0 } rm ? rm.CombatPower
            : PlayerProfiles.Get(name, s.LocalServerId ?? settings.LastServerId, troopRegion).Card?.CombatPower;
        if (fighting && top.Count > 0)
        {
            if (wholeTroop) party.Children.Add(Label($"TROPA · DAÑO · {players.Count(p => p.Damage > 0)} con daño", 9.5, Dim));
            for (int i = 0; i < top.Count; i++) party.Children.Add(PartyRow(i + 1, top[i], top[i].Id == s.LocalId, (double)top[i].Damage / best, (double)top[i].Damage / sum, ClassOf(top[i].Name), PowerOf(top[i].Name)));
        }
        else if (s.Room is { Members.Count: > 0 } roomInfo)
        {
            // In an instance room: its roster as the game sends it (server, gear score, combat power, leader).
            party.Children.Add(Label($"SALA · {roomInfo.Dungeon} · {roomInfo.Members.Count}/{roomInfo.Capacity} · {roomInfo.Members.Count(m => m.Ready || m.Leader)}/{roomInfo.Members.Count} listos", 9.5, Gold));
            if (roomInfo.Title.Length > 0) party.Children.Add(Label($"“{roomInfo.Title}”", 10, Dim));
            party.Children.Add(Label("MIEMBRO  ·  PE  ·  PC  ·  ESTADO", 9.5, Dim));
            foreach (var m in roomInfo.Members) party.Children.Add(RoomRow(m, m.Name.Equals(s.Character, StringComparison.OrdinalIgnoreCase), PlayerProfiles.RegionOf(settings.RegionOffset)));
        }
        else if (shown is { Count: > 0 } && troop is not null)
        {
            // Party or troop roster from the game itself (class, gear score, combat power); the own entry and anyone the
            // game has not detailed yet fall back to the official profile. In a troop, a header per group.
            party.Children.Add(Label(troop.IsTroop
                ? wholeTroop ? $"TROPA · {troop.Members.Count}/20 · tu grupo: {troop.OwnGroup}" : $"TROPA · GRUPO {troop.OwnGroup} · {shown.Count} miembros"
                : "GRUPO", 9.5, troop.IsTroop ? Gold : Dim));
            party.Children.Add(ColumnsHeader());
            foreach (var grp in shown.GroupBy(m => m.Group))
            {
                if (wholeTroop) party.Children.Add(Label($"Grupo {grp.Key}{(grp.Key == troop.OwnGroup ? "  (el tuyo)" : "")}", 9, grp.Key == troop.OwnGroup ? Teal : Muted));
                foreach (var m in grp) party.Children.Add(TroopRow(m, m.Name.Equals(s.Character, StringComparison.OrdinalIgnoreCase), PlayerProfiles.Get(m.Name, m.ServerId, troopRegion)));
            }
        }
        else if (s.Party is { Count: > 1 } members)
        {
            // Out of combat: the party with combat power (PC) and gear score (PE) from the official profiles, as the
            // room screens show them. Loaded through the same cache as the PvP panel.
            int server = s.LocalServerId ?? settings.LastServerId; string region = troopRegion;
            party.Children.Add(Label("GRUPO", 9.5, Dim));
            party.Children.Add(ColumnsHeader());
            int slot = 0;
            foreach (string member in members.Take(10))
                party.Children.Add(TroopRow(new TroopMember(0, member, server, 1, ++slot, 0, 0, 0, null), member.Equals(s.Character, StringComparison.OrdinalIgnoreCase), PlayerProfiles.Get(member, server, region)));
        }
        party.Visibility = settings.OverlayShowParty && party.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // Od energy for dungeons, as the game last sent it (only on changes).
        if (s.Od is { } odNow && settings.OverlayShowOd)
        {
            odGroup.Visibility = Visibility.Visible;
            odText.Text = $"{odNow.Current} (+{odNow.Bonus})";
            odGroup.ToolTip = $"Energía Od {odNow.Current} (+{odNow.Bonus}) / {settings.OdMax} · actualizada {odNow.At:HH:mm}";
        }
        else odGroup.Visibility = Visibility.Collapsed;
        UpdatePetCard(s.Pet?.Target);
        UpdatePinCards(s.Pet?.Pinned);
        UpdateFarmBlock(s);
        UpdateLootFeed(s);
        UpdateRankLine();
        UpdateObjective(s);
        // Pet hunt: souls so far, still to go, and the kills that will take at the drop rate seen this session.
        if (petCard.Visibility == Visibility.Visible) petLine.Visibility = Visibility.Collapsed; // the card already says it
        else if (s.Pet?.Tracked is { } pet && settings.OverlayShowPet)
        {
            petLine.Visibility = Visibility.Visible; petLine.Margin = new Thickness(0, 4, 0, 0);
            petLine.Text = pet.Threshold == 0 ? $"🐾 {pet.Name} · Nv. {pet.Level} · MÁX."
                : $"🐾 {pet.Name} · Nv. {pet.Level} · {pet.Progress}/{pet.Threshold} · faltan {pet.Remaining}{(pet.KillsLeft is int k ? $" (~{k} muertes)" : "")}";
        }
        else if (s.Pet is { LastSoul: { } lastPet, LastSoulAt: { } lastAt } hunt && settings.OverlayShowPet && DateTime.Now - lastAt < TimeSpan.FromMinutes(30))
        {
            // Nothing followed: the latest soul, so the hunt is visible without setting anything up.
            int today = hunt.Today.FirstOrDefault(d => d.Name == lastPet)?.Souls ?? 0;
            petLine.Visibility = Visibility.Visible; petLine.Margin = new Thickness(0, 4, 0, 0);
            petLine.Text = $"🐾 +1 {lastPet} · {today} {(today == 1 ? "alma" : "almas")} hoy";
        }
        else petLine.Visibility = Visibility.Collapsed;
        if (e is null || !settings.OverlayShowEvent) { timer.Text = ""; timer.Visibility = Visibility.Collapsed; }
        else
        {
            timer.Visibility = Visibility.Visible;
            timer.Text = $"{e.Name}  ·  {Clock(e.Remaining)}";
            timer.Foreground = e.Remaining <= settings.LeadMinutes * 60 ? Alert : Gold;
            timer.Margin = new Thickness(0, 6, 0, 0);
        }
        UpdateStyleParts(s, players, fighting, t);
        ThemeFamily();

        // Stays up whatever window has the focus (alt-tab, browser, Discord); it only goes away with the game.
        bool recheck=(now-gameChecked).TotalSeconds>=5;
        if(recheck)
        {
            gameChecked=now;var running=Process.GetProcessesByName("AION2");gameRunning=running.Length>0;
            gameWindow=running.Select(p=>p.MainWindowHandle).FirstOrDefault(h=>h!=IntPtr.Zero);
            foreach(var p in running)p.Dispose();
        }
        bool attach=settings.OverlayAttachToGame&&gameWindow!=IntPtr.Zero;
        if(Requested && gameRunning && !(attach && IsIconic(gameWindow)))
        {
            if(!IsVisible){PlusRequested?.Invoke();Show();dock.Show();Arrange(fromDock:false);Restack(GetForegroundWindow());}
            if(attachedTo!=(attach?gameWindow:IntPtr.Zero))Reattach();
            // Another topmost window (the game going borderless, a browser in full screen) can cover it; re-assert now and then.
            else if(recheck&&!PanelOpen&&!attach){foreach(var w in new Window[]{this,dock,drops}){w.Topmost=false;w.Topmost=true;}}
            else if(recheck&&attach)Restack(GetForegroundWindow());
            // The side panel: a player's official profile (PvP), else the creature's loot table.

            if(s.LocalServerId is int own&&own!=settings.LastServerId){settings.LastServerId=own;settings.Save();}
            // The PvP card and the minimap follow the plan the account grants (Companion Plus).
            bool playerTarget=settings.OverlayDrops&&t is {NpcId:null}&&Readable(t.Name);
            string? playerKey=playerTarget?$"{t!.EntityId}|{t.ServerId}|{t.Name}":null;
            if(playerTarget&&playerKey!=lastPlayerTarget)PlusRequested?.Invoke();lastPlayerTarget=playerKey;
            bool isPlayer=playerTarget&&Feature("companionPvp");
            if(isPlayer)
            {
                int server=t!.ServerId??s.LocalServerId??settings.LastServerId;string region=PlayerProfiles.RegionOf(settings.RegionOffset);
                var mine=s.Character is {Length:>0} me?PlayerProfiles.Get(me,s.LocalServerId??settings.LastServerId,region).Card:null;
                player.Load($"{region}|{server}|{t.Name}",t.Name,PlayerProfiles.Get(t.Name,server,region),mine);
                if(!player.IsVisible){player.Show();player.Topmost=!attach;Restack(GetForegroundWindow());Arrange(fromDock:false);}
            }
            else if(player.IsVisible)player.Hide();
            bool loot=!isPlayer&&settings.OverlayDrops&&drops.Load(currentNpc);
            if(loot&&!drops.IsVisible){drops.Show();drops.Topmost=!attach;Restack(GetForegroundWindow());Arrange(fromDock:false);}else if(!loot&&drops.IsVisible)drops.Hide();
            bool mapAllowed=Feature("companionMinimap");dock.SetMinimapAccess(mapAllowed);
            if(settings.MinimapVisible&&mapAllowed){minimap.Render(s);if(!minimap.IsVisible){minimap.Show();minimap.Topmost=!attach;Restack(GetForegroundWindow());}}
            else if(minimap.IsVisible)minimap.Hide();
        }
        else if(IsVisible){Hide();dock.Hide();drops.Hide();player.Hide();minimap.Hide();bossWindow.Hide();}
    }
    // The caster's kind: a boss in a dungeon or instance, a field / world boss, an elite, or an ordinary monster.
    static string KindOf(CastRow c, int? world)
    {
        string grade = c.NpcId is int npc && Catalog.Npc(npc) is { } r && r.TryGetProperty("grade", out var g) ? g.GetString() ?? "" : "";
        bool boss = grade is "HeroMonster" or "LegendMonster" or "UniqueMonster" or "BossMonster";
        return boss ? (world is > 100_000 ? "dungeon" : "field") : grade == "EliteMonster" ? "elite" : "normal";
    }
    bool Wants(HashSet<string> kinds, CastRow c, int? world) => kinds.Contains(KindOf(c, world)) || c.IsTarget && kinds.Contains("target");
    void UpdateCasts(IReadOnlyList<CastRow> casts, int? world)
    {
        var relevant = casts.Where(c => Wants(settings.CastBannerKinds, c, world) || Wants(settings.CastSoundKinds, c, world)).ToList();
        var latest = relevant.FirstOrDefault();
        bool alert = latest is not null && latest.Age < 4;
        bool inOverlay = settings.CastAlertPlace is "overlay" or "both", inCenter = settings.CastAlertPlace is "center" or "both";
        castBanner.Visibility = alert && inOverlay && settings.OverlayShowCasts && Wants(settings.CastBannerKinds, latest!, world) ? Visibility.Visible : Visibility.Collapsed;
        if (alert)
        {
            castName.Text = "⚠  " + latest!.Skill.ToUpperInvariant();
            string lands = latest.CastMs > 0 ? $"cae en {Math.Max(0, latest.CastMs / 1000.0 - latest.Age):0.0} s" : "";
            castFrom.Text = string.Join("  ·  ", new[] { latest.IsTarget ? "" : latest.Caster, lands }.Where(x => x.Length > 0));
            castBlink = !castBlink; castBanner.Opacity = castBlink ? 1 : .78;
            // One warning per cast: the same cast stays in the list for several ticks.
            var cast = (latest.CasterId, latest.SkillId, DateTime.Now.AddSeconds(-latest.Age));
            if (latest.Age < 1.6 && (cast.CasterId != lastAlert.Caster || cast.SkillId != lastAlert.Skill || (cast.Item3 - lastAlert.At).TotalSeconds > 3))
            {
                lastAlert = cast;
                bool banner = Wants(settings.CastBannerKinds, latest, world);
                if (banner && inCenter && settings.OverlayShowCasts) centerAlert.Flash(latest.Skill, string.Join("  ·  ", new[] { latest.IsTarget ? "" : latest.Caster, latest.CastMs > 0 ? $"{latest.CastMs / 1000.0:0.0} s" : "" }.Where(x => x.Length > 0)) is { Length: > 0 } sub ? sub : null);
                if (Wants(settings.CastSoundKinds, latest, world)) BossCast?.Invoke(latest);
            }
        }
        var log = relevant.Skip(alert ? 1 : 0).Take(3).Select(c => $"{c.Skill} ({c.Age:0}s)").ToList();
        castLog.Text = log.Count == 0 ? "" : "Últimas: " + string.Join("  ·  ", log);
        castLog.Visibility = log.Count == 0 || !settings.OverlayShowCasts ? Visibility.Collapsed : Visibility.Visible;
    }
    UIElement TargetRow()
    {
        targetFrame.Child = new Border { CornerRadius = new CornerRadius(5), ClipToBounds = true, Child = targetIcon };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(target); text.Children.Add(targetMeta);
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 0), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        DockPanel.SetDock(targetFrame, Dock.Left); row.Children.Add(targetFrame);
        DockPanel.SetDock(targetBadge, Dock.Right); row.Children.Add(targetBadge); row.Children.Add(text);
        row.PreviewMouseLeftButtonDown += (_, e) => { if (ClickThrough || currentNpc is not int npc) return; e.Handled = true; OpenNpc?.Invoke(npc); };
        return row;
    }
    static string PaceClock(double seconds) => seconds >= 3600 ? "+1 h" : $"{(int)seconds / 60}:{(int)seconds % 60:00}";
    // The boss card: inside the overlay, in its own window under the game's boss bar, or both (Configuración).
    void UpdatePace(TargetInfo t, double ratio)
    {
        bool shown = bossPace.Update(t);
        bossPace.Visibility = shown && settings.BossPacePlace is "overlay" or "both" ? Visibility.Visible : Visibility.Collapsed;
        double at = t.HpAtEnragePct ?? 0;
        bool tick = shown && !t.Enraged && t.EnrageIn is double until && until > 0 && at > 0 && at / 100 < ratio;
        enrageTick.Visibility = tick ? Visibility.Visible : Visibility.Collapsed;
        if (tick) enrageTick.Margin = new Thickness(HpWidth * at / 100, 1, 0, 1);
    }
    static readonly Dictionary<string, string> ClassKeys = new(StringComparer.OrdinalIgnoreCase)
    { ["Gladiador"] = "gladiator", ["Templario"] = "templar", ["Arquero"] = "ranger", ["Asesino"] = "assassin", ["Espiritualista"] = "elementalist",
      ["Hechicero"] = "sorcerer", ["Clérigo"] = "cleric", ["Cantor"] = "chanter" };
    void UpdateFooter(CaptureSnapshot s)
    {
        string region = PlayerProfiles.RegionOf(settings.RegionOffset);
        var me = s.Character is { Length: > 0 } name ? PlayerProfiles.Get(name, s.LocalServerId ?? settings.LastServerId, region).Card : null;
        selfClassName = me?.ClassName; ownCard = me;
        selfIcon.Source = me is not null && ClassKeys.TryGetValue(me.ClassName, out var key) ? Icon($"/assets/ui/class_icon_{key}.png") : null;
        selfIcon.Visibility = selfIcon.Source is null ? Visibility.Collapsed : Visibility.Visible;
        selfName.Text = string.IsNullOrEmpty(s.Character) ? "Identificando…" : s.Character + (s.IdentityInferred ? "  (deducido)" : "");
        selfName.Foreground = string.IsNullOrEmpty(s.Character) ? Muted : Teal;
        // Ping: four bars like the game's, coloured by quality, and the figure next to them.
        int lit = s.PingMs is not int ms ? 0 : ms < 60 ? 4 : ms < 100 ? 3 : ms < 160 ? 2 : 1;
        Brush color = s.PingMs is not int rtt ? Dim : rtt < 100 ? Paint("#5FE0A0") : rtt < 160 ? Gold : Alert;
        for (int i = 0; i < bars.Length; i++) bars[i].Fill = i < lit ? color : Paint("#2B3C48");
        pingFooter.Text = s.PingMs is int p ? $"{p}ms" : "—";
        pingFooter.Foreground = color;
    }
    static UIElement RoomRow(RoomMember m, bool self, string region)
    {
        var row = new DockPanel { Height = 18, Margin = new Thickness(0, 1, 0, 1) };
        // Class icon (the website's), and the gear score from the official profile when the game sent 0 (the leader in a
        // room's first roster).
        var icon = new Image { Width = 15, Height = 15, Margin = new Thickness(0, 0, 5, 0), Source = m.ClassKey is { } cls ? Icon($"/assets/ui/class_icon_{cls}.png") : null };
        DockPanel.SetDock(icon, Dock.Left); row.Children.Add(icon);
        int gearScore = m.GearScore > 0 ? m.GearScore : PlayerProfiles.Get(m.Name, m.ServerId, region).Card?.ItemLevel ?? 0;
        var state = Label(m.Leader ? "Líder" : m.Ready ? "✓ Listo" : "No listo", 10, m.Leader ? Muted : m.Ready ? Teal : Gold);
        state.Width = 52; state.TextAlignment = TextAlignment.Right;
        DockPanel.SetDock(state, Dock.Right); row.Children.Add(state);
        var right = Label($"{(gearScore > 0 ? gearScore.ToString("N0") : "—")}   {(m.CombatPower >= 1000 ? $"{m.CombatPower / 1000.0:0.0}K" : m.CombatPower.ToString("N0"))}", 10.5, self ? Brushes.White : RowText);
        DockPanel.SetDock(right, Dock.Right); row.Children.Add(right);
        var left = Label($"{(m.Leader ? "♛ " : "")}{m.Name}  {m.ServerTag}", 10.5, self ? Teal : RowText);
        if (self) left.FontWeight = FontWeights.SemiBold;
        row.Children.Add(left);
        row.Height = double.NaN; row.Margin = new Thickness(0); row.VerticalAlignment = VerticalAlignment.Center;
        return Pill(row, m.ClassKey, self);
    }
    /// <summary>The roster rows' pill: a stripe of the class colour on the left and its tint fading across, green edge for us.</summary>
    static UIElement Pill(UIElement content, string? classKey, bool self)
    {
        var c = classKey is not null && ClassColors.TryGetValue(classKey, out var k) ? k : Color.FromRgb(0x3F, 0xB8, 0xAF);
        var tint = new LinearGradientBrush(new GradientStopCollection { new(Color.FromArgb(140, c.R, c.G, c.B), 0), new(Color.FromArgb(31, c.R, c.G, c.B), .7), new(Color.FromArgb(13, c.R, c.G, c.B), 1) },
            new Point(0, .5), new Point(1, .5));
        var pill = new Grid { Height = 24, Margin = new Thickness(0, 1.5, 0, 1.5) };
        pill.Children.Add(new Border { Child = content, CornerRadius = new CornerRadius(7), Padding = new Thickness(6, 0, 6, 0), BorderThickness = new Thickness(1),
            BorderBrush = self ? Teal : Paint("#26FFFFFF"), Background = tint });
        pill.Children.Add(new Border { Width = 3, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(1, 1, 0, 1), CornerRadius = new CornerRadius(6, 0, 0, 6),
            Background = new SolidColorBrush(c), IsHitTestVisible = false });
        return pill;
    }
    const double PeWidth = 44, PcWidth = 50;
    internal static UIElement ColumnsHeader()
    {
        var row = new DockPanel { Margin = new Thickness(0, 1, 0, 2) };
        foreach (var (text, width) in new[] { ("PC", PcWidth), ("PE", PeWidth) })
        {
            var t = Label(text, 9, Dim); t.Width = width; t.TextAlignment = TextAlignment.Right;
            DockPanel.SetDock(t, Dock.Right); row.Children.Add(t);
        }
        row.Children.Add(Label("MIEMBRO", 9, Dim));
        return row;
    }
    internal static UIElement TroopRow(TroopMember m, bool self, ProfileState state)
    {
        var card = state.Card;
        string? cls = m.ClassKey ?? (card is not null && ClassKeys.TryGetValue(card.ClassName, out var k) ? k : null);
        int gear = m.GearScore > 0 ? m.GearScore : card?.ItemLevel ?? 0;
        long power = m.CombatPower > 0 ? m.CombatPower : card?.CombatPower ?? 0;
        bool loading = card is null && state.Stage is not ("missing" or "error");
        var row = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        var icon = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(4), Background = Paint("#59000000"), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center,
            Child = cls is null ? null : new Image { Source = Icon($"/assets/ui/class_icon_{cls}.png"), Margin = new Thickness(1.5) } };
        DockPanel.SetDock(icon, Dock.Left); row.Children.Add(icon);
        var pc = Label(power >= 1000 ? $"{power / 1000.0:0.0}K" : power > 0 ? power.ToString("N0") : loading ? "…" : "—", 10.5, self ? Brushes.White : RowText);
        pc.Width = PcWidth; pc.TextAlignment = TextAlignment.Right;
        DockPanel.SetDock(pc, Dock.Right); row.Children.Add(pc);
        var pe = Label(gear > 0 ? gear.ToString("N0") : loading ? "…" : "—", 10.5, self ? Brushes.White : Muted);
        pe.Width = PeWidth; pe.TextAlignment = TextAlignment.Right;
        DockPanel.SetDock(pe, Dock.Right); row.Children.Add(pe);
        // The PvP ranking of the official profile, when it has one.
        if (card?.Rankings.FirstOrDefault() is { Length: > 0 } rank)
        {
            var r = Label(rank, 9.5, Gold); r.Margin = new Thickness(6, 0, 2, 0); r.MaxWidth = 84; r.TextTrimming = TextTrimming.CharacterEllipsis;
            DockPanel.SetDock(r, Dock.Right); row.Children.Add(r);
        }
        var left = Label(m.Name, 10.5, self ? Teal : RowText);
        left.TextTrimming = TextTrimming.CharacterEllipsis; left.VerticalAlignment = VerticalAlignment.Center;
        if (self) left.FontWeight = FontWeights.SemiBold;
        row.Children.Add(left);
        return Pill(row, cls, self);
    }
    static UIElement RosterRow(string name, bool self, ProfileState state)
    {
        var row = new DockPanel { Height = 17, Margin = new Thickness(0, 1, 0, 1) };
        var card = state.Card;
        string numbers = card is null ? (state.Stage is "missing" or "error" ? "—" : "…")
            : $"{(card.CombatPower >= 1000 ? $"{card.CombatPower / 1000.0:0.0}K" : card.CombatPower.ToString("N0"))}   {(card.ItemLevel?.ToString("N0") ?? "—")}";
        var right = Label(numbers, 10.5, self ? Brushes.White : RowText);
        DockPanel.SetDock(right, Dock.Right); row.Children.Add(right);
        var left = Label(card is null ? name : $"{name}  ·  {card.ClassName}", 10.5, self ? Teal : RowText);
        if (self) left.FontWeight = FontWeights.SemiBold;
        row.Children.Add(left);
        return row;
    }
    // Class colours of the DPS rows (the panel uses the same ones).
    static readonly Dictionary<string, Color> ClassColors = new()
    {
        // Eight hues apart (2026-10-18): Templar, Sorcerer and Elementalist were three close blues, Ranger and Chanter two greens.
        ["gladiator"] = Color.FromRgb(0xE8, 0x89, 0x3A), ["templar"] = Color.FromRgb(0x4C, 0x8D, 0xF6), ["ranger"] = Color.FromRgb(0x6C, 0xC2, 0x4A),
        ["assassin"] = Color.FromRgb(0xE2, 0x55, 0x5C), ["sorcerer"] = Color.FromRgb(0xA2, 0x74, 0xF2), ["elementalist"] = Color.FromRgb(0x3F, 0xC8, 0xD8),
        ["cleric"] = Color.FromRgb(0xE9, 0xCC, 0x55), ["chanter"] = Color.FromRgb(0xE3, 0x6C, 0xB8),
    };
    /// <summary>A DPS row like TeamMate's, in our green: a rounded pill whose bar is the class colour, the class icon, the
    /// name with the player's combat power, DPS in bold and the share.</summary>
    internal static UIElement PartyRow(int rank, ActorRow a, bool self, double fill, double share, string? classKey = null, long? power = null)
    {
        var color = classKey is not null && ClassColors.TryGetValue(classKey, out var c) ? c : Color.FromRgb(0x3F, 0xB8, 0xAF);
        var grid = new Grid { Height = 26, Margin = new Thickness(0, 2, 0, 1) };
        var bar = new Border
        {
            CornerRadius = new CornerRadius(7), HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(10, BarWidth * fill),
            Background = new LinearGradientBrush(Color.FromArgb(200, color.R, color.G, color.B), Color.FromArgb(40, color.R, color.G, color.B), 0),
        };
        grid.Children.Add(new Border { Background = Paint("#B3101A21"), CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1),
            BorderBrush = self ? Teal : Paint("#33FFFFFF") });
        grid.Children.Add(bar);
        var text = new DockPanel { Margin = new Thickness(6, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        var pct = Label($"{share * 100:0.0}%", 10.5, Paint("#A8ECD6")); pct.Width = 40; pct.TextAlignment = TextAlignment.Right;
        DockPanel.SetDock(pct, Dock.Right); text.Children.Add(pct);
        var dpsText = Label($"{a.Dps:N0}/s", 11.5, Brushes.White); dpsText.FontWeight = FontWeights.Bold;
        DockPanel.SetDock(dpsText, Dock.Right); text.Children.Add(dpsText);
        var icon = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(5), Background = Paint("#59000000"), Margin = new Thickness(0, 0, 6, 0),
            Child = classKey is null ? null : new Image { Source = Icon($"/assets/ui/class_icon_{classKey}.png"), Margin = new Thickness(2) } };
        DockPanel.SetDock(icon, Dock.Left); text.Children.Add(icon);
        var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        name.Inlines.Add(new System.Windows.Documents.Run(a.Name) { FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = self ? Paint("#E9FFF7") : Brushes.White });
        if (power is long pc && pc > 0)
            name.Inlines.Add(new System.Windows.Documents.Run($"  ⚔{(pc >= 1000 ? $"{pc / 1000.0:0.0}K" : pc.ToString("N0"))}") { FontSize = 10, Foreground = Paint("#9FC9BF") });
        text.Children.Add(name);
        grid.Children.Add(text);
        return grid;
    }
}
