using System.Windows;
using System.Windows.Controls;

namespace NurMarketKassa.Views.Dialogs;

internal static class PosModalHost
{
    public static void PlayOpenAnimation(Grid rootGrid, FrameworkElement card)
    {
        rootGrid.BeginAnimation(UIElement.OpacityProperty, null);
        rootGrid.Opacity = 1;

        card.BeginAnimation(UIElement.OpacityProperty, null);
        card.Opacity = 1;
        card.RenderTransform = null;
    }

    public static void PlayCloseAnimation(Window window, Action onCompleted)
    {
        if (window.Content is Grid root)
        {
            root.BeginAnimation(UIElement.OpacityProperty, null);
            root.Opacity = 1;
        }

        onCompleted();
    }
}
