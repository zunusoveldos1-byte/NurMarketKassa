using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NurMarketKassa.Views.Dialogs;

internal static class PosDialogUi
{
    public static readonly Color TitleColor = Color.FromRgb(0x0F, 0x17, 0x2A);
    public static readonly Color SubtitleColor = Color.FromRgb(0x64, 0x74, 0x8B);
    public static readonly Color AccentYellow = Color.FromRgb(0xF4, 0xD3, 0x0B);
    public static readonly Color DangerRed = Color.FromRgb(0xC5, 0x22, 0x1F);

    public const double CardMinWidth = 520;
    public const double CardMaxWidth = 580;
    public const double ButtonHeight = 56;
    public const double ButtonRadius = 12;

    public static TextBlock CreateTitle(string text) => new()
    {
        Text = text,
        FontSize = 32,
        FontWeight = FontWeights.Bold,
        Foreground = new SolidColorBrush(TitleColor),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 12),
    };

    public static TextBlock CreateMessage(string text, TextAlignment align = TextAlignment.Left) => new()
    {
        Text = text,
        FontSize = 18,
        Foreground = new SolidColorBrush(SubtitleColor),
        TextWrapping = TextWrapping.Wrap,
        TextAlignment = align,
        Margin = new Thickness(0, 0, 0, 20),
    };

    public static Button CreateButton(string text, PosDialogButtonStyle style)
    {
        var btn = new Button
        {
            Content = text,
            Height = ButtonHeight,
            MinHeight = ButtonHeight,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 0, 16, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
        };

        switch (style)
        {
            case PosDialogButtonStyle.Secondary:
                btn.Background = Brushes.White;
                btn.BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1));
                btn.Foreground = new SolidColorBrush(TitleColor);
                break;
            case PosDialogButtonStyle.Danger:
                btn.Background = new SolidColorBrush(DangerRed);
                btn.BorderBrush = new SolidColorBrush(DangerRed);
                btn.Foreground = Brushes.White;
                break;
            default:
                btn.Background = new SolidColorBrush(AccentYellow);
                btn.BorderBrush = new SolidColorBrush(Color.FromRgb(0xD4, 0xB8, 0x0A));
                btn.Foreground = new SolidColorBrush(TitleColor);
                break;
        }

        btn.Template = CreateRoundedButtonTemplate();
        return btn;
    }

    public static Grid CreateButtonRow(params (Button Button, int Column)[] buttons)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        for (var i = 0; i < buttons.Length; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition());

        for (var i = 0; i < buttons.Length - 1; i++)
        {
            grid.ColumnDefinitions.Insert(i * 2 + 1, new ColumnDefinition { Width = new GridLength(12) });
        }

        var col = 0;
        foreach (var (button, _) in buttons)
        {
            Grid.SetColumn(button, col);
            grid.Children.Add(button);
            col += 2;
        }

        return grid;
    }

    private static ControlTemplate CreateRoundedButtonTemplate()
    {
        const string xaml = """
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             TargetType="Button">
              <Border x:Name="bd" Background="{TemplateBinding Background}"
                      BorderBrush="{TemplateBinding BorderBrush}"
                      BorderThickness="{TemplateBinding BorderThickness}"
                      CornerRadius="12" Padding="{TemplateBinding Padding}">
                <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True">
                  <Setter TargetName="bd" Property="Opacity" Value="0.92"/>
                </Trigger>
                <Trigger Property="IsPressed" Value="True">
                  <Setter TargetName="bd" Property="Opacity" Value="0.85"/>
                </Trigger>
                <Trigger Property="IsEnabled" Value="False">
                  <Setter TargetName="bd" Property="Opacity" Value="0.55"/>
                </Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
            """;

        return (ControlTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
    }
}

internal enum PosDialogButtonStyle
{
    Primary,
    Secondary,
    Danger,
}
