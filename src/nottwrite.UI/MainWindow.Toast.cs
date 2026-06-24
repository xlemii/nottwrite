using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace nottwrite.UI;

public partial class MainWindow
{
    public enum ToastKind { Success, Error, Warning, Info }

    private const int MaxToasts = 4;

    /// Show a transient toast notification (semantic colours, auto-dismiss).
    /// Optional action shows a clickable button (e.g. "Undo").
    public void ShowToast(string message, ToastKind kind = ToastKind.Info, double seconds = 2.6,
        string? actionLabel = null, Action? action = null)
    {
        if (ToastHost == null) return;
        if (actionLabel != null) seconds = Math.Max(seconds, 5.0);   // give time to click

        var (accent, icon) = kind switch
        {
            ToastKind.Success => (GetBrush("SuccessBrush"), "✓"),  // ✓
            ToastKind.Error   => (GetBrush("ErrorBrush"),   "✕"),  // ✕
            ToastKind.Warning => (GetBrush("WarningBrush"), "⚠"),  // ⚠
            _                 => (GetBrush("InfoBrush"),    "ℹ"),  // ℹ
        };

        // Icon chip
        var iconText = new TextBlock
        {
            Text = icon, FontSize = 12, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var iconChip = new Border
        {
            Width = 22, Height = 22, CornerRadius = new CornerRadius(11),
            Background = accent, Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = iconText,
        };

        var msg = new TextBlock
        {
            Text = message, FontSize = 12.5,
            Foreground = GetBrush("PrimaryText"),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap, MaxWidth = 300,
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(iconChip);
        row.Children.Add(msg);

        Border? card = null;   // forward ref for action handler
        if (actionLabel != null && action != null)
        {
            var actionBtn = new TextBlock
            {
                Text = actionLabel, FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = GetBrush("AccentBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 0, 0, 0), Cursor = Cursors.Hand,
            };
            actionBtn.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                action();
                if (card != null) DismissToast(card);
            };
            row.Children.Add(actionBtn);
        }

        card = new Border
        {
            Background       = GetBrush("PanelBg"),
            BorderBrush      = GetBrush("AppBorderBrush"),
            BorderThickness  = new Thickness(1),
            CornerRadius     = new CornerRadius(10),
            Padding          = new Thickness(12, 10, 16, 10),
            Margin           = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Child = row,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black, Opacity = 0.45,
                BlurRadius = 20, ShadowDepth = 4, Direction = 270,
            },
        };
        // left accent strip
        card.BorderBrush = GetBrush("AppBorderBrush");

        // entrance: slide up + fade
        var trans = new TranslateTransform(0, 16);
        card.RenderTransform = trans;
        card.Opacity = 0;

        ToastHost.Children.Add(card);
        while (ToastHost.Children.Count > MaxToasts)
            ToastHost.Children.RemoveAt(0);

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        card.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        trans.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });

        // dismiss on click
        card.MouseLeftButtonUp += (_, _) => DismissToast(card);

        // auto-dismiss
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        timer.Tick += (_, _) => { timer.Stop(); DismissToast(card); };
        timer.Start();
    }

    private void DismissToast(Border card)
    {
        if (card.Tag is bool b && b) return;   // already dismissing
        card.Tag = true;

        var fade = new DoubleAnimation(card.Opacity, 0, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) => ToastHost.Children.Remove(card);
        card.BeginAnimation(UIElement.OpacityProperty, fade);

        if (card.RenderTransform is TranslateTransform t)
            t.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(0, 8, TimeSpan.FromMilliseconds(180)));
    }
}
