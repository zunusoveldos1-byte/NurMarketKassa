using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace NurMarketKassa.Services;

/// <summary>
/// Распознавание штрих-кода при вводе с экранной клавиатуры (8/12/13 цифр или Enter).
/// </summary>
internal static class VirtualKeyboardBarcodeHandler
{
    private static WpfTextBox? _catalogSearchBox;
    private static WpfTextBox? _barcodeBox;
    private static string? _lastProcessedBarcode;
    private static long _lastProcessedTicks;

    public static Func<WpfTextBox, string, Task>? ProcessBarcodeAsync { get; set; }

    public static void Configure(WpfTextBox catalogSearchBox, WpfTextBox barcodeBox)
    {
        _catalogSearchBox = catalogSearchBox;
        _barcodeBox = barcodeBox;
    }

    public static bool IsEligibleField(WpfTextBox textBox) =>
        ReferenceEquals(textBox, _catalogSearchBox) || ReferenceEquals(textBox, _barcodeBox);

    public static void CheckForBarcodeAndProcess(WpfTextBox textBox, bool forceEnter = false)
    {
        if (!IsEligibleField(textBox) || ProcessBarcodeAsync == null)
            return;

        var text = (textBox.Text ?? "").Trim();
        if (text.Length == 0)
            return;

        if (forceEnter)
        {
            if (!ShouldProcessOnEnter(textBox, text))
                return;
        }
        else if (!IsAutoTriggerBarcode(text))
        {
            return;
        }

        if (IsDuplicate(text))
            return;

        _lastProcessedBarcode = text;
        _lastProcessedTicks = Environment.TickCount64;
        _ = ProcessBarcodeAsync.Invoke(textBox, text);
    }

    public static bool TryHandleEnter(WpfTextBox textBox)
    {
        if (!IsEligibleField(textBox))
            return false;

        var text = (textBox.Text ?? "").Trim();
        if (text.Length == 0)
            return false;

        if (!ShouldProcessOnEnter(textBox, text))
            return false;

        CheckForBarcodeAndProcess(textBox, forceEnter: true);
        return true;
    }

    private static bool ShouldProcessOnEnter(WpfTextBox textBox, string text)
    {
        if (ReferenceEquals(textBox, _barcodeBox))
            return text.Trim().Length > 0;

        if (ReferenceEquals(textBox, _catalogSearchBox))
            return text.Trim().Length > 0;

        return IsAutoTriggerBarcode(text);
    }

    private static bool IsAutoTriggerBarcode(string text) =>
        text.Length is 8 or 12 or 13 && text.All(char.IsDigit);

    private static bool IsDuplicate(string text)
    {
        var now = Environment.TickCount64;
        return string.Equals(text, _lastProcessedBarcode, StringComparison.Ordinal)
               && now - _lastProcessedTicks < 900;
    }
}
