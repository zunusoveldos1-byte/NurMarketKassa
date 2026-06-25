using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace NurMarketKassa.Views.Dialogs;

/// <summary>Модальное окно: затемнение + карточка (разметка в XAML, RootGrid / DialogCard).</summary>
public class PosDialogWindowBase : Window
{
    internal const string ThemeSource = "/NurCrmKassa;component/Views/Dialogs/PosDialogTheme.xaml";

    public static readonly DependencyProperty DialogCardMinWidthProperty =
        DependencyProperty.Register(nameof(DialogCardMinWidth), typeof(double), typeof(PosDialogWindowBase),
            new PropertyMetadata(PosDialogUi.CardMinWidth));

    public static readonly DependencyProperty DialogCardMaxWidthProperty =
        DependencyProperty.Register(nameof(DialogCardMaxWidth), typeof(double), typeof(PosDialogWindowBase),
            new PropertyMetadata(PosDialogUi.CardMaxWidth));

    public double DialogCardMinWidth
    {
        get => (double)GetValue(DialogCardMinWidthProperty);
        set => SetValue(DialogCardMinWidthProperty, value);
    }

    public double DialogCardMaxWidth
    {
        get => (double)GetValue(DialogCardMaxWidthProperty);
        set => SetValue(DialogCardMaxWidthProperty, value);
    }

    public PosDialogWindowBase()
    {
        EnsureDialogThemeResources();

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Padding = new Thickness(0);

        Loaded += OnLoaded;
    }

    internal static void EnsureDialogThemeResources(ResourceDictionary resources)
    {
        if (resources.MergedDictionaries.Any(d =>
                string.Equals(d.Source?.OriginalString, ThemeSource, StringComparison.OrdinalIgnoreCase)))
            return;

        resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(ThemeSource, UriKind.Relative),
        });
    }

    private void EnsureDialogThemeResources() => EnsureDialogThemeResources(Resources);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Owner != null)
            PosDialogLayout.FitOverlayToOwner(this, Owner);

        if (FindName("DialogCard") is Border card)
        {
            card.MinWidth = DialogCardMinWidth;
            card.MaxWidth = DialogCardMaxWidth;
            card.MaxHeight = SystemParameters.WorkArea.Height * 0.9;
        }

        if (FindName("RootGrid") is Grid root && FindName("DialogCard") is FrameworkElement cardEl)
            PosModalHost.PlayOpenAnimation(root, cardEl);
    }
}
