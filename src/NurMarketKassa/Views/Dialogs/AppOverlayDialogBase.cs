using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NurMarketKassa.Views.Dialogs;

/// <summary>Модальное окно поверх родителя: затемнение только области owner + карточка по центру.</summary>
public class AppOverlayDialogBase : Window
{
    private bool _isClosing;

    public AppOverlayDialogBase()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Padding = new Thickness(0);

        MergeAppResources();
        PreviewKeyDown += OnPreviewKeyDown;
        Loaded += OnLoaded;
        SourceInitialized += (_, _) =>
        {
            if (Owner != null)
                PosDialogLayout.FitOverlayToOwner(this, Owner);
        };
    }

    public void CloseWithAnimation(bool result)
    {
        if (_isClosing)
            return;

        _isClosing = true;
        PosModalHost.PlayCloseAnimation(this, () =>
        {
            if (!IsLoaded)
                return;

            DialogResult = result;
        });
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _isClosing)
            return;

        e.Handled = true;
        CloseWithAnimation(false);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PosDialogLayout.FitOverlayToOwner(this, Owner);

        if (FindName("DialogCard") is FrameworkElement card)
            card.MaxHeight = SystemParameters.WorkArea.Height * 0.9;

        if (FindName("RootGrid") is Grid root && FindName("DialogCard") is FrameworkElement cardEl)
            PosModalHost.PlayOpenAnimation(root, cardEl);
    }

    private void MergeAppResources()
    {
        const string themePath = "/Views/Dialogs/NurMarketDialogTheme.xaml";
        if (Resources.MergedDictionaries.Any(d => d.Source?.OriginalString == themePath))
            return;

        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(themePath, UriKind.Relative),
        });
    }
}
