using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace FullAIon2Companion;

/// <summary>
/// The minimap: the website's atlas around the player, with the player in the middle, the creatures the game announced,
/// the troop members where the game last placed them, and the atlas markers (hidden cubes, ores, plants, Od, pets,
/// bosses, places, services…) filtered by layer. It takes the mouse but never the keyboard focus: drag the title to move
/// it, drag any edge or corner to resize it, the wheel zooms, hovering a marker names it.
/// </summary>
public sealed class MinimapWindow : Window
{
    const double Tile = 256, HeadHeight = 26, Edge = 7;
    static readonly Brush Teal = Paint("#71DED6"), Muted = Paint("#95A4B6"), Gold = Paint("#E9C77E"), Mob = Paint("#E0705A");
    /// <summary>The layers, in the order of the filter bar: key, label, colour.</summary>
    public static readonly (string Key, string Label, string Color)[] Layers =
    [
        ("tropa", "Tropa", "#71DED6"), ("vivos", "Criaturas", "#E0705A"), ("jefes", "Jefes", "#E9C77E"), ("cubos", "Cubos", "#B98CFF"),
        ("minerales", "Minerales", "#7FB3FF"), ("plantas", "Plantas", "#7BD88F"), ("od", "Od", "#5FE0E0"), ("rastros", "Rastros", "#F2A65A"),
        ("mascotas", "Mascotas", "#FF8FB8"), ("lugares", "Lugares", "#C9D3DD"), ("servicios", "Servicios", "#D8B769"), ("pnj", "PNJ", "#A9B4C0"),
        ("misiones", "Misiones", "#FFD86B"), ("zonas", "Zonas", "#E6EDF3"), ("criaturas", "Spawns", "#B4654F"),
    ];
    static readonly Brush[] GroupColors = [Paint("#71DED6"), Paint("#6FA8FF"), Paint("#C08CFF"), Paint("#FFB066"), Paint("#FF7FA8")];
    readonly AppSettings settings;
    readonly Canvas viewport = new() { ClipToBounds = true, Background = Paint("#0B1117") };
    readonly Canvas world = new() { Width = 4096, Height = 4096 };
    readonly Canvas markers = new() { Width = 4096, Height = 4096 };
    readonly Canvas live = new() { Width = 4096, Height = 4096 };
    readonly ScaleTransform scale = new();
    readonly TranslateTransform shift = new();
    readonly Dictionary<(string, int, int), Image> tiles = [];
    readonly TextBlock title = Label("Minimapa", 11, Brushes.White), note = Label("", 11, Muted), legend = Label("", 9.5, Muted);
    readonly Polygon me = new() { Points = new PointCollection { new(7, 0), new(14, 16), new(7, 12), new(0, 16) }, Stroke = Brushes.Black, StrokeThickness = 1, Fill = Teal, Width = 14, Height = 16, RenderTransformOrigin = new Point(.5, .55) };
    readonly RotateTransform heading = new();
    readonly TextBlock scaleText = new() { FontSize = 9.5, Foreground = Brushes.White };
    readonly Rectangle scaleBar = new() { Height = 3, Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = .5 };
    readonly System.Windows.Threading.DispatcherTimer smooth = new() { Interval = TimeSpan.FromMilliseconds(33) };
    (double X, double Y)? shown, target;
    readonly Ellipse range = new() { Width = 26, Height = 26, Stroke = Teal, StrokeThickness = 1, Opacity = .45 };
    readonly WrapPanel filterBar = new() { Margin = new Thickness(6, 0, 6, 4) };
    readonly Dictionary<string, ToggleButton> chips = [];
    string? mapId;
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    public event Action? Moved, Closed_;

    public MinimapWindow(AppSettings settings)
    {
        this.settings = settings;
        Title = "FullAIon2Companion · Minimapa";
        Width = Math.Clamp(settings.MinimapWidth, 200, 1200); Height = Math.Clamp(settings.MinimapHeight, 200, 1200);
        MinWidth = 200; MinHeight = 200;
        Left = settings.MinimapLeft ?? SystemParameters.PrimaryScreenWidth - 320; Top = settings.MinimapTop ?? 330;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowActivated = false; ShowInTaskbar = false;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        var transform = new TransformGroup { Children = { scale, shift } };
        world.RenderTransform = markers.RenderTransform = live.RenderTransform = transform;
        viewport.Children.Add(world); viewport.Children.Add(markers); viewport.Children.Add(live);
        viewport.Children.Add(range); viewport.Children.Add(me); viewport.Children.Add(note);
        me.RenderTransform = heading;
        var scaleBox = new StackPanel { Margin = new Thickness(8, 0, 0, 6) };
        scaleBox.Children.Add(scaleText); scaleBox.Children.Add(scaleBar);
        viewport.Children.Add(scaleBox);
        viewport.SizeChanged += (_, _) => Canvas.SetTop(scaleBox, ViewH - 26);
        Canvas.SetLeft(scaleBox, 0);
        // The camera glides to each new position instead of jumping (positions arrive every 0.5 s or less often).
        smooth.Tick += (_, _) => Glide();
        note.TextWrapping = TextWrapping.Wrap; note.TextAlignment = TextAlignment.Center;
        viewport.SizeChanged += (_, _) => { PlaceCenter(); lastCenter = null; markerKey = null; };
        viewport.MouseWheel += (_, e) => Zoom(e.Delta > 0 ? 1 : -1);

        var head = new DockPanel { Height = HeadHeight, Background = Brushes.Transparent, Cursor = Cursors.SizeAll };
        var close = Button("×", "Ocultar minimapa (Ctrl+Alt+N)", () => Closed_?.Invoke());
        var plus = Button("+", "Acercar", () => Zoom(1)); var minus = Button("−", "Alejar", () => Zoom(-1));
        var filters = Button("☰", "Mostrar u ocultar los filtros", () => { settings.MinimapFiltersOpen = !settings.MinimapFiltersOpen; settings.Save(); filterBar.Visibility = settings.MinimapFiltersOpen ? Visibility.Visible : Visibility.Collapsed; });
        foreach (var b in new[] { close, plus, minus, filters }) { DockPanel.SetDock(b, Dock.Right); head.Children.Add(b); }
        title.Margin = new Thickness(8, 0, 0, 0); title.FontWeight = FontWeights.SemiBold; head.Children.Add(title);
        head.MouseLeftButtonDown += (_, e) => { if (e.ButtonState != MouseButtonState.Pressed) return; DragMove(); settings.MinimapLeft = Math.Round(Left); settings.MinimapTop = Math.Round(Top); settings.Save(); Moved?.Invoke(); };

        foreach (var (key, label, color) in Layers)
        {
            var dot = new Ellipse { Width = 8, Height = 8, Fill = Paint(color), Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
            var content = new StackPanel { Orientation = Orientation.Horizontal }; content.Children.Add(dot); content.Children.Add(new TextBlock { Text = label, FontSize = 10 });
            var chip = new ToggleButton { Content = content, IsChecked = settings.MinimapFilters.Contains(key), Focusable = false, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(6, 1, 7, 1), Template = ChipTemplate() };
            chip.Click += (_, _) =>
            {
                if (chip.IsChecked == true) { if (!settings.MinimapFilters.Contains(key)) settings.MinimapFilters.Add(key); }
                else settings.MinimapFilters.Remove(key);
                settings.Save(); markerKey = null; Redraw();
            };
            chips[key] = chip; filterBar.Children.Add(chip);
        }
        filterBar.Visibility = settings.MinimapFiltersOpen ? Visibility.Visible : Visibility.Collapsed;
        legend.Margin = new Thickness(8, 0, 8, 4); legend.TextWrapping = TextWrapping.Wrap;

        var grip = new Thumb { Width = 16, Height = 16, Cursor = Cursors.SizeNWSE, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Opacity = .8, ToolTip = "Arrastra para cambiar el tamaño",
            Template = GripTemplate() };
        grip.DragDelta += (_, e) => { Width = Math.Clamp(Width + e.HorizontalChange, MinWidth, 1400); Height = Math.Clamp(Height + e.VerticalChange, MinHeight, 1400); };
        grip.DragCompleted += (_, _) => { settings.MinimapWidth = Math.Round(Width); settings.MinimapHeight = Math.Round(Height); settings.Save(); Moved?.Invoke(); };
        var layout = new DockPanel();
        DockPanel.SetDock(head, Dock.Top); layout.Children.Add(head);
        DockPanel.SetDock(filterBar, Dock.Top); layout.Children.Add(filterBar);
        DockPanel.SetDock(legend, Dock.Bottom); layout.Children.Add(legend);
        layout.Children.Add(viewport);
        var frame = new Grid(); frame.Children.Add(layout); frame.Children.Add(grip);
        Content = new Border { Background = new SolidColorBrush(Color.FromArgb((byte)(Math.Clamp(settings.MinimapOpacity, .3, 1) * 255), 0x0D, 0x14, 0x1B)), BorderBrush = Paint("#C833575B"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = new Border { CornerRadius = new CornerRadius(3), ClipToBounds = true, Child = frame } };
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x08000000 | 0x80);
            HwndSource.FromHwnd(hwnd).AddHook(Hook);
        };
    }

    // Resizing like a normal window: the edges and corners answer WM_NCHITTEST as borders; the size is kept on release.
    IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_NCHITTEST = 0x84, WM_EXITSIZEMOVE = 0x232;
        if (msg == WM_NCHITTEST)
        {
            var p = PointFromScreen(new Point((short)(lParam.ToInt64() & 0xFFFF), (short)((lParam.ToInt64() >> 16) & 0xFFFF)));
            bool l = p.X < Edge, r = p.X > ActualWidth - Edge, t = p.Y < Edge, b = p.Y > ActualHeight - Edge;
            int hit = t && l ? 13 : t && r ? 14 : b && l ? 16 : b && r ? 17 : l ? 10 : r ? 11 : t ? 12 : b ? 15 : 0;
            if (hit != 0) { handled = true; return hit; }
        }
        else if (msg == WM_EXITSIZEMOVE)
        {
            settings.MinimapWidth = Math.Round(ActualWidth); settings.MinimapHeight = Math.Round(ActualHeight);
            settings.MinimapLeft = Math.Round(Left); settings.MinimapTop = Math.Round(Top); settings.Save(); Moved?.Invoke();
        }
        return IntPtr.Zero;
    }
    static ControlTemplate GripTemplate()
    {
        var path = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
        path.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M 15,3 L 3,15 M 15,8 L 8,15 M 15,12 L 12,15"));
        path.SetValue(System.Windows.Shapes.Path.StrokeProperty, Paint("#C3CFD9"));
        path.SetValue(System.Windows.Shapes.Path.StrokeThicknessProperty, 1.4);
        var box = new FrameworkElementFactory(typeof(Grid));
        box.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
        box.AppendChild(path);
        return new ControlTemplate(typeof(Thumb)) { VisualTree = box };
    }
    static ControlTemplate ChipTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty));
        border.SetValue(Border.BackgroundProperty, Paint("#14202A"));
        border.SetValue(Border.BorderBrushProperty, Paint("#2B3C48"));
        border.Name = "chip";
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(ToggleButton)) { VisualTree = border };
        var on = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        on.Setters.Add(new Setter(Border.BackgroundProperty, Paint("#1F3A3F"), "chip"));
        on.Setters.Add(new Setter(Border.BorderBrushProperty, Paint("#3F9D84"), "chip"));
        on.Setters.Add(new Setter(TextElement.ForegroundProperty, Brushes.White));
        var off = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = false };
        off.Setters.Add(new Setter(TextElement.ForegroundProperty, Muted));
        off.Setters.Add(new Setter(OpacityProperty, .7));
        template.Triggers.Add(on); template.Triggers.Add(off);
        return template;
    }
    static Brush Paint(string color) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); b.Freeze(); return b; }
    static TextBlock Label(string text, double size, Brush color) => new() { Text = text, FontSize = size, Foreground = color, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    static Button Button(string text, string tip, Action click)
    {
        var b = new Button { Content = text, Width = 24, Height = 22, Padding = new Thickness(0), Margin = new Thickness(0, 1, 2, 1), ToolTip = tip, Focusable = false, Foreground = Paint("#C3CFD9"), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand, FontSize = 14 };
        b.Click += (_, _) => click();
        return b;
    }
    // Zoom steps: 1 atlas pixel (2 m) per screen pixel and its halves and doubles.
    const double MinZoom = .25, MaxZoom = 8;
    void Zoom(int step)
    {
        settings.MinimapZoom = Math.Round(Math.Clamp(CurrentZoom * Math.Pow(1.25, step), MinZoom, MaxZoom), 3); settings.Save();
        lastCenter = null; markerKey = null; Redraw();
    }
    double CurrentZoom => double.IsFinite(settings.MinimapZoom) && settings.MinimapZoom > 0 ? Math.Clamp(settings.MinimapZoom, MinZoom, MaxZoom) : 1;
    double ViewW => Math.Max(50, viewport.ActualWidth);
    double ViewH => Math.Max(50, viewport.ActualHeight);
    void PlaceCenter()
    {
        Canvas.SetLeft(range, ViewW / 2 - 13); Canvas.SetTop(range, ViewH / 2 - 13);
        Canvas.SetLeft(me, ViewW / 2 - 7); Canvas.SetTop(me, ViewH / 2 - 9);
        Canvas.SetLeft(note, 10); Canvas.SetTop(note, ViewH / 2 - 30); note.Width = ViewW - 20;
    }

    (double X, double Y)? lastCenter;
    double lastZoom;
    void ApplyCamera()
    {
        if (shown is not { } c) return;
        double z = CurrentZoom;
        shift.X = ViewW / 2 - c.X * z; shift.Y = ViewH / 2 - c.Y * z;
    }
    void Glide()
    {
        if (shown is not { } c || target is not { } t) return;
        double dx = t.X - c.X, dy = t.Y - c.Y;
        if (Math.Abs(dx) + Math.Abs(dy) < .05) { smooth.Stop(); return; }
        shown = (c.X + dx * .22, c.Y + dy * .22);
        ApplyCamera();
    }
    // A scale bar of a round length (10, 25, 50, 100 … m) about 60 px long. World units are centimetres: one atlas
    // pixel is 1 / (ax * 100) m - 2 m on the open-world maps, 0.5 m on the smaller Abyss ones.
    void UpdateScale(double z, MinimapMap map)
    {
        double metresPerPixel = 1 / (Math.Max(1e-6, map.Ax) * 100) / z;
        double[] steps = [5, 10, 25, 50, 100, 250, 500, 1000];
        double metres = steps.FirstOrDefault(m => m / metresPerPixel >= 50, 1000);
        scaleBar.Width = metres / metresPerPixel; scaleText.Text = metres >= 1000 ? $"{metres / 1000:0} km" : $"{metres:0} m";
    }
    MinimapMap? lastMap;
    int lastLoads = -1;
    MapPosition? lastPos;
    CaptureSnapshot? lastSnapshot;
    void Redraw() { if (lastSnapshot is { } s) Render(s); }
    /// <summary>Applies the opacity, marker size and filter changes made in Configuración.</summary>
    public void ApplyLook()
    {
        if (Content is Border b) b.Background = new SolidColorBrush(Color.FromArgb((byte)(Math.Clamp(settings.MinimapOpacity, .3, 1) * 255), 0x0D, 0x14, 0x1B));
        foreach (var (key, chip) in chips) chip.IsChecked = settings.MinimapFilters.Contains(key);
        filterBar.Visibility = settings.MinimapFiltersOpen ? Visibility.Visible : Visibility.Collapsed;
        markerKey = null; Redraw();
    }
    /// <summary>Back to the default size, next to the right edge of the screen.</summary>
    public void ResetPlace()
    {
        settings.MinimapWidth = 280; settings.MinimapHeight = 330; settings.MinimapLeft = null; settings.MinimapTop = null; settings.Save();
        Width = 280; Height = 330; Left = SystemParameters.PrimaryScreenWidth - 320; Top = 330;
    }
    public void Render(CaptureSnapshot s)
    {
        lastSnapshot = s;
        // A map load (teleport, dungeon, Abyss) makes the last map meaningless: start over.
        if (s.MapLoads != lastLoads) { lastLoads = s.MapLoads; lastMap = null; lastPos = null; shown = target = null; }
        var vote = MinimapData.Vote((s.Mobs ?? []).Select(m => m.Npc));
        string? known = MinimapData.MapOfWorld(s.World);
        // The creatures around clearly on another map: the world id is stale (a map load missed), trust them, learn nothing.
        bool disagree = known is not null && vote.Region is not null && vote.Region != known && vote.Votes >= 4 && vote.Share >= .8;
        if (disagree) known = null; else MinimapData.Learn(s.World, vote);
        // The world the game named wins over the vote once learned; an unknown world needs the vote.
        var map = known is not null ? MinimapData.Map(known) : vote.Map;
        string? region = known ?? vote.Region;
        // Standing still with nothing around, the game reports neither: keep the last place we knew (same map load).
        bool stale = false;
        if (map is not null && s.Position is { } now) { lastMap = map; lastPos = now; }
        else if (map is null && region is null && lastMap is not null && lastPos is not null) { map = lastMap; s = s with { Position = lastPos, Mobs = [] }; stale = true; }
        if (map is null || s.Position is not { } pos)
        {
            title.Text = region == "abyss" ? "Abismo" : s.World is > 100_000 ? "Mazmorra" : "Minimapa"; legend.Text = "";
            note.Text = region == "abyss" ? "Abismo: el atlas de esta zona todavía no está calibrado para la posición"
                : s.World is > 100_000 ? "Mazmorra o instancia: no hay atlas de esta zona"
                : s.Position is null ? "Esperando tu posición: muévete o usa una habilidad"
                : "Reconociendo el mapa por las criaturas de alrededor…";
            note.Visibility = Visibility.Visible; world.Visibility = markers.Visibility = live.Visibility = Visibility.Hidden; me.Visibility = range.Visibility = Visibility.Hidden;
            return;
        }
        note.Visibility = Visibility.Collapsed; world.Visibility = markers.Visibility = live.Visibility = Visibility.Visible; me.Visibility = range.Visibility = Visibility.Visible;
        if (map.Id != mapId) { mapId = map.Id; world.Children.Clear(); tiles.Clear(); lastCenter = null; markerKey = null; }
        title.Text = map.Name + (stale ? "  ·  última posición" : pos.Exact ? "" : "  ·  posición aproximada");
        me.Fill = pos.Exact ? Teal : Paint("#5571DED6"); me.Stroke = pos.Exact ? Brushes.Black : Teal;
        range.Visibility = pos.Exact ? Visibility.Collapsed : Visibility.Visible; // the ring says "around here"
        PlaceCenter();
        var (cx, cy) = MinimapData.Project(map, pos.X, pos.Y);
        if (cx is < -200 or > 4296 || cy is < -200 or > 4296)
        {
            // The position does not fall on this atlas: wrong map, or a part of the world the atlas does not cover.
            title.Text = map.Name; note.Text = "Tu posición queda fuera del atlas de este mapa"; note.Visibility = Visibility.Visible;
            world.Visibility = markers.Visibility = live.Visibility = Visibility.Hidden; me.Visibility = range.Visibility = Visibility.Hidden;
            return;
        }
        // Heading: the direction of the last move longer than ~4 m.
        if (target is { } before && Math.Abs(cx - before.X) + Math.Abs(cy - before.Y) > 2)
            heading.Angle = Math.Atan2(cx - before.X, before.Y - cy) * 180 / Math.PI;
        if (target is null || Math.Abs(cx - target.Value.X) + Math.Abs(cy - target.Value.Y) > 2) target = (cx, cy);
        if (shown is null || Math.Abs(shown.Value.X - cx) + Math.Abs(shown.Value.Y - cy) > 300) shown = target;
        double z = CurrentZoom;
        scale.ScaleX = scale.ScaleY = z;
        ApplyCamera();
        if (!smooth.IsEnabled) smooth.Start();
        UpdateScale(z, map);
        if (lastCenter is not { } last || Math.Abs(last.X - cx) > 64 || Math.Abs(last.Y - cy) > 64 || lastZoom != z) { lastCenter = (cx, cy); lastZoom = z; LoadTiles(map.Id, cx, cy, z); }
        DrawMarkers(map.Id, cx, cy, z);
        DrawLive(s, map, z);
        DrawCubeGuide(s, map, z);
    }

    // ---- Atlas markers (static): only those in view, redrawn when the view moves a little or a filter changes.
    sealed record Marker(string Filter, string Category, string? Icon, double X, double Y, string Name);
    readonly Dictionary<string, List<Marker>> markerCache = [];
    readonly Dictionary<string, BitmapImage?> icons = [];
    string? markerKey;
    List<Marker> MarkersOf(string map)
    {
        if (markerCache.TryGetValue(map, out var list)) return list;
        list = [];
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "data", "minimap-markers", map + ".json")));
            foreach (var c in doc.RootElement.GetProperty("categories").EnumerateArray())
            {
                string filter = c.GetProperty("filter").GetString()!, name = c.GetProperty("name").GetString()!, icon = c.GetProperty("icon").GetString() ?? "";
                foreach (var m in c.GetProperty("markers").EnumerateArray())
                    list.Add(new(filter, name, icon.Length > 0 ? icon : null, m[0].GetDouble(), m[1].GetDouble(), m[2].GetString() ?? name));
            }
        }
        catch { }
        return markerCache[map] = list;
    }
    BitmapImage? IconOf(string file)
    {
        if (icons.TryGetValue(file, out var cached)) return cached;
        BitmapImage? b = null;
        try
        {
            string path = System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "map-icons", file);
            if (File.Exists(path)) { b = new BitmapImage(); b.BeginInit(); b.UriSource = new Uri(path); b.DecodePixelWidth = 32; b.CacheOption = BitmapCacheOption.OnLoad; b.EndInit(); b.Freeze(); }
        }
        catch { b = null; }
        return icons[file] = b;
    }
    void DrawMarkers(string map, double cx, double cy, double z)
    {
        double halfW = ViewW / 2 / z, halfH = ViewH / 2 / z;
        // Redraw only after moving a fifth of the view (or on zoom, size or filter changes): hundreds of elements.
        string key = $"{map}|{z}|{ViewW:0}|{ViewH:0}|{string.Join(",", settings.MinimapFilters)}|{Math.Round(cx / (halfW / 2.5))}|{Math.Round(cy / (halfH / 2.5))}";
        if (key == markerKey) return;
        markerKey = key;
        markers.Children.Clear();
        var on = settings.MinimapFilters.ToHashSet();
        double margin = Math.Max(halfW, halfH) * .6;
        var visible = MarkersOf(map).Where(m => on.Contains(m.Filter) && Math.Abs(m.X - cx) < halfW + margin && Math.Abs(m.Y - cy) < halfH + margin)
            .OrderBy(m => (m.X - cx) * (m.X - cx) + (m.Y - cy) * (m.Y - cy)).Take(700).ToList();
        var counts = visible.Where(m => Math.Abs(m.X - cx) < halfW && Math.Abs(m.Y - cy) < halfH).GroupBy(m => m.Filter).ToDictionary(g => g.Key, g => g.Count());
        bool useIcons = z >= .5;
        foreach (var m in visible)
        {
            var color = Paint(Layers.FirstOrDefault(l => l.Key == m.Filter).Color ?? "#C9D3DD");
            FrameworkElement element;
            if (m.Filter == "zonas")
            {
                element = new TextBlock { Text = m.Name, FontSize = 11 / z, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, Opacity = .85,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 0, Opacity = 1, Color = Colors.Black } };
                element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(element, m.X - element.DesiredSize.Width / 2); Canvas.SetTop(element, m.Y - element.DesiredSize.Height / 2);
                element.IsHitTestVisible = false;
                markers.Children.Add(element);
                continue;
            }
            double size = (useIcons && m.Icon is not null ? 18 : 7) * Math.Clamp(settings.MinimapIconScale, .6, 2) / z;
            if (useIcons && m.Icon is { } file && IconOf(file) is { } bitmap) element = new Image { Source = bitmap, Width = size, Height = size };
            else { size = 7 * Math.Clamp(settings.MinimapIconScale, .6, 2) / z; element = new Ellipse { Width = size, Height = size, Fill = color, Stroke = Brushes.Black, StrokeThickness = .8 / z }; }
            element.ToolTip = m.Name == m.Category ? m.Name : $"{m.Name} · {m.Category}";
            ToolTipService.SetInitialShowDelay(element, 80);
            Canvas.SetLeft(element, m.X - size / 2); Canvas.SetTop(element, m.Y - size / 2);
            markers.Children.Add(element);
        }
        legend.Text = counts.Count == 0 ? "" : "A la vista: " + string.Join(" · ", Layers.Where(l => counts.ContainsKey(l.Key)).Select(l => $"{l.Label} {counts[l.Key]}"));
    }

    // ---- Live layer: the creatures the game announced and the troop members' last places.
    readonly Dictionary<string, BitmapImage?> classIcons = [];
    BitmapImage? ClassIcon(string key)
    {
        if (classIcons.TryGetValue(key, out var b)) return b;
        try
        {
            string path = System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "ui", $"class_icon_{key}.png");
            if (File.Exists(path)) { b = new BitmapImage(); b.BeginInit(); b.UriSource = new Uri(path); b.DecodePixelWidth = 32; b.CacheOption = BitmapCacheOption.OnLoad; b.EndInit(); b.Freeze(); }
        }
        catch { b = null; }
        return classIcons[key] = b;
    }
    /// <summary>Environment objects present now (0x3436): drawn over the atlas markers, which are only possible places.</summary>
    public Func<IReadOnlyList<EnvObject>>? EnvObjects { get; set; }
    /// <summary>The player's own pins and routes of a map, from the Mapa de Atreia (AtlasMarks): same atlas pixels.</summary>
    public Func<string, AtlasMarks.Map?>? Marks { get; set; }
    /// <summary>The hidden cube to go to (a live one, else the nearest possible spot), for the guide arrow.</summary>
    public Func<CubeView>? Cubes { get; set; }
    // Guide to the cube: a dashed line from the player, an arrow head next to him and the distance. Purple and bright for a
    // live cube; softer for a possible spot (within 300 m, with the "Cubos" layer on).
    void DrawCubeGuide(CaptureSnapshot s, MinimapMap map, double z)
    {
        if (!settings.MinimapFilters.Contains("cubos") || Cubes?.Invoke() is not { } v || s.Position is not { } pos) return;
        bool isLive = v.Active is not null;
        (float X, float Y)? goal = isLive ? (v.Active!.X, v.Active.Y) : v.NearestX is float nx && v.NearestY is float ny && v.NearestSpot < 300 ? (nx, ny) : null;
        if (goal is not { } g) return;
        var (cx, cy) = MinimapData.Project(map, pos.X, pos.Y);
        var (tx, ty) = MinimapData.Project(map, g.X, g.Y);
        double dx = tx - cx, dy = ty - cy, len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-6) return;
        double ux = dx / len, uy = dy / len;
        var color = isLive ? Paint("#C9A6FF") : Paint("#B98CFF");
        var line = new Line { X1 = cx, Y1 = cy, X2 = tx, Y2 = ty, Stroke = color, StrokeThickness = (isLive ? 2.5 : 1.6) / z, StrokeDashArray = [4, 3], Opacity = isLive ? 1 : .75, IsHitTestVisible = false };
        live.Children.Add(line);
        // Arrow head 22 px (on screen) from the player, pointing at the goal.
        double r = 22 / z, head = 9 / z;
        double ax = cx + ux * r, ay = cy + uy * r;
        var arrow = new Polygon { Fill = color, Stroke = Brushes.Black, StrokeThickness = .8 / z, IsHitTestVisible = false,
            Points = [new(ax + ux * head, ay + uy * head), new(ax - uy * head * .6, ay + ux * head * .6), new(ax + uy * head * .6, ay - ux * head * .6)] };
        live.Children.Add(arrow);
        double metres = isLive ? v.Distance ?? 0 : v.NearestSpot ?? 0;
        var label = new Border { Background = Paint("#E61C1430"), BorderBrush = color, BorderThickness = new Thickness(1 / z), CornerRadius = new CornerRadius(4 / z), Padding = new Thickness(4 / z, 1 / z, 4 / z, 1 / z), IsHitTestVisible = false,
            Child = new TextBlock { Text = (isLive ? "◆ Cubo " : "◇ ") + $"{metres:0} m", FontSize = 10 / z, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White } };
        Canvas.SetLeft(label, cx + ux * (r + 14 / z) - 4 / z); Canvas.SetTop(label, cy + uy * (r + 14 / z) - 8 / z);
        live.Children.Add(label);
    }
    // The player's own routes (teal, dashed, the start gold) and pins (gold, with their names), under the live markers.
    void DrawMarks(MinimapMap map, double z)
    {
        if (Marks?.Invoke(map.Id) is not { } own) return;
        foreach (var r in own.Routes)
        {
            if (r.Points.Length < 2) continue;
            var line = new Polyline { Stroke = Teal, StrokeThickness = 3 / z, StrokeDashArray = [3, 2], StrokeLineJoin = PenLineJoin.Round, Opacity = .95, IsHitTestVisible = false };
            foreach (var p in r.Points) if (p.Length >= 2) line.Points.Add(new Point(p[0], p[1]));
            live.Children.Add(line);
            double dot = 7 / z;
            var start = new Ellipse { Width = dot, Height = dot, Fill = Gold, Stroke = Brushes.Black, StrokeThickness = 1 / z, ToolTip = r.Name };
            Canvas.SetLeft(start, r.Points[0][0] - dot / 2); Canvas.SetTop(start, r.Points[0][1] - dot / 2);
            live.Children.Add(start);
        }
        foreach (var p in own.Pins)
        {
            double size = 12 / z;
            var pin = new Ellipse { Width = size, Height = size, Fill = Gold, Stroke = Brushes.Black, StrokeThickness = 1.5 / z, ToolTip = p.Name };
            Canvas.SetLeft(pin, p.X - size / 2); Canvas.SetTop(pin, p.Y - size / 2);
            live.Children.Add(pin);
            var name = new TextBlock { Text = p.Name, FontSize = 10 / z, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, IsHitTestVisible = false,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 0, Opacity = 1, Color = Colors.Black } };
            Canvas.SetLeft(name, p.X + size / 2 + 2 / z); Canvas.SetTop(name, p.Y - 7 / z);
            live.Children.Add(name);
        }
    }
    static readonly Dictionary<string, (string Filter, string Color)> EnvLook = new()
    {
        ["Cube"] = ("cubos", "#B98CFF"), ["Od"] = ("od", "#6FC3FF"), ["Ore"] = ("minerales", "#D9B27A"), ["Gemstone"] = ("minerales", "#FF7BAC"),
        ["Herb"] = ("plantas", "#7FDB6A"), ["Food"] = ("plantas", "#C6E36A"), ["Wood"] = ("plantas", "#B98A5A"), ["Cotton"] = ("plantas", "#F2F2F2"), ["Fragment"] = ("rastros", "#F2C94C"),
    };
    void DrawLive(CaptureSnapshot s, MinimapMap map, double z)
    {
        live.Children.Clear();
        DrawMarks(map, z);
        var on = settings.MinimapFilters;
        foreach (var o in EnvObjects?.Invoke() ?? [])
        {
            if (!EnvLook.TryGetValue(o.Kind, out var look) || !on.Contains(look.Filter)) continue;
            var (ox, oy) = MinimapData.Project(map, o.X, o.Y);
            if (ox is < 0 or > 4096 || oy is < 0 or > 4096) continue;
            bool cube = o.Kind == "Cube";
            double size = (cube ? 16 : 9) / z;
            var mark = new Rectangle { Width = size, Height = size, RadiusX = 2 / z, RadiusY = 2 / z, Fill = Paint(look.Color), Stroke = cube ? Brushes.White : Brushes.Black, StrokeThickness = (cube ? 2 : 1) / z,
                RenderTransform = new RotateTransform(45, size / 2, size / 2), ToolTip = o.Name + " · ahora" };
            if (cube) mark.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(0xB9, 0x8C, 0xFF), BlurRadius = 12, ShadowDepth = 0, Opacity = 1 };
            Canvas.SetLeft(mark, ox - size / 2); Canvas.SetTop(mark, oy - size / 2);
            live.Children.Add(mark);
        }
        foreach (var m in s.Mobs ?? [])
        {
            if (!(m.Elite ? on.Contains("jefes") || on.Contains("vivos") : on.Contains("vivos"))) continue;
            var (mx, my) = MinimapData.Project(map, m.X, m.Y);
            double size = (m.Elite ? 10 : 5) / z;
            var dot = new Ellipse { Width = size, Height = size, Fill = m.Elite ? Gold : Mob, Opacity = m.Elite ? 1 : .8, Stroke = Brushes.Black, StrokeThickness = 1 / z, ToolTip = Catalog.NpcName(m.Npc) + (m.Elite ? " · élite" : "") };
            Canvas.SetLeft(dot, mx - size / 2); Canvas.SetTop(dot, my - size / 2);
            live.Children.Add(dot);
        }
        if (on.Contains("tropa") && s.Troop is { } troop)
        {
            foreach (var t in troop.Members)
            {
                if (t.X is not float x || t.Y is not float y || t.SeenAt is not DateTime at || DateTime.Now - at > TimeSpan.FromSeconds(60)) continue;
                if (t.Name.Equals(s.Character, StringComparison.OrdinalIgnoreCase)) continue;
                var (px, py) = MinimapData.Project(map, x, y);
                if (px is < 0 or > 4096 || py is < 0 or > 4096) continue; // another map
                bool own = t.Group == troop.OwnGroup;
                var brush = own ? GroupColors[0] : GroupColors[Math.Clamp(t.Group, 1, GroupColors.Length - 1)];
                double size = 17 / z;
                var badge = new Grid { Width = size, Height = size, Opacity = DateTime.Now - at > TimeSpan.FromSeconds(15) ? .55 : 1,
                    ToolTip = $"{t.Name} · grupo {t.Group}" + (DateTime.Now - at > TimeSpan.FromSeconds(10) ? $" · hace {(DateTime.Now - at).TotalSeconds:0} s" : "") };
                badge.Children.Add(new Ellipse { Fill = Paint("#E60D141B"), Stroke = brush, StrokeThickness = 2 / z });
                if (t.ClassKey is { } cls && ClassIcon(cls) is { } icon) badge.Children.Add(new Image { Source = icon, Margin = new Thickness(2.5 / z) });
                else badge.Children.Add(new TextBlock { Text = t.Name[..1].ToUpperInvariant(), FontSize = 9 / z, FontWeight = FontWeights.Bold, Foreground = brush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                Canvas.SetLeft(badge, px - size / 2); Canvas.SetTop(badge, py - size / 2);
                live.Children.Add(badge);
            }
        }
    }
    // Tiles around the centre, loaded once per map and kept: a session rarely sees more than a few dozen of the 256.
    void LoadTiles(string map, double cx, double cy, double z)
    {
        double halfX = ViewW / 2 / z + Tile, halfY = ViewH / 2 / z + Tile;
        for (int tx = Math.Max(0, (int)((cx - halfX) / Tile)); tx <= Math.Min(15, (int)((cx + halfX) / Tile)); tx++)
            for (int ty = Math.Max(0, (int)((cy - halfY) / Tile)); ty <= Math.Min(15, (int)((cy + halfY) / Tile)); ty++)
            {
                if (tiles.ContainsKey((map, tx, ty))) continue;
                string file = System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "atlas", map, $"4_{tx}_{ty}.webp");
                var image = new Image { Width = Tile + .5, Height = Tile + .5 };
                if (File.Exists(file))
                {
                    try { var b = new BitmapImage(); b.BeginInit(); b.UriSource = new Uri(file); b.CacheOption = BitmapCacheOption.OnLoad; b.EndInit(); b.Freeze(); image.Source = b; } catch { }
                }
                Canvas.SetLeft(image, tx * Tile); Canvas.SetTop(image, ty * Tile);
                world.Children.Add(image); tiles[(map, tx, ty)] = image;
            }
    }
}
