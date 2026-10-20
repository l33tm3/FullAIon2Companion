using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace FullAIon2Companion;

/// <summary>
/// The combat assistant: a small bar over the game with the player's buffs (time left) and the skills cooling down,
/// a gold flash (and optionally the alert sound) when a long cooldown is ready. Click-through; it moves with the handle at
/// its left, the overlay's move mode (Ctrl+Alt+M) or Configuración → Combate → Mover.
/// </summary>
public sealed class AssistantWindow : Window
{
    readonly AppSettings settings;
    readonly WrapPanel buffRow = new() { HorizontalAlignment = HorizontalAlignment.Center };
    readonly WrapPanel cooldownRow = new() { HorizontalAlignment = HorizontalAlignment.Center };
    readonly TextBlock hint = new() { Text = "ASISTENTE DE COMBATE · arrastra para moverlo", FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(0x5F, 0xD3, 0xB4)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 2) };
    readonly Border frame;
    readonly HashSet<int> announced = [];
    bool clickThrough = true, overlayLocked = true;
    /// <summary>Moving the bar on its own (Configuración → Combate → Mover), apart from the overlay's move mode.</summary>
    public bool Moving { get; private set; }
    public event Action? Shown;
    /// <summary>A small handle at the left of the bar (like the overlay's dock grip): the bar itself stays click-through,
    /// this handle can be dragged at any time to move it.</summary>
    public Window Grip { get; }
    bool gripDragging;
    /// <summary>Plays the alert sound (set by the main window).</summary>
    public Action? Beep { get; set; }
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out Rect32 rect);
    [DllImport("user32.dll")] static extern bool GetCursorInfo(ref CursorInfo info);
    struct Rect32 { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct CursorInfo { public int Size, Flags; public IntPtr Cursor; public int X, Y; }
    /// <summary>The card of the icon under the mouse (buff or cooldown): what it does at the player's level and the time left.</summary>
    public SkillCardWindow Card { get; } = new();
    readonly DispatcherTimer hover = new() { Interval = TimeSpan.FromMilliseconds(80) };
    /// <summary>The level of a class skill from the player's official profile (set by the overlay), or null.</summary>
    public Func<int, int?>? LevelOf { get; set; }
    /// <summary>Puts the overlay's style on a window (set by the overlay): the card is built after the style was applied.</summary>
    public Action<Window>? Theme { get; set; }
    static readonly Brush Green = Freeze(new SolidColorBrush(Color.FromRgb(0x5F, 0xD3, 0xB4)));
    static readonly Brush Gold = Freeze(new SolidColorBrush(Color.FromRgb(0xF2, 0xC9, 0x4C)));
    static readonly Brush Edge = Freeze(new SolidColorBrush(Color.FromArgb(220, 0x2E, 0x4A, 0x4F)));
    static readonly Brush Shade = Freeze(new SolidColorBrush(Color.FromArgb(170, 5, 9, 12)));
    static Brush Freeze(Brush b) { b.Freeze(); return b; }

    public AssistantWindow(AppSettings settings)
    {
        this.settings = settings;
        Title = "FullAIon2Companion · Asistente"; SizeToContent = SizeToContent.WidthAndHeight;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowActivated = false; ShowInTaskbar = false;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        var stack = new StackPanel();
        stack.Children.Add(hint); stack.Children.Add(buffRow); stack.Children.Add(cooldownRow);
        hint.Visibility = Visibility.Collapsed;
        frame = new Border { Padding = new Thickness(4), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(0), Child = stack, MinWidth = 120 };
        Content = frame;
        MouseLeftButtonDown += (_, e) =>
        {
            if (clickThrough || e.ButtonState != MouseButtonState.Pressed) return;
            DragMove();
            settings.AssistantLeft = Math.Round(Left); settings.AssistantTop = Math.Round(Top); settings.Save();
        };
        SourceInitialized += (_, _) => ApplyStyle();
        Grip = MakeGrip();
        IsVisibleChanged += (_, _) => { if (IsVisible) { Grip.Show(); PlaceGrip(); } else Grip.Hide(); };
        LocationChanged += (_, _) => PlaceGrip();
        SizeChanged += (_, _) => PlaceGrip();
        Closed += (_, _) => { hover.Stop(); Grip.Close(); Card.Close(); };
        // The bar never takes the mouse (the game keeps every click): the cursor is followed instead, and the card shows
        // while it rests on an icon - only with the cursor showing, so not while the game turns the camera with it hidden.
        // It runs every 80 ms over the game: a failure is logged and the card hidden, never a dialog on top of the game.
        hover.Tick += (_, _) =>
        {
            try { Hover(); }
            catch (Exception e)
            {
                try { System.IO.File.AppendAllText(System.IO.Path.Combine(Program.DataDirectory, "errors.log"), e + "\n"); } catch { }
                Card.Clear();
            }
        };
        IsVisibleChanged += (_, _) => { if (IsVisible) hover.Start(); else { hover.Stop(); Card.Clear(); } };
        ApplyScale();
    }
    Window MakeGrip()
    {
        var face = new Border
        {
            Width = 14, Height = 30, CornerRadius = new CornerRadius(5), Cursor = Cursors.SizeAll, BorderThickness = new Thickness(1), BorderBrush = Edge,
            Background = new SolidColorBrush(Color.FromArgb(170, 13, 20, 27)), ToolTip = "Arrastra para mover el asistente de combate",
            Child = new TextBlock { Text = "⠿", FontSize = 13, Foreground = Green, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var grip = new Window
        {
            Title = "FullAIon2Companion · Asistente (mover)", SizeToContent = SizeToContent.WidthAndHeight, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true, Background = Brushes.Transparent, Topmost = true, ShowActivated = false, ShowInTaskbar = false, Content = face, Opacity = .8,
        };
        grip.SourceInitialized += (_, _) => { var h = new WindowInteropHelper(grip).Handle; SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x08000000 | 0x80); };
        grip.SizeChanged += (_, _) => PlaceGrip();
        grip.MouseEnter += (_, _) => grip.Opacity = 1;
        grip.MouseLeave += (_, _) => grip.Opacity = .8;
        // The bar follows the handle while it is dragged.
        grip.LocationChanged += (_, _) => { if (!gripDragging) return; Left = grip.Left + grip.ActualWidth + 2; Top = grip.Top - (ActualHeight - grip.ActualHeight) / 2; };
        face.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState != MouseButtonState.Pressed) return;
            gripDragging = true;
            try { grip.DragMove(); } finally { gripDragging = false; }
            settings.AssistantLeft = Math.Round(Left); settings.AssistantTop = Math.Round(Top); settings.Save();
        };
        return grip;
    }
    void PlaceGrip()
    {
        if (gripDragging || !Grip.IsVisible) return;
        Grip.Left = Left - Grip.ActualWidth - 2;
        Grip.Top = Top + (ActualHeight - Grip.ActualHeight) / 2;
    }
    public void ApplyScale() { double k = Math.Clamp(settings.AssistantScale, .6, 2); frame.LayoutTransform = new ScaleTransform(k, k); ((FrameworkElement)Grip.Content).LayoutTransform = new ScaleTransform(k, k); }
    void ApplyStyle()
    {
        var hwnd = new WindowInteropHelper(this).Handle; if (hwnd == IntPtr.Zero) return;
        int style = GetWindowLong(hwnd, -20) | 0x08000000 | 0x80;
        SetWindowLong(hwnd, -20, clickThrough ? style | 0x20 : style & ~0x20);
    }
    /// <summary>Move mode of the overlay: the bar shows a frame and can be dragged.</summary>
    public void SetClickThrough(bool value) { overlayLocked = value; ApplyMode(); }
    public void SetMoving(bool value) { Moving = value; ApplyMode(); if (value && !IsVisible && settings.AssistantVisible) { Show(); Place(); Shown?.Invoke(); } }
    /// <summary>Back to the default place: centred low over the game, above the skill bar.</summary>
    public void ResetPlace() { settings.AssistantLeft = null; settings.AssistantTop = null; settings.Save(); if (IsVisible) Place(); }
    void ApplyMode()
    {
        bool value = overlayLocked && !Moving;
        clickThrough = value; ApplyStyle();
        hint.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
        frame.Background = value ? Brushes.Transparent : new SolidColorBrush(Color.FromArgb(150, 13, 20, 27));
        frame.BorderThickness = new Thickness(value ? 0 : 2); frame.BorderBrush = Green;
    }

    public void Update(IReadOnlyList<TimerTile> buffs, IReadOnlyList<TimerTile> cooldowns, bool enabled)
    {
        var shownBuffs = settings.AssistantShowBuffs ? buffs : [];
        var shownCooldowns = settings.AssistantShowCooldowns ? cooldowns : [];
        bool any = shownBuffs.Count > 0 || shownCooldowns.Count > 0;
        if (Moving && settings.AssistantVisible) enabled = true; // while moving it stays up, even with the panel in front
        if (!enabled || (!any && clickThrough)) { if (IsVisible) Hide(); return; }
        Fill(buffRow, shownBuffs, 28);
        Fill(cooldownRow, shownCooldowns, 36);
        foreach (var c in cooldowns)
        {
            if (c.Left > 0) { announced.Remove(c.Skill); continue; }
            if (c.Total >= 30 && settings.AssistantSound && announced.Add(c.Skill)) Beep?.Invoke();
        }
        if (!IsVisible) { Show(); Place(); Shown?.Invoke(); }
    }
    /// <summary>For the test: the icons on the bar, buffs first.</summary>
    internal IReadOnlyList<FrameworkElement> TilesForTest => [.. buffRow.Children.OfType<FrameworkElement>(), .. cooldownRow.Children.OfType<FrameworkElement>()];
    /// <summary>The icon under a point of the screen (physical pixels), and what it shows.</summary>
    internal (FrameworkElement Tile, TimerTile Data)? TileAt(int x, int y)
    {
        foreach (var row in new[] { buffRow, cooldownRow })
            foreach (Tile tile in row.Children)
            {
                if (tile.Data is not { } data || !tile.IsVisible || PresentationSource.FromVisual(tile) is null) continue;
                var a = tile.PointToScreen(new Point(0, 0)); var b = tile.PointToScreen(new Point(tile.ActualWidth, tile.ActualHeight));
                if (x >= a.X && x < b.X && y >= a.Y && y < b.Y) return (tile, data);
            }
        return null;
    }
    void Hover()
    {
        var info = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
        var hit = IsVisible && GetCursorInfo(ref info) && (info.Flags & 1) != 0 ? TileAt(info.X, info.Y) : null;
        if (hit is not ({ } over, { } t)) { Card.Clear(); return; }
        int? level = LevelOf?.Invoke(t.Skill);
        if (Card.Skill != t.Skill || !Card.IsVisible)
        {
            // Built, styled and placed before it shows, so it never flashes elsewhere or in the default colours.
            Card.Load(t, level, Math.Clamp(settings.AssistantScale, .8, 1.6));
            Theme?.Invoke(Card);
            PlaceCard(over);
            if (!Card.IsVisible) Card.Show();
        }
        else { Card.Tick(t); PlaceCard(over); }
    }
    // Above the bar, centred on the icon; under it when there is no room above. Kept inside the screen.
    void PlaceCard(FrameworkElement tile)
    {
        var source = PresentationSource.FromVisual(this); if (source?.CompositionTarget is null) return;
        var toDip = source.CompositionTarget.TransformFromDevice;
        var a = toDip.Transform(tile.PointToScreen(new Point(0, 0))); var b = toDip.Transform(tile.PointToScreen(new Point(tile.ActualWidth, tile.ActualHeight)));
        var screen = SystemParameters.VirtualScreenWidth > 0 ? new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight) : SystemParameters.WorkArea;
        if (Card.Content is not FrameworkElement content) return;
        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double w = content.DesiredSize.Width, h = content.DesiredSize.Height;
        double left = Math.Clamp((a.X + b.X) / 2 - w / 2, screen.Left, Math.Max(screen.Left, screen.Right - w));
        double top = Top - h + 6 >= screen.Top ? Top - h + 6 : Top + ActualHeight - 6;
        Card.Left = left; Card.Top = top;
    }
    static void Fill(Panel row, IReadOnlyList<TimerTile> tiles, double size)
    {
        row.Visibility = tiles.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        while (row.Children.Count > tiles.Count) row.Children.RemoveAt(row.Children.Count - 1);
        while (row.Children.Count < tiles.Count) row.Children.Add(new Tile(size));
        for (int i = 0; i < tiles.Count; i++) ((Tile)row.Children[i]).Set(tiles[i]);
    }
    // Where it was left; the first time, centred low over the game, above the skill bar.
    void Place()
    {
        if (settings.AssistantLeft is double l && settings.AssistantTop is double tp) { Left = l; Top = tp; return; }
        UpdateLayout();
        double scale = PresentationSource.FromVisual(this)?.CompositionTarget.TransformFromDevice.M11 ?? 1;
        double left = 0, top = 0, width = SystemParameters.PrimaryScreenWidth, height = SystemParameters.PrimaryScreenHeight;
        var game = Process.GetProcessesByName("AION2").FirstOrDefault();
        if (game is not null)
            using (game)
                if (game.MainWindowHandle != IntPtr.Zero && GetWindowRect(game.MainWindowHandle, out var r) && r.Right > r.Left)
                { left = r.Left * scale; top = r.Top * scale; width = (r.Right - r.Left) * scale; height = (r.Bottom - r.Top) * scale; }
        Left = left + (width - Math.Max(ActualWidth, 300)) / 2;
        Top = top + height * .70;
    }

    /// <summary>An icon with the time left: a shade that shrinks as the cooldown runs (buffs: a green bar that empties).</summary>
    sealed class Tile : Grid
    {
        readonly Image icon = new() { Stretch = Stretch.UniformToFill };
        readonly Rectangle shade = new() { Fill = Shade, VerticalAlignment = VerticalAlignment.Top };
        readonly Border bar = new() { Height = 3, Background = Green, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom };
        readonly TextBlock text = new() { FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        readonly Border edge = new() { BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(5) };
        readonly double size;
        /// <summary>What the tile shows now, for the hover card.</summary>
        public TimerTile? Data { get; private set; }
        public Tile(double size)
        {
            this.size = size; Width = Height = size; Margin = new Thickness(2);
            text.FontSize = size >= 34 ? 13 : 11;
            text.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 1, Opacity = 1, Color = Colors.Black };
            var clip = new Border { CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x2A, 0x33)), ClipToBounds = true };
            var inner = new Grid(); inner.Children.Add(icon); inner.Children.Add(shade); inner.Children.Add(bar);
            clip.Child = inner;
            Clip = new RectangleGeometry(new Rect(0, 0, size, size), 5, 5);
            Children.Add(clip); Children.Add(text); Children.Add(edge);
        }
        public void Set(TimerTile t)
        {
            Data = t;
            icon.Source = OverlayWindow.Icon(t.Icon);
            double ratio = t.Total > 0 ? Math.Clamp(t.Left / t.Total, 0, 1) : 0;
            text.Text = t.Left <= 0 ? "" : t.Left >= 60 ? $"{Math.Ceiling(t.Left / 60)}m" : t.Left >= 10 ? $"{Math.Ceiling(t.Left)}" : $"{t.Left:0.0}";
            if (t.Buff)
            {
                shade.Height = 0; bar.Visibility = Visibility.Visible; bar.Width = size * ratio;
                edge.BorderBrush = t.Left < 3 ? Gold : Green;
            }
            else
            {
                bar.Visibility = Visibility.Collapsed; shade.Height = size * ratio;
                edge.BorderBrush = t.Left <= 0 ? Gold : Edge;
                edge.BorderThickness = new Thickness(t.Left <= 0 ? 2.5 : 1.5);
            }
        }
    }
}
