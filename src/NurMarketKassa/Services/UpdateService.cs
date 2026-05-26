using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace NurMarketKassa.Services
{
    public class UpdateManifest
    {
        public string LatestVersion { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
    }

    public class UpdateService
    {
        private readonly HttpClient _http;
        private readonly string _manifestUrl;

        public UpdateService(string manifestUrl, bool ignoreSsl = false)
        {
            _manifestUrl = manifestUrl;
            var handler = new HttpClientHandler();
            if (ignoreSsl)
            {
                handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
            }
            _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        }

        public async Task<UpdateManifest?> CheckAsync()
        {
            if (string.IsNullOrWhiteSpace(_manifestUrl))
                return null;

            try
            {
                // Создаём отдельный запрос с запретом кэширования
                using var request = new HttpRequestMessage(HttpMethod.Get, _manifestUrl);
                request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
                {
                    NoCache = true,
                    NoStore = true
                };

                using var response = await _http.SendAsync(request, CancellationToken.None);
                response.EnsureSuccessStatusCode();
                var manifest = await response.Content.ReadFromJsonAsync<UpdateManifest>();

                if (manifest == null || string.IsNullOrWhiteSpace(manifest.LatestVersion))
                    return null;

                var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
                if (currentVersion == null || !Version.TryParse(manifest.LatestVersion, out var latestVersion))
                    return null;

                return latestVersion > currentVersion ? manifest : null;
            }
            catch
            {
                return null;
            }
        }

        public async Task<bool> DownloadAndRunAsync(string url, IProgress<double>? progress = null)
        {
            try
            {
                // 1. Скачиваем архив во временную папку
                string tempZip = Path.Combine(Path.GetTempPath(), $"nur_update_{Guid.NewGuid():N}.zip");
                using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
                {
                    var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();
                    long? totalBytes = response.Content.Headers.ContentLength;
                    using var stream = await response.Content.ReadAsStreamAsync();
                    using var fileStream = File.Create(tempZip);
                    byte[] buffer = new byte[8192];
                    long totalRead = 0;
                    int bytesRead;
                    while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        totalRead += bytesRead;
                        if (totalBytes.HasValue)
                            progress?.Report((double)totalRead / totalBytes.Value * 100);
                    }
                }

                // 2. Распаковываем в папку Update внутри каталога приложения
                string appFolder = AppDomain.CurrentDomain.BaseDirectory;
                string updateFolder = Path.Combine(appFolder, "Update");
                if (Directory.Exists(updateFolder))
                    Directory.Delete(updateFolder, true);
                ZipFile.ExtractToDirectory(tempZip, updateFolder);
                File.Delete(tempZip);

                // 3. Запускаем новую версию
                string newExe = Path.Combine(updateFolder, "NurMarketKassa.exe");
                if (File.Exists(newExe))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = newExe,
                        WorkingDirectory = updateFolder,
                        UseShellExecute = true
                    });
                    // Возвращаем true, чтобы вызывающий код закрыл приложение
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Update error: {ex.Message}");
                return false;
            }
        }
    }
}