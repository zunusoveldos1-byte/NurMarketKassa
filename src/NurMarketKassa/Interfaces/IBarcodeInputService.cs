using System.Windows.Input;

namespace NurMarketKassa.Interfaces;

public interface IBarcodeInputService
{
    event Action<string>? BarcodeScanned;

    void ProcessKeyDown(KeyEventArgs e);
}
