using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NurMarketKassa.Views.Dialogs;

internal static class PosModalHost
{
    private static readonly Duration OpenDuration = TimeSpan.FromMilliseconds(180);
    private static readonly Duration CloseDuration = TimeSpan.FromMilliseconds(160);

    public static void PlayOpenAnimation(Grid rootGrid, FrameworkElement card)
    {
        rootGrid.BeginAnimation(UIElement.OpacityProperty, null);
        rootGrid.Opacity = 0;

        var rootFade = new DoubleAnimation(0, 1, OpenDuration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        rootGrid.BeginAnimation(UIElement.OpacityProperty, rootFade);

        card.BeginAnimation(UIElement.OpacityProperty, null);
        card.Opacity = 0;
        card.RenderTransformOrigin = new Point(0.5, 0.5);

        var scale = new ScaleTransform(0.95, 0.95);
        card.RenderTransform = scale;

        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        card.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, OpenDuration) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.95, 1, OpenDuration) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.95, 1, OpenDuration) { EasingFunction = ease });
    }

    public static void PlayCloseAnimation(Window window, Action onCompleted)
    {
        if (window.Content is not Grid root)
        {
            onCompleted();
            return;
        }

        var ease = new QuadraticEase { EasingMode = EasingMode.EaseIn };
        var rootFade = new DoubleAnimation(root.Opacity, 0, CloseDuration) { EasingFunction = ease };
        rootFade.Completed += (_, _) => onCompleted();
        root.BeginAnimation(UIElement.OpacityProperty, rootFade);

        if (window.FindName("DialogCard") is FrameworkElement card)
        {
            card.RenderTransformOrigin = new Point(0.5, 0.5);
            if (card.RenderTransform is not ScaleTransform scale)
            {
                scale = new ScaleTransform(1, 1);
                card.RenderTransform = scale;
            }

            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0.95, CloseDuration) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 0.95, CloseDuration) { EasingFunction = ease });
            card.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(card.Opacity, 0, CloseDuration) { EasingFunction = ease });
        }
    }
}
