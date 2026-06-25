using System.Windows.Input;

namespace NurMarketKassa.Core.Contracts;

public interface IBarcodeInputService
{
    event Action<string>? BarcodeScanned;

    void ProcessKeyDown(KeyEventArgs e);
}
