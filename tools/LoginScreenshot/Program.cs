using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NurMarketKassa.Views;

namespace LoginScreenshot;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var output = args.Length > 0
            ? args[0]
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "login-window-screenshot.png"));

        var wpfApp = new Application();
        var window = new LoginWindow
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 80,
            Top = 80
        };

        window.Loaded += (_, _) =>
        {
            window.Dispatcher.BeginInvoke(async () =>
            {
                await Task.Delay(800);
                window.UpdateLayout();
                var width = (int)window.ActualWidth;
                var height = (int)window.ActualHeight;
                var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(window);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                using var stream = File.Create(output);
                encoder.Save(stream);

                window.Close();
                wpfApp.Shutdown();
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        };

        wpfApp.Run(window);
        Console.WriteLine($"Saved: {output}");
    }
}
