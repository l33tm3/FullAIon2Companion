using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace FullAIon2Companion;

/// <summary>
/// Event notifications in a corner of the game window, like the other meter's: the event's name, when it starts, the
/// minutes left in large type and a bar that drains while the card is up (12 s). Several stack. Click-through and never
/// focused, so it does not get in the way of the game.
/// </summary>
public sealed class EventToast : Window
{
    const double Seconds = 12;
    static readonly Brush Teal = Paint("#71DED6"), Muted = Paint("#95A4B6"), Card = Paint("#EB111A22"), Edge = Paint("#C833575B");
    readonly StackPanel stack = new();
    readonly AppSettings settings;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    readonly List<(Border Card, TextBlock Left, Border Bar, DateTimeOffset At, DateTime Until)> cards = [];
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out Rect32 rect);
    struct Rect32 { public int Left, Top, Right, Bottom; }
    public event Action? Shown;

    public EventToast(AppSettings settings)
    {
        this.settings = settings;
        Title = "FullAIon2Companion · Avisos"; Width = 330; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowActivated = false; ShowInTaskbar = false;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Content = stack;
        SourceInitialized += (_, _) => { var hwnd = new WindowInteropHelper(this).Handle; SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x08000000 | 0x80 | 0x20); };
        timer.Tick += (_, _) => Refresh();
    }

    /// <summary>A card for an event: "soon" (minutes before) or "start" (now).</summary>
    public void Notify(string name, string color, DateTimeOffset at, bool starting)
    {
        var icon = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(7), Background = Paint("#1C2B36"), Margin = new Thickness(0, 0, 12, 0),
            Child = new TextBlock { Text = "⚔", FontSize = 17, Foreground = TryPaint(color) ?? Teal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        var title = new TextBlock { Text = name, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis };
        var when = new TextBlock { Text = starting ? "¡Comienza ahora!" : $"Empieza a las {at.LocalDateTime:HH:mm}", FontSize = 11, Foreground = Muted, Margin = new Thickness(0, 2, 0, 0) };
        var left = new TextBlock { FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Teal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; text.Children.Add(title); text.Children.Add(when);
        var row = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left); row.Children.Add(icon);
        DockPanel.SetDock(left, Dock.Right); row.Children.Add(left);
        row.Children.Add(text);
        var bar = new Border { Height = 3, Background = Teal, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0), Width = 300 };
        var body = new StackPanel(); body.Children.Add(row); body.Children.Add(bar);
        var card = new Border { Background = Card, BorderBrush = Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 12, 14, 10), Margin = new Thickness(0, 0, 0, 8), Child = body, Opacity = 0 };
        cards.Add((card, left, bar, at, DateTime.Now.AddSeconds(Seconds)));
        stack.Children.Add(card);
        card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        Refresh();
        if (!IsVisible) { Show(); Shown?.Invoke(); }
        timer.Start();
        Dispatcher.BeginInvoke(Place, DispatcherPriority.Loaded);
    }
    /// <summary>For the style previews: a notice's card in the window, without showing it.</summary>
    internal void PreviewMessage(string titleText, string bodyText) { var card = MessageCard(titleText, bodyText, false, out _); card.Opacity = 1; stack.Children.Add(card); }
    /// <summary>A plain-text notice (from the administration): title and body, no countdown.</summary>
    public void Message(string titleText, string bodyText, bool warning)
    {
        var card = MessageCard(titleText, bodyText, warning, out var bar);
        cards.Add((card, new TextBlock(), bar, DateTimeOffset.Now, DateTime.Now.AddSeconds(Seconds + 6)));
        stack.Children.Add(card);
        card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        if (!IsVisible) { Show(); Shown?.Invoke(); }
        timer.Start();
        Dispatcher.BeginInvoke(Place, DispatcherPriority.Loaded);
    }
    Border MessageCard(string titleText, string bodyText, bool warning, out Border bar)
    {
        var accent = warning ? Paint("#E9C77E") : Teal;
        var icon = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(7), Background = Paint("#1C2B36"), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = warning ? "⚠" : "ℹ", FontSize = 17, Foreground = accent, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = titleText, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock { Text = bodyText, FontSize = 11, Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        var row = new DockPanel(); DockPanel.SetDock(icon, Dock.Left); row.Children.Add(icon); row.Children.Add(text);
        bar = new Border { Height = 3, Background = accent, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0), Width = 300 };
        var body = new StackPanel(); body.Children.Add(row); body.Children.Add(bar);
        var card = new Border { Background = Card, BorderBrush = warning ? accent : Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 12, 14, 10), Margin = new Thickness(0, 0, 0, 8), Child = body, Opacity = 0 };
        return card;
    }
    void Refresh()
    {
        var now = DateTime.Now;
        foreach (var c in cards.ToList())
        {
            double secondsLeft = (c.At - DateTimeOffset.Now).TotalSeconds;
            c.Left.Text = secondsLeft <= 0 ? "Ya" : secondsLeft < 60 ? $"{secondsLeft:0} s" : $"{Math.Ceiling(secondsLeft / 60):0} min";
            double life = Math.Clamp((c.Until - now).TotalSeconds / Seconds, 0, 1);
            c.Bar.Width = Math.Max(0, (c.Card.ActualWidth - 30) * life);
            if (life <= 0)
            {
                cards.Remove(c);
                var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
                fade.Completed += (_, _) => { stack.Children.Remove(c.Card); if (cards.Count == 0) { timer.Stop(); Hide(); } else Place(); };
                c.Card.BeginAnimation(OpacityProperty, fade);
            }
        }
    }
    // The chosen corner of the game window (else of the primary screen), clear of the game's own frame.
    void Place()
    {
        UpdateLayout();
        double scale = PresentationSource.FromVisual(this)?.CompositionTarget.TransformFromDevice.M11 ?? 1;
        double left = 0, top = 0, width = SystemParameters.PrimaryScreenWidth, height = SystemParameters.PrimaryScreenHeight;
        var game = Process.GetProcessesByName("AION2").FirstOrDefault();
        if (game is not null)
            using (game)
                if (game.MainWindowHandle != IntPtr.Zero && GetWindowRect(game.MainWindowHandle, out var r) && r.Right > r.Left)
                { left = r.Left * scale; top = r.Top * scale; width = (r.Right - r.Left) * scale; height = (r.Bottom - r.Top) * scale; }
        bool right = settings.EventToastCorner.EndsWith("right"), bottom = settings.EventToastCorner.StartsWith("bottom");
        Left = right ? left + width - ActualWidth - 24 : left + 24;
        Top = bottom ? top + height - ActualHeight - 120 : top + 90;
    }
    static Brush Paint(string color) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); b.Freeze(); return b; }
    static Brush? TryPaint(string color) { try { return Paint(color); } catch { return null; } }
}
