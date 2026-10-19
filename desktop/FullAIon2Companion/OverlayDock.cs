using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace FullAIon2Companion;

/// <summary>
/// The overlay's control strip. The overlay itself lets every click through to the game, so this small strip is
/// the one part that takes the mouse: drag it to move the overlay, open the drops panel or the companion panel.
/// It never takes the keyboard focus from the game (WS_EX_NOACTIVATE).
/// </summary>
public sealed class OverlayDock : Window
{
    static readonly Brush Face = Frozen(Color.FromArgb(215, 13, 20, 27)), Edge = Frozen(Color.FromArgb(200, 51, 87, 91)),
        Glyph = Frozen(Color.FromRgb(0xC3, 0xCF, 0xD9)), Hover = Frozen(Color.FromArgb(255, 0x2B, 0x41, 0x4D)), On = Frozen(Color.FromRgb(0x71, 0xDE, 0xD6));
    readonly Button drops, minimap, troop, news;
    readonly System.Windows.Shapes.Ellipse newsDot = new() { Width = 8, Height = 8, Fill = Frozen(Color.FromRgb(0x5F, 0xE0, 0xA0)), Stroke = Frozen(Color.FromRgb(0x0D, 0x14, 0x1B)), StrokeThickness = 1.5, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    public event Action? DragFinished, DropsClicked, SettingsClicked, MinimapClicked, BreakdownClicked, TroopClicked, NewsClicked;
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    public OverlayDock()
    {
        Title = "FullAIon2Companion · Controles"; Width = 32; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowActivated = false; ShowInTaskbar = false;
        var grip = new Border { Height = 30, Background = Brushes.Transparent, Cursor = Cursors.SizeAll, ToolTip = "Arrastra para mover el overlay",
            Child = new TextBlock { Text = "⠿", FontSize = 17, Foreground = Glyph, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        grip.MouseLeftButtonDown += (_, e) => { if (e.ButtonState != MouseButtonState.Pressed) return; DragMove(); DragFinished?.Invoke(); };
        drops = Icon("", "Panel del objetivo: botín de la criatura o perfil del jugador (Ctrl+Alt+B)", () => DropsClicked?.Invoke());
        var stack = new StackPanel();
        stack.Children.Add(grip);
        stack.Children.Add(drops);
        minimap = Icon("\uE707", "Minimapa (Ctrl+Alt+N)", () => MinimapClicked?.Invoke());
        stack.Children.Add(minimap);
        troop = Icon("", "Tropa: ver toda la tropa o solo tu grupo", () => TroopClicked?.Invoke());
        stack.Children.Add(troop);
        stack.Children.Add(Icon("\uE9D2", "Desglose de tu combate por habilidad (Ctrl+Alt+D)", () => BreakdownClicked?.Invoke()));
        // Novedades: the update available and the site's notices; a green dot while something is unread.
        news = Icon("", "Novedades: actualizaciones y noticias", () => NewsClicked?.Invoke());
        var newsGlyph = (UIElement)news.Content; var newsFace = new Grid(); news.Content = newsFace; newsFace.Children.Add(newsGlyph); newsFace.Children.Add(newsDot); newsDot.Margin = new Thickness(0, -3, -6, 0);
        stack.Children.Add(news);
        stack.Children.Add(Icon("", "Panel de FullAIon2Companion", () => SettingsClicked?.Invoke()));
        Content = new Border { Background = Face, BorderBrush = Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(1, 2, 1, 3), Child = stack };
        SourceInitialized += (_, _) => { var hwnd = new WindowInteropHelper(this).Handle; SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x08000000 | 0x80); };
    }
    public void ApplyScale(double k) { ((FrameworkElement)Content).LayoutTransform = new ScaleTransform(k, k); Width = 32 * k; }
    public void SetNewsUnread(bool unread, string? tip = null) { newsDot.Visibility = unread ? Visibility.Visible : Visibility.Collapsed; news.ToolTip = tip ?? "Novedades: actualizaciones y noticias"; }
    public void SetMinimapOn(bool on) => ((TextBlock)minimap.Content).Foreground = on ? On : Glyph;
    public void SetMinimapAccess(bool allowed) { minimap.ToolTip = allowed ? "Minimapa (Ctrl+Alt+N)" : "Minimapa"; minimap.Opacity = allowed ? 1 : .45; }
    public void SetDropsOn(bool on) => ((TextBlock)drops.Content).Foreground = on ? On : Glyph;
    public void SetTroopOn(bool on) { ((TextBlock)troop.Content).Foreground = on ? On : Glyph; troop.ToolTip = on ? "Mostrando toda la tropa · clic para ver solo tu grupo" : "Mostrando solo tu grupo · clic para ver toda la tropa"; }
    static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    new static Button Icon(string glyph, string tip, Action click)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 14, Foreground = Glyph },
            Width = 28, Height = 28, Margin = new Thickness(0, 1, 0, 1), ToolTip = tip, Cursor = Cursors.Hand, Focusable = false,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Template = Flat(),
        };
        button.Click += (_, _) => click();
        return button;
    }
    // A bare template: the default WPF button chrome (grey gradient) does not belong on a game overlay.
    static ControlTemplate Flat()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border)) { Name = "face" };
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        template.VisualTree = border;
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, Hover, "face"));
        template.Triggers.Add(hover);
        return template;
    }
}
