using System.Windows;

namespace NurMarketKassaSetup;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AppendLog("Готово к установке.");
    }

    private void AppendLog(string text)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => AppendLog(text));
            return;
        }

        LogBox.AppendText(text + Environment.NewLine);
        LogBox.ScrollToEnd();
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        InstallButton.IsEnabled = false;
        try
        {
            await Task.Run(InstallCore);
            AppendLog("Установка завершена.");
            MessageBox.Show("Nur Market Kassa установлена.", "Установка", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppendLog("Ошибка: " + ex.Message);
            MessageBox.Show(ex.Message, "Установка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            InstallButton.IsEnabled = true;
        }
    }

    private void InstallCore() =>
        InstallerEngine.RunInstall(AppendLog, launchAfter: true);

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
