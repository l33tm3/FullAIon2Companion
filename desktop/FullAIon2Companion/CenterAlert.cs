using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace FullAIon2Companion;

/// <summary>
/// The boss-mod style warning: the skill name large in the upper middle of the game window, fading out after a couple
/// of seconds. Click-through and never focused, like the rest of the overlay.
/// </summary>
public sealed class CenterAlert : Window
{
    readonly TextBlock skill = new() { FontSize = 38, FontWeight = FontWeights.Black, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center };
    readonly TextBlock caster = new() { FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD9, 0xC9)), HorizontalAlignment = HorizontalAlignment.Center };
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out Rect32 rect);
    struct Rect32 { public int Left, Top, Right, Bottom; }
    /// <summary>First shown: the overlay hands it the game window as owner.</summary>
    public event Action? Shown;
    public CenterAlert()
    {
        Title = "FullAIon2Companion · Aviso"; SizeToContent = SizeToContent.WidthAndHeight;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowActivated = false; ShowInTaskbar = false; Opacity = 0;
        skill.Effect = new DropShadowEffect { Color = Color.FromRgb(0xFF, 0x3B, 0x1F), BlurRadius = 22, ShadowDepth = 0, Opacity = 1 };
        var stack = new StackPanel();
        stack.Children.Add(skill); stack.Children.Add(caster);
        Content = new Border
        {
            Padding = new Thickness(34, 12, 34, 14), CornerRadius = new CornerRadius(14),
            BorderBrush = new SolidColorBrush(Color.FromArgb(230, 0xFF, 0x6B, 0x3D)), BorderThickness = new Thickness(2),
            Background = new LinearGradientBrush(Color.FromArgb(215, 0x7A, 0x14, 0x0C), Color.FromArgb(215, 0x3A, 0x08, 0x06), 90),
            Child = stack,
        };
        SourceInitialized += (_, _) => { var hwnd = new WindowInteropHelper(this).Handle; SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x08000000 | 0x80 | 0x20); };
    }
    /// <summary>Shows the warning, restarting the fade when another cast comes in before the last one faded.</summary>
    public void Flash(string skillName, string? casterName)
    {
        skill.Text = "⚠  " + skillName.ToUpperInvariant();
        caster.Text = casterName ?? "";
        caster.Visibility = string.IsNullOrEmpty(casterName) ? Visibility.Collapsed : Visibility.Visible;
        if (!IsVisible) { Show(); Shown?.Invoke(); }
        UpdateLayout();
        Place();
        var fade = new DoubleAnimationUsingKeyFrames();
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2300))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(3000))));
        fade.Completed += (_, _) => { if (Opacity <= .01) Hide(); };
        BeginAnimation(OpacityProperty, fade);
    }
    // Upper middle of the game window (a third of the way down, above the character), else of the primary screen.
    void Place()
    {
        double scale = PresentationSource.FromVisual(this)?.CompositionTarget.TransformFromDevice.M11 ?? 1;
        double left = 0, top = 0, width = SystemParameters.PrimaryScreenWidth, height = SystemParameters.PrimaryScreenHeight;
        var game = Process.GetProcessesByName("AION2").FirstOrDefault();
        if (game is not null)
        {
            using (game)
                if (game.MainWindowHandle != IntPtr.Zero && GetWindowRect(game.MainWindowHandle, out var r) && r.Right > r.Left)
                { left = r.Left * scale; top = r.Top * scale; width = (r.Right - r.Left) * scale; height = (r.Bottom - r.Top) * scale; }
        }
        Left = left + (width - ActualWidth) / 2;
        Top = top + height * .26 - ActualHeight / 2;
    }
}
