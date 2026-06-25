using Octokit;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;

namespace NurMarketKassa.Services;

public sealed class UpdateInfo
{
    public string Version { get; init; } = "";
    public string DownloadUrl { get; init; } = "";
    public string FileName { get; init; } = "";
}

public static class UpdateService
{
    private const string Owner = "";
    private const string Repo = "";

    private static readonly Version CurrentVersion = GetCurrentVersion();
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    public static async Task CheckAndPerformUpdateAsync()
    {
        try
        {
            var update = await CheckForUpdateAsync().ConfigureAwait(false);
            if (update == null)
                return;

            if (!await DownloadAndInstallAsync(update).ConfigureAwait(false))
                return;

            System.Windows.Application.Current.Dispatcher.Invoke(System.Windows.Application.Current.Shutdown);
        }
        catch
        {
            // Тихо продолжаем работу при отсутствии сети или ошибках GitHub API.
        }
    }

    public static async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(Owner) || string.IsNullOrWhiteSpace(Repo))
                return null;

            var client = new GitHubClient(new ProductHeaderValue("NurMarketKassa"));
            var release = await client.Repository.Release.GetLatest(Owner, Repo).ConfigureAwait(false);

            if (!TryParseVersion(release.TagName, out var releaseVersion) || releaseVersion <= CurrentVersion)
                return null;

            var installerAsset = release.Assets
                .FirstOrDefault(asset => asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

            if (installerAsset == null || string.IsNullOrWhiteSpace(installerAsset.BrowserDownloadUrl))
                return null;

            return new UpdateInfo
            {
                Version = releaseVersion.ToString(3),
                DownloadUrl = installerAsset.BrowserDownloadUrl,
                FileName = installerAsset.Name
            };
        }
        catch
        {
            return null;
        }
    }

    public static async Task<bool> DownloadAndInstallAsync(UpdateInfo update, IProgress<double>? progress = null)
    {
        try
        {
            var installerPath = Path.Combine(Path.GetTempPath(), update.FileName);

            using (var response = await Http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength;
                await using var contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                await using var fileStream = File.Create(installerPath);

                var buffer = new byte[81920];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead)).ConfigureAwait(false);
                    totalRead += bytesRead;

                    if (totalBytes.HasValue && totalBytes.Value > 0)
                        progress?.Report(totalRead * 100.0 / totalBytes.Value);
                }
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/SILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS",
                UseShellExecute = true
            });

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Version GetCurrentVersion()
    {
        var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
        if (assemblyVersion != null)
            return new Version(assemblyVersion.Major, assemblyVersion.Minor, assemblyVersion.Build);

        return new Version(1, 2, 9);
    }

    private static bool TryParseVersion(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);

        if (string.IsNullOrWhiteSpace(tag))
            return false;

        var normalized = tag.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V'))
            normalized = normalized[1..];

        var plusIndex = normalized.IndexOf('+', StringComparison.Ordinal);
        if (plusIndex >= 0)
            normalized = normalized[..plusIndex];

        return Version.TryParse(normalized, out version!);
    }
}
