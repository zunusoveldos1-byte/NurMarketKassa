using System.Diagnostics;
using System.Windows;

namespace NurMarketKassa.Views;

public partial class AdminSupportWindow : Window
{
    private const string Phone = "+996555123456";
    private const string Email = "support@nurmarket.kg";

    public AdminSupportWindow()
    {
        InitializeComponent();
    }

    private void Phone_Click(object sender, RoutedEventArgs e) =>
        OpenUrl($"tel:{Phone}");

    private void WhatsApp_Click(object sender, RoutedEventArgs e) =>
        OpenUrl($"https://wa.me/{Phone.TrimStart('+')}");

    private void Email_Click(object sender, RoutedEventArgs e) =>
        OpenUrl($"mailto:{Email}");

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            MessageBox.Show("Не удалось открыть ссылку.", "Поддержка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
