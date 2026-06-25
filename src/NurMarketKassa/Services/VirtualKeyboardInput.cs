using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace NurMarketKassa.Services;

/// <summary>
/// Ввод в активное поле без перехвата фокуса клавиатурой.
/// </summary>
internal static class VirtualKeyboardInput
{
    private static WeakReference? _lastInputTarget;

    /// <summary>Запоминает поле ввода, чтобы клавиатура могла вводить текст без активации окна.</summary>
    public static void RememberInputTarget(IInputElement? element)
    {
        if (element is WpfTextBox or PasswordBox)
            _lastInputTarget = new WeakReference(element);
    }

    public static IInputElement? GetFocusedInputElement()
    {
        if (Keyboard.FocusedElement is WpfTextBox or PasswordBox)
            return Keyboard.FocusedElement;

        var activeWindow = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        if (activeWindow != null)
        {
            var fromManager = FocusManager.GetFocusedElement(activeWindow);
            if (fromManager is WpfTextBox or PasswordBox)
                return fromManager;
        }

        if (_lastInputTarget?.Target is IInputElement cached
            && cached is WpfTextBox or PasswordBox
            && cached is UIElement { IsEnabled: true, IsVisible: true })
            return cached;

        return null;
    }

    public static void InsertText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        switch (GetFocusedInputElement())
        {
            case WpfTextBox textBox:
                InsertIntoTextBox(textBox, text);
                VirtualKeyboardBarcodeHandler.CheckForBarcodeAndProcess(textBox);
                break;
            case PasswordBox passwordBox:
                passwordBox.Password += text;
                break;
        }
    }

    public static void SendBackspace()
    {
        switch (GetFocusedInputElement())
        {
            case WpfTextBox textBox:
                var caret = textBox.CaretIndex;
                if (caret <= 0 || textBox.Text.Length == 0)
                    return;
                textBox.Text = textBox.Text.Remove(caret - 1, 1);
                textBox.CaretIndex = caret - 1;
                VirtualKeyboardBarcodeHandler.CheckForBarcodeAndProcess(textBox);
                break;
            case PasswordBox passwordBox when passwordBox.Password.Length > 0:
                passwordBox.Password = passwordBox.Password[..^1];
                break;
        }
    }

    public static void SendDelete()
    {
        if (GetFocusedInputElement() is not WpfTextBox textBox)
            return;

        var caret = textBox.CaretIndex;
        if (caret >= textBox.Text.Length)
            return;

        textBox.Text = textBox.Text.Remove(caret, 1);
        textBox.CaretIndex = caret;
        VirtualKeyboardBarcodeHandler.CheckForBarcodeAndProcess(textBox);
    }

    public static void SendEnter()
    {
        if (GetFocusedInputElement() is WpfTextBox textBox)
        {
            if (VirtualKeyboardBarcodeHandler.TryHandleEnter(textBox))
                return;

            if (textBox.AcceptsReturn)
            {
                InsertIntoTextBox(textBox, "\n");
                return;
            }

            textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            return;
        }

        if (GetFocusedInputElement() is PasswordBox passwordBox)
            passwordBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
    }

    public static void SendTab()
    {
        if (GetFocusedInputElement() is WpfTextBox textBox && textBox.AcceptsTab)
        {
            InsertIntoTextBox(textBox, "\t");
            return;
        }

        if (GetFocusedInputElement() is UIElement element)
            element.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
    }

    private static void InsertIntoTextBox(WpfTextBox textBox, string text)
    {
        var caret = textBox.CaretIndex;
        if (caret < 0)
            caret = textBox.Text.Length;

        textBox.Text = textBox.Text.Insert(caret, text);
        textBox.CaretIndex = caret + text.Length;
    }
}
