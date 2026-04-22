using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace NurMarketKassaSetup;

/// <summary>Копирование встроенных файлов в %LocalAppData%\Programs\NurMarketKassa.</summary>
internal static class InstallerEngine
{
    private static readonly string TargetDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs",
        "NurMarketKassa");

    internal static void RunInstall(Action<string>? log, bool launchAfter = true)
    {
        Directory.CreateDirectory(TargetDir);
        Log(log, "Папка: " + TargetDir);
        ExtractResource(log, "NurMarketKassa.exe", Path.Combine(TargetDir, "NurMarketKassa.exe"));
        ExtractResource(log, "appsettings.json", Path.Combine(TargetDir, "appsettings.json"));
        CreateShortcut(log,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Nur Market Kassa.lnk"),
            Path.Combine(TargetDir, "NurMarketKassa.exe"));
        var startMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Nur Market");
        Directory.CreateDirectory(startMenuDir);
        CreateShortcut(log, Path.Combine(startMenuDir, "Nur Market Kassa.lnk"),
            Path.Combine(TargetDir, "NurMarketKassa.exe"));

        if (launchAfter)
            StartInstalledApp();
    }

    private static void Log(Action<string>? log, string text)
    {
        if (log != null)
            log(text);
    }

    private static void ExtractResource(Action<string>? log, string fileName, string outputPath)
    {
        var asm = Assembly.GetExecutingAssembly();
        var resource = asm.GetManifestResourceNames()
            .FirstOrDefault(x => x.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
        if (resource == null)
            throw new InvalidOperationException("Не найден ресурс: " + fileName);
        using var input = asm.GetManifestResourceStream(resource) ??
                            throw new InvalidOperationException("Не удалось открыть ресурс: " + fileName);
        using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        input.CopyTo(output);
        Log(log, "Скопирован: " + fileName);
    }

    private static void CreateShortcut(Action<string>? log, string shortcutPath, string targetPath)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var shell = Type.GetTypeFromProgID("WScript.Shell");
                if (shell == null)
                    return;
                dynamic ws = Activator.CreateInstance(shell)!;
                dynamic shortcut = ws.CreateShortcut(shortcutPath);
                shortcut.TargetPath = targetPath;
                shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
                shortcut.Save();
                Log(log, "Ярлык: " + shortcutPath);
            }
        }
        catch
        {
            /* без ярлыка установка всё равно валидна */
        }
    }

    private static void StartInstalledApp()
    {
        var exe = Path.Combine(TargetDir, "NurMarketKassa.exe");
        if (File.Exists(exe))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                WorkingDirectory = TargetDir,
            });
        }
    }
}
