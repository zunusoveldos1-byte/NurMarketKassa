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

    private void WhatsApp_Click(object sender, RoutedEventArgs e)
    {
        // Убираем пробелы, чтобы получить чистый международный номер
        string phoneNumber = "+996559340032";

        // Универсальная ссылка, которая открывается и в десктопном приложении, и в веб-версии
        string url = $"https://wa.me/{phoneNumber}";

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось открыть WhatsApp: {ex.Message}", "Ошибка",
                            MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Website_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://nurcrm.kg/");
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            PosMessageBox.Show("Не удалось открыть ссылку.", "Поддержка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
