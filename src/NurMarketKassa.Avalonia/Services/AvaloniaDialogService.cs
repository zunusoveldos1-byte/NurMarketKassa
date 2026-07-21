using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using NurMarketKassa.Ui.Shared;

namespace NurMarketKassa.AvaloniaHost.Services;

public sealed class AvaloniaDialogService : IDialogService
{
    public Task<bool> ConfirmAsync(string title, string message) =>
        AvaloniaPromptWindow.ShowConfirmAsync(GetOwner(), title, message);

    public Task ShowInfoAsync(string message) =>
        AvaloniaPromptWindow.ShowAlertAsync(GetOwner(), "Сообщение", message);

    public Task ShowErrorAsync(string message) =>
        AvaloniaPromptWindow.ShowAlertAsync(GetOwner(), "Ошибка", message);

    public Task<PrinterNotConnectedResult> ShowPrinterNotConnectedAsync() =>
        AvaloniaPromptWindow.ShowPrinterNotConnectedAsync(GetOwner());

    public Task<bool> ConfirmPaymentAsync() =>
        AvaloniaPromptWindow.ShowConfirmAsync(
            GetOwner(),
            "Подтверждение",
            "Подтвердить оплату?",
            confirmText: "Подтвердить",
            cancelText: "Нет");

    private static Window? GetOwner()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return null;

        return desktop.MainWindow;
    }
}

internal static class AvaloniaPromptWindow
{
    private static readonly IBrush PrimaryBrush = new SolidColorBrush(Color.Parse("#007ACC"));
    private static readonly IBrush SecondaryBrush = new SolidColorBrush(Color.Parse("#F1F5F9"));
    private static readonly IBrush BorderBrush = new SolidColorBrush(Color.Parse("#CBD5E1"));

    public static Task<bool> ShowConfirmAsync(
        Window? owner,
        string title,
        string message,
        string confirmText = "Да",
        string cancelText = "Нет")
    {
        var dialog = CreateShell(title, message, width: 480);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 12,
            Margin = new Thickness(0, 20, 0, 0)
        };

        var cancel = CreateButton(cancelText, isPrimary: false);
        cancel.Click += (_, _) => dialog.Close(false);
        buttons.Children.Add(cancel);

        var confirm = CreateButton(confirmText, isPrimary: true);
        confirm.IsDefault = true;
        confirm.Click += (_, _) => dialog.Close(true);
        buttons.Children.Add(confirm);

        ((StackPanel)dialog.Content!).Children.Add(buttons);
        return ShowDialogAsync<bool>(RequireOwner(owner), dialog);
    }

    public static async Task ShowAlertAsync(Window? owner, string title, string message)
    {
        var dialog = CreateShell(title, message, width: 420);
        var ok = CreateButton("ОК", isPrimary: true);
        ok.IsDefault = true;
        ok.Click += (_, _) => dialog.Close();
        ok.HorizontalAlignment = HorizontalAlignment.Right;
        ok.Margin = new Thickness(0, 20, 0, 0);
        ((StackPanel)dialog.Content!).Children.Add(ok);

        if (owner is not null)
            await dialog.ShowDialog(RequireOwner(owner));
    }

    public static Task<PrinterNotConnectedResult> ShowPrinterNotConnectedAsync(Window? owner)
    {
        var dialog = CreateShell(
            "Принтер не подключен",
            "Принтер не найден. Продолжить без печати чека?",
            width: 520);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 12,
            Margin = new Thickness(0, 20, 0, 0)
        };

        var cancel = CreateButton("Отмена", isPrimary: false);
        cancel.Click += (_, _) => dialog.Close(PrinterNotConnectedResult.Cancel);
        buttons.Children.Add(cancel);

        var proceed = CreateButton("Продолжить без печати", isPrimary: true);
        proceed.IsDefault = true;
        proceed.Click += (_, _) => dialog.Close(PrinterNotConnectedResult.ContinueWithoutPrint);
        buttons.Children.Add(proceed);

        ((StackPanel)dialog.Content!).Children.Add(buttons);
        return ShowDialogAsync<PrinterNotConnectedResult>(RequireOwner(owner), dialog);
    }

    private static Task<T> ShowDialogAsync<T>(Window owner, Window dialog) =>
        dialog.ShowDialog<T>(owner);

    private static Window RequireOwner(Window? owner) =>
        owner ?? throw new InvalidOperationException("Cannot show dialog: no active Avalonia window.");

    private static Window CreateShell(string title, string message, double width)
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(24),
            Children =
            {
                new TextBlock
                {
                    Text = title,
                    FontSize = 20,
                    FontWeight = FontWeight.Bold,
                    Margin = new Thickness(0, 0, 0, 8)
                },
                new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 15,
                    Opacity = 0.85
                }
            }
        };

        return new Window
        {
            Title = title,
            Width = width,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel,
            CanResize = false
        };
    }

    private static Button CreateButton(string text, bool isPrimary) =>
        new()
        {
            Content = text,
            Padding = new Thickness(16, 10),
            Background = isPrimary ? PrimaryBrush : SecondaryBrush,
            Foreground = isPrimary ? Brushes.White : Brushes.Black,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8)
        };
}
