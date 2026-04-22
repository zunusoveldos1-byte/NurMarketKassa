using System.Linq;
using System.Windows;

namespace NurMarketKassaSetup;

public partial class App : Application
{
    private void Application_Startup(object sender, StartupEventArgs e)
    {
        var args = e.Args ?? Array.Empty<string>();
        var silent = args.Any(a =>
            string.Equals(a, "--silent", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "/silent", StringComparison.OrdinalIgnoreCase));

        if (silent)
        {
            var noLaunch = args.Any(a =>
                string.Equals(a, "--no-launch", StringComparison.OrdinalIgnoreCase));
            try
            {
                InstallerEngine.RunInstall(log: null, launchAfter: !noLaunch);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Ошибка установки: " + ex.Message,
                    "Nur Market Kassa",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            Shutdown(0);
            return;
        }

        var w = new MainWindow();
        MainWindow = w;
        w.Show();
    }
}
