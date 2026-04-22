using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Input;
using NurMarketKassa;
using NurMarketKassa.Configuration;

namespace NurMarketKassa.Services;

internal static class AppUpdateService
{
    private static readonly JsonSerializerOptions JsonOpt = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static string StateDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NurMarketKassa");

    private static string StatePath => Path.Combine(StateDir, "update-check.json");

    /// <summary>Проверка при старте главного окна (с задержкой и троттлингом).</summary>
    internal static async Task TryOfferUpdateOnStartupAsync(Window owner, UpdateSettings cfg, CancellationToken ct = default)
    {
        if (!cfg.CheckOnStartup || string.IsNullOrWhiteSpace(cfg.ManifestUrl))
            return;
        await TryOfferUpdateAsync(owner, cfg, force: false, ct).ConfigureAwait(true);
    }

    /// <summary>Ручная проверка из настроек — без троттлинга.</summary>
    internal static Task CheckNowAsync(Window owner, UpdateSettings cfg, CancellationToken ct = default) =>
        TryOfferUpdateAsync(owner, cfg, force: true, ct);

    private static async Task TryOfferUpdateAsync(Window owner, UpdateSettings cfg, bool force, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cfg.ManifestUrl))
        {
            MessageBox.Show(
                owner,
                "Обновления не настроены: в appsettings.json не задан Updates.ManifestUrl (или переменная DESKTOP_MARKET_UPDATE_MANIFEST_URL).",
                "Обновление",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!Uri.TryCreate(cfg.ManifestUrl.Trim(), UriKind.Absolute, out var manifestUri) ||
            manifestUri.Scheme != Uri.UriSchemeHttps)
        {
            MessageBox.Show(
                owner,
                "Некорректный URL манифеста обновлений (нужен https).",
                "Обновление",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!force && !ShouldRunThrottledCheck(cfg.MinHoursBetweenChecks))
            return;

        UpdateManifest? manifest;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
            var json = await http.GetStringAsync(manifestUri, ct).ConfigureAwait(true);
            manifest = JsonSerializer.Deserialize<UpdateManifest>(json, JsonOpt);
        }
        catch (Exception ex)
        {
            PosLogger.Log($"Update manifest fetch failed: {ex.Message}", "UPDATE");
            if (force)
            {
                MessageBox.Show(
                    owner,
                    "Не удалось получить манифест обновлений:\n" + ex.Message,
                    "Обновление",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            return;
        }

        if (manifest == null || string.IsNullOrWhiteSpace(manifest.LatestVersion) ||
            string.IsNullOrWhiteSpace(manifest.DownloadUrl))
        {
            if (force)
                MessageBox.Show(owner, "В манифесте нет версии или ссылки на загрузку.", "Обновление", MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            return;
        }

        if (!Uri.TryCreate(manifest.DownloadUrl.Trim(), UriKind.Absolute, out var downloadUri) ||
            downloadUri.Scheme != Uri.UriSchemeHttps)
        {
            if (force)
                MessageBox.Show(owner, "В манифесте указана некорректная ссылка загрузки (нужен https).", "Обновление",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var current = AppVersionInfo.CurrentVersionLabel;
        if (VersionComparer.Compare(manifest.LatestVersion, current) <= 0)
        {
            WriteLastCheckUtc();
            if (force)
            {
                MessageBox.Show(
                    owner,
                    $"Установлена актуальная версия ({current}).",
                    "Обновление",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            return;
        }

        var notes = string.IsNullOrWhiteSpace(manifest.ReleaseNotes) ? "" : "\n\n" + manifest.ReleaseNotes.Trim();
        var ask = MessageBox.Show(
            owner,
            $"Доступна новая версия: {manifest.LatestVersion} (у вас {current}).{notes}\n\nУстановить сейчас? Касса закроется и запустится установщик.",
            "Обновление Nur Market Kassa",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (ask != MessageBoxResult.Yes)
        {
            WriteLastCheckUtc();
            return;
        }

        var targetPath = Path.Combine(Path.GetTempPath(), "NurMarketKassaSetup-update.exe");
        try
        {
            Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            await using (var input = await http.GetStreamAsync(downloadUri, ct).ConfigureAwait(true))
            await using (var output = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await input.CopyToAsync(output, ct).ConfigureAwait(true);
            }

            if (!string.IsNullOrWhiteSpace(manifest.Sha256Hex) &&
                !VerifySha256(targetPath, manifest.Sha256Hex))
            {
                MessageBox.Show(
                    owner,
                    "Контрольная сумма загруженного файла не совпала. Обновление отменено.",
                    "Обновление",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                TryDeleteFile(targetPath);
                return;
            }
        }
        catch (Exception ex)
        {
            Mouse.OverrideCursor = null;
            MessageBox.Show(owner, "Не удалось скачать обновление:\n" + ex.Message, "Обновление", MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }

        try
        {
            LaunchInstallerAfterExit(targetPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, "Не удалось запустить установщик:\n" + ex.Message, "Обновление", MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        App.ExitWithoutLoginRedirect = true;
        Application.Current.Shutdown(0);
    }

    private static bool ShouldRunThrottledCheck(int minHours)
    {
        if (minHours <= 0)
            return true;
        try
        {
            if (!File.Exists(StatePath))
                return true;
            var json = File.ReadAllText(StatePath);
            var st = JsonSerializer.Deserialize<UpdateCheckState>(json, JsonOpt);
            if (st?.LastCheckUtc is not { } utc)
                return true;
            return DateTime.UtcNow - utc >= TimeSpan.FromHours(minHours);
        }
        catch
        {
            return true;
        }
    }

    private static void WriteLastCheckUtc()
    {
        try
        {
            Directory.CreateDirectory(StateDir);
            var st = new UpdateCheckState { LastCheckUtc = DateTime.UtcNow };
            File.WriteAllText(StatePath, JsonSerializer.Serialize(st, JsonOpt));
        }
        catch
        {
            /* ignore */
        }
    }

    private static void LaunchInstallerAfterExit(string setupExePath)
    {
        var batPath = Path.Combine(Path.GetTempPath(), $"NurMarketKassa-run-update-{Guid.NewGuid():N}.cmd");
        // Пауза, чтобы касса успела закрыться и отпустить exe; затем тихая установка поверх каталога в LocalAppData.
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine("ping -n 5 127.0.0.1 >nul");
        sb.Append("start /wait \"\" \"").Append(setupExePath.Replace("\"", "\"\"", StringComparison.Ordinal))
            .AppendLine("\" --silent");
        sb.AppendLine("del /q \"%~f0\"");
        File.WriteAllText(batPath, sb.ToString(), System.Text.Encoding.Default);
        Process.Start(new ProcessStartInfo
        {
            FileName = batPath,
            UseShellExecute = true,
            WorkingDirectory = Path.GetTempPath(),
            WindowStyle = ProcessWindowStyle.Hidden,
        });
    }

    private static bool VerifySha256(string path, string expectedHex)
    {
        var want = NormalizeHex(expectedHex);
        if (want.Length == 0)
            return true;
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        var hash = sha.ComputeHash(fs);
        var got = Convert.ToHexString(hash);
        return string.Equals(got, want, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeHex(string s)
    {
        var t = (s ?? "").Trim().Replace(" ", "", StringComparison.Ordinal);
        return t.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? t[2..] : t;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            /* ignore */
        }
    }

    private sealed class UpdateManifest
    {
        [JsonPropertyName("latest_version")]
        public string? LatestVersion { get; set; }

        [JsonPropertyName("download_url")]
        public string? DownloadUrl { get; set; }

        [JsonPropertyName("release_notes")]
        public string? ReleaseNotes { get; set; }

        [JsonPropertyName("sha256_hex")]
        public string? Sha256Hex { get; set; }
    }

    private sealed class UpdateCheckState
    {
        public DateTime? LastCheckUtc { get; set; }
    }

    private static class VersionComparer
    {
        internal static int Compare(string a, string b)
        {
            var pa = Parse(a);
            var pb = Parse(b);
            var n = Math.Max(pa.Count, pb.Count);
            for (var i = 0; i < n; i++)
            {
                var va = i < pa.Count ? pa[i] : 0;
                var vb = i < pb.Count ? pb[i] : 0;
                if (va != vb)
                    return va.CompareTo(vb);
            }

            return 0;
        }

        private static List<int> Parse(string v)
        {
            var parts = (v ?? "").Trim().Split('.', StringSplitOptions.RemoveEmptyEntries);
            var list = new List<int>(4);
            foreach (var p in parts)
            {
                if (int.TryParse(p, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture,
                        out var n))
                    list.Add(n);
                else
                    break;
            }

            if (list.Count == 0)
                list.Add(0);
            return list;
        }
    }
}
