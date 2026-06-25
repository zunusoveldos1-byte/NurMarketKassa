using NurMarketKassa.Services;
using NurMarketKassa.ViewModels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using MouseButton = System.Windows.Input.MouseButton;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;

namespace NurMarketKassa.Views.Dialogs;

public partial class FrmKeyboard : Window
{
    private const int GwlExstyle = -20;
    private const int WsExNoActivate = 0x08000000;

    private FrmKeyboardViewModel _model = null!;
    private readonly LinkedList<string> _installedLangs = new();

    public static bool IsShift { get; set; }
    public static Window? CurrentForm { get; private set; }

    public static bool IsShown =>
        CurrentForm is FrmKeyboard keyboard && keyboard.IsVisible;

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    public FrmKeyboard()
    {
        InitializeComponent();
        ShowActivated = false;
        Topmost = true;

        _model = new FrmKeyboardViewModel();
        DataContext = _model;

        Loaded += FrmKeyboard_Loaded;

        CurrentForm = this;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(handle, GwlExstyle);
        SetWindowLong(handle, GwlExstyle, style | WsExNoActivate);
    }

    public static void KillKeyboard()
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (CurrentForm is FrmKeyboard keyboard)
                keyboard.Close();
        });
    }

    public static void ShowKeyboard(Window? owner = null, bool hideLetters = false)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (CurrentForm is FrmKeyboard existing && existing.IsVisible)
            {
                if (owner != null)
                    existing.Owner = owner;

                ApplyAdaptiveLayout(hideLetters);
                existing.PositionOnScreen();
                return;
            }

            if (CurrentForm is FrmKeyboard stale)
            {
                try
                {
                    stale.Close();
                }
                catch
                {
                    /* ignore */
                }

                CurrentForm = null;
            }

            try
            {
                var keyboard = new FrmKeyboard();
                keyboard.Owner = owner;
                keyboard.Show();
                ApplyAdaptiveLayout(hideLetters);
                keyboard.PositionOnScreen();
            }
            catch (Exception ex)
            {
                CurrentForm = null;
                PosLogger.Log($"FrmKeyboard: {ex.GetType().Name}: {ex.Message}", "ERROR");
                PosMessageBox.Show(
                    "Не удалось открыть экранную клавиатуру.\n\n" + ex.Message,
                    "Клавиатура",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        });
    }

    private static void ApplyAdaptiveLayout(bool hideLetters)
    {
        if (CurrentForm?.DataContext is not FrmKeyboardViewModel model)
            return;

        model.LettersVisibility = hideLetters ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void FrmKeyboard_Loaded(object sender, RoutedEventArgs e)
    {
        _model.Timer = new System.Timers.Timer(250);
        _model.Timer.Elapsed += (_, _) =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                var capsLock = (Keyboard.GetKeyStates(Key.Capital) & KeyStates.Toggled) == KeyStates.Toggled;
                var shift = IsShift || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                _model.RefreshIndicators(shift, capsLock);
                _model.UpdateButtons();
            });
        };
        _model.Timer.AutoReset = true;
        _model.Timer.Start();

        _installedLangs.Clear();
        foreach (CultureInfo culture in InputLanguageManager.Current.AvailableInputLanguages)
            _installedLangs.AddLast(culture.Name);

        PositionOnScreen();
    }

    private void PositionOnScreen()
    {
        UpdateLayout();
        var workArea = SystemParameters.WorkArea;
        Top = workArea.Bottom - ActualHeight - 8;
        Left = workArea.Left + Math.Max(0, (workArea.Width - ActualWidth) / 2);
    }

    protected override void OnClosed(EventArgs e)
    {
        _model.Timer?.Stop();
        _model.Timer?.Dispose();
        _model.Timer = null;
        CurrentForm = null;
        IsShift = false;
        base.OnClosed(e);
    }

    private void ChangeLang()
    {
        if (_installedLangs.Count <= 1)
            return;

        var currentLang = InputLanguageManager.Current.CurrentInputLanguage.Name;
        var node = _installedLangs.Find(currentLang);
        if (node == null)
            return;

        var newLang = node.Next?.Value ?? _installedLangs.First!.Value;
        try
        {
            InputLanguageManager.Current.CurrentInputLanguage = CultureInfo.GetCultureInfo(newLang);
        }
        catch
        {
            /* ignore */
        }

        _model.UpdateButtons();
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T parent)
                return parent;
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    private void FrmKeyboard_OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (e.Source is not DependencyObject sourceObj)
                return;
            var button = FindParent<Button>(sourceObj);
            if (button == null)
                return;

            var tagText = button.Tag?.ToString();
            if (tagText == "close")
            {
                Close();
                return;
            }
            if (tagText == "lang")
            {
                ChangeLang();
                return;
            }
            if (!TryGetKeyCode(button.Tag, out var keyCode))
                return;
            if (IsShiftKey(keyCode))
            {
                IsShift = !IsShift;
                _model.RefreshIndicators(IsShift,
                    (Keyboard.GetKeyStates(Key.Capital) & KeyStates.Toggled) == KeyStates.Toggled);
                _model.UpdateButtons();
                return;
            }
            ApplyVirtualKey(keyCode, button);
            IsShift = false;
            _model.RefreshIndicators(false,
                (Keyboard.GetKeyStates(Key.Capital) & KeyStates.Toggled) == KeyStates.Toggled);
        }
        catch
        {
            /* ignore */
        }
    }

    private static bool TryGetKeyCode(object? tag, out int keyCode)
    {
        switch (tag)
        {
            case int code:
                keyCode = code;
                return true;
            case string text when int.TryParse(text, out var parsed):
                keyCode = parsed;
                return true;
            default:
                keyCode = 0;
                return false;
        }
    }

    private static bool IsShiftKey(int keyCode) =>
        keyCode is 160 or 161;

    private void ApplyVirtualKey(int keyCode, Button button)
    {
        switch (keyCode)
        {
            case 8:
                VirtualKeyboardInput.SendBackspace();
                return;
            case 9:
                VirtualKeyboardInput.SendTab();
                return;
            case 13:
                VirtualKeyboardInput.SendEnter();
                return;
            case 20:
                ToggleCapsLock();
                return;
            case 32:
                VirtualKeyboardInput.InsertText(" ");
                return;
            case 46:
                VirtualKeyboardInput.SendDelete();
                return;
        }

        var contentText = button.Content?.ToString();
        if (!string.IsNullOrEmpty(contentText) && contentText.Length == 1)
        {
            VirtualKeyboardInput.InsertText(contentText);
            return;
        }

        if (contentText != null && contentText.All(char.IsDigit))
        {
            VirtualKeyboardInput.InsertText(contentText);
            return;
        }

        var text = FrmKeyboardViewModel.User32Interop.ToAscii(keyCode, IsShift);
        if (!string.IsNullOrEmpty(text))
            VirtualKeyboardInput.InsertText(text);
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const byte VkCapital = 0x14;
    private const uint KeyeventfKeyup = 0x0002;

    private static void ToggleCapsLock()
    {
        keybd_event(VkCapital, 0, 0, UIntPtr.Zero);
        keybd_event(VkCapital, 0, KeyeventfKeyup, UIntPtr.Zero);
    }
}
