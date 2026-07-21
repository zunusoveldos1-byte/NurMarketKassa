using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using NurMarketKassa.Core.Contracts;
using NurMarketKassa.Interfaces;

namespace NurMarketKassa.Services;

public sealed class KeyboardWedgeBarcodeService : IBarcodeInputService
{
    private const int BarcodeInterkeyMs = 220;
    private const int MinBarcodeLen = 4;
    private const int BarcodeMaxLen = 64;

    private string _barcodeBuf = "";
    private long _barcodeLastTick;

    public event Action<string>? BarcodeScanned;

    public void ProcessKeyDown(KeyEventArgs e)
    {
        var m = Keyboard.Modifiers;
        if (m.HasFlag(ModifierKeys.Control) || m.HasFlag(ModifierKeys.Alt) || m.HasFlag(ModifierKeys.Windows))
            return;

        if (Keyboard.FocusedElement is TextBoxBase or PasswordBox)
            return;

        if (e.Key == Key.Enter || e.Key == Key.Return)
        {
            if (_barcodeBuf.Length >= MinBarcodeLen)
            {
                e.Handled = true;
                var code = _barcodeBuf;
                _barcodeBuf = "";
                BarcodeScanned?.Invoke(code);
            }
            else
            {
                _barcodeBuf = "";
            }

            return;
        }

        var shift = m.HasFlag(ModifierKeys.Shift);
        var ch = KeyToBarcodeChar(e.Key, shift);
        if (ch == null)
            return;

        var now = Environment.TickCount64;
        var delta = now - _barcodeLastTick;
        if (delta < 0 || delta > BarcodeInterkeyMs)
            _barcodeBuf = "";
        _barcodeLastTick = now;

        _barcodeBuf += ch;
        if (_barcodeBuf.Length > BarcodeMaxLen)
            _barcodeBuf = _barcodeBuf.Substring(_barcodeBuf.Length - BarcodeMaxLen);

        e.Handled = true;
    }

    private static string? KeyToBarcodeChar(Key key, bool shift)
    {
        if (key is >= Key.D0 and <= Key.D9)
            return ((char)('0' + (key - Key.D0))).ToString();

        if (key is >= Key.NumPad0 and <= Key.NumPad9)
            return ((char)('0' + (key - Key.NumPad0))).ToString();

        if (key is >= Key.A and <= Key.Z)
        {
            var c = (char)('a' + (key - Key.A));
            if (shift)
                c = char.ToUpperInvariant(c);
            return c.ToString();
        }

        if (key == Key.Space)
            return " ";

        if (key == Key.OemMinus || key == Key.Subtract)
            return "-";

        if (key == Key.OemPeriod || key == Key.Decimal)
            return ".";

        return null;
    }
}
