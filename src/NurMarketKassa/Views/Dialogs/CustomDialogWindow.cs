using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace NurMarketKassa.Views.Dialogs;

/// <summary>Базовое модальное окно: затемнение на весь экран + компактная карточка по центру.</summary>
public class CustomDialogWindow : Window
{
    private readonly Grid _rootGrid;
    protected readonly Border Card;
    protected readonly StackPanel Body;

    protected CustomDialogWindow(double cardWidth = 540)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        MergeAppResources();

        Body = new StackPanel();

        Card = new Border
        {
            Width = Math.Clamp(cardWidth, PosDialogUi.CardMinWidth, PosDialogUi.CardMaxWidth),
            MinWidth = PosDialogUi.CardMinWidth,
            MaxWidth = PosDialogUi.CardMaxWidth,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.White,
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(28, 24, 28, 24),
            Effect = new DropShadowEffect
            {
                BlurRadius = 28,
                ShadowDepth = 0,
                Opacity = 0.22,
                Color = Colors.Black,
            },
            Child = Body,
        };

        Card.MaxHeight = SystemParameters.WorkArea.Height * 0.9;

        _rootGrid = new Grid
        {
            Opacity = 1,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        _rootGrid.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        });
        _rootGrid.Children.Add(Card);
        Content = _rootGrid;

        Loaded += OnLoaded;
    }

    protected void AddTitle(string text) => Body.Children.Add(PosDialogUi.CreateTitle(text));

    protected void AddMessage(string text, TextAlignment align = TextAlignment.Left) =>
        Body.Children.Add(PosDialogUi.CreateMessage(text, align));

    protected void AddContent(UIElement element) => Body.Children.Add(element);

    protected Button CreatePrimaryButton(string text) =>
        PosDialogUi.CreateButton(text, PosDialogButtonStyle.Primary);

    protected Button CreateSecondaryButton(string text) =>
        PosDialogUi.CreateButton(text, PosDialogButtonStyle.Secondary);

    protected Button CreateDangerButton(string text) =>
        PosDialogUi.CreateButton(text, PosDialogButtonStyle.Danger);

    protected Grid CreateTwoButtonRow(Button left, Button right)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 2);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }

    protected StackPanel CreateStackedButtons(params Button[] buttons)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        for (var i = 0; i < buttons.Length; i++)
        {
            if (i > 0)
                buttons[i].Margin = new Thickness(0, 12, 0, 0);
            buttons[i].HorizontalAlignment = HorizontalAlignment.Stretch;
            panel.Children.Add(buttons[i]);
        }

        return panel;
    }

    protected void CloseWithResult(bool? result)
    {
        if (result == null)
            return;

        PosModalHost.PlayCloseAnimation(this, () =>
        {
            DialogResult = result;
            Close();
        });
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (Owner != null)
            PosDialogLayout.FitOverlayToOwner(this, Owner);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PosDialogLayout.FitOverlayToOwner(this, Owner);
        PosModalHost.PlayOpenAnimation(_rootGrid, Card);
    }

    private void MergeAppResources()
    {
        if (Application.Current?.Resources == null)
            return;

        foreach (var dict in Application.Current.Resources.MergedDictionaries)
        {
            if (!Resources.MergedDictionaries.Contains(dict))
                Resources.MergedDictionaries.Add(dict);
        }
    }
}
