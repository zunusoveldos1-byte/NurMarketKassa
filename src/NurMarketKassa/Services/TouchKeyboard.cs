using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NurMarketKassa.Views.Dialogs;

namespace NurMarketKassa.Services;

/// <summary>
/// Экранная клавиатура: авто-показ при фокусе в поле ввода (если включено в настройках) и по кнопке ⌨.
/// </summary>
public static class TouchKeyboard
{
    private static object? _lastAutoKbFocusTarget;
    private static long _lastAutoKbFocusTicks;

    public static void TryShow(Window? owner = null) => FrmKeyboard.ShowKeyboard(owner);

    public static void ShowOnDemand(Window? owner = null) => FrmKeyboard.ShowKeyboard(owner);

    public static void Close() => FrmKeyboard.KillKeyboard();

    internal static void OnPreviewKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.NewFocus is not TextBox and not PasswordBox)
            return;

        if (e.NewFocus is not UIElement ui || !ui.IsEnabled || !ui.IsVisible)
            return;

        if (e.NewFocus is TextBox { IsReadOnly: true })
            return;

        if (e.NewFocus is DependencyObject dep && IsInsideKeyboardWindow(dep))
            return;

        VirtualKeyboardInput.RememberInputTarget(e.NewFocus);

        if (!UserPreferences.Instance.AutoShowTouchKeyboard)
            return;

        if (ShouldSuppressRapidRepeatFocus(e.NewFocus))
            return;

        var owner = e.NewFocus is DependencyObject focusDep
            ? Window.GetWindow(focusDep)
            : Application.Current?.MainWindow;

        FrmKeyboard.ShowKeyboard(owner);
    }

    private static bool IsInsideKeyboardWindow(DependencyObject element)
    {
        var window = Window.GetWindow(element);
        return window is FrmKeyboard;
    }

    private static bool ShouldSuppressRapidRepeatFocus(IInputElement? el)
    {
        if (el is not DependencyObject d)
            return false;

        var now = Environment.TickCount64;
        if (ReferenceEquals(d, _lastAutoKbFocusTarget) && now - _lastAutoKbFocusTicks < 550)
            return true;

        _lastAutoKbFocusTarget = d;
        _lastAutoKbFocusTicks = now;
        return false;
    }
}
